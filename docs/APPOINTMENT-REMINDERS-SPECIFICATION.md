# Appointment Reminders + WhatsApp (Twilio) — specification

This document is the authoritative description of the Appointment Reminders
module: the engine that turns appointment lifecycle events into WhatsApp
messages, the Twilio delivery integration, the customer consent model, and the
operations surfaces that monitor and configure it. Where this document and the
code disagree, this document is corrected or the code is changed — the two are
never allowed to drift apart silently.

The product principle for this module is: **the salon's customers receive
timely, correct, consented transactional messages about their appointments —
and nothing else.** The reminder system exists so the salon stops losing
bookings to forgotten appointments, not to send marketing. Every message is
one of four appointment lifecycle notices; there is no promotional path.

---

## Scope

In scope for this module:

- The four transactional WhatsApp notices: **confirmation**, **timed
  reminders** (one row per configured rule, default 24 h and 2 h before),
  **cancellation** and **reschedule**.
- An owner-configurable rules table (timing + enable/disable per notice type),
  seeded with defaults, editable — never hardcoded in business logic.
- The full appointment lifecycle integration: confirm schedules messages,
  reschedule rebuilds them, cancel/completed/no-show cancel what is pending.
- Customer WhatsApp consent (opt-in / opt-out) recorded per customer, enforced
  at both planning and dispatch time.
- A background dispatch worker with DB-backed idempotency, safe claiming,
  bounded retries and structured failure records.
- The Twilio WhatsApp provider behind a provider-neutral port, Sandbox-first,
  with production sender/template configuration without code changes.
- The Reminders workspace in the admin app: operational history, owner-only
  settings, connection status and the operator test send.
- The appointment details panel reminder section and the customer consent
  control.

Deliberately **not** in scope (do not build these under this module):

- SMS or any channel other than WhatsApp (the port allows adding one later).
- Marketing, promotional or bulk/mass messaging, offers, loyalty, packages,
  gift cards, AI messaging.
- The customer-facing website's Phase 2 messaging features.
- Automatic Meta/Twilio account provisioning or verification.

---

## Architecture

The reminder engine is provider-agnostic. Twilio is an implementation detail
behind one port, and the only project that references the Twilio SDK is
`DeBliss.Infrastructure`.

```
AppointmentService (Application/Scheduling)
        │  plans rows on every lifecycle event
        ▼
ReminderPlanner (Application/Notifications)
        │  writes ReminderStatus.Pending rows
        ▼
AppointmentReminders table  ←─ the work queue AND the historical record
        │  due rows picked up by
        ▼
ReminderDispatchWorker (Infrastructure, BackgroundService)
        │  opens a DI scope per tick, calls
        ▼
ReminderDispatchService (Application/Notifications)
        │  claims safely → re-validates → sends via
        ▼
INotificationProvider (Application/Notifications/Ports)
        │  V1 implementation, registered in DI:
        ▼
TwilioWhatsAppProvider (Infrastructure/Notifications)
        │  Twilio SDK 8.0.2 (the only Twilio reference in the solution)
        ▼
Twilio API → WhatsApp
```

Key layering rules:

- `DeBliss.Application` has **no reference to the Twilio SDK**. It knows only
  `INotificationProvider`, `NotificationMessage` and `NotificationSendResult`.
- All Twilio-specific concerns (SDK client, error codes, ContentSid handling,
  Sandbox free-form fallback) live in
  `backend/DeBliss.Infrastructure/Notifications/`.
- The appointment API never talks to a provider and never waits for a send.
  Planning writes rows in the same unit of work as the booking itself; the
  worker sends asynchronously.
- Replacing Twilio means writing one new `INotificationProvider` and changing
  one DI registration (see *Future provider replacement*).

### Application components

| Component | Responsibility |
| --- | --- |
| `ReminderPlanner` | Turns booking / change / status events into Pending rows. The single place where lifecycle→message rules live. |
| `ReminderDispatchService` | The send loop: claim → re-validate → send → persist outcome, with the retry policy. |
| `ReminderMessageComposer` | Builds the template variables (CustomerName, SalonName, AppointmentDate, AppointmentTime, StaffName, ServiceName, AppointmentId). |
| `ReminderSettingsService` | Rules CRUD (owner), provider status, and the operator test send. |
| `ReminderHistoryService` | The filtered history query. |
| `PhoneNumbers` | E.164 normalisation and log masking. |
| `ReminderFailureReasons` | The staff-readable reason strings stored on rows. |
| `ReminderWorkerIdentity` | Per-process worker id (machine + PID + run guid) used for claims. |

### Infrastructure components

| Component | Responsibility |
| --- | --- |
| `TwilioWhatsAppProvider` | `INotificationProvider` over Twilio's `MessageResource.CreateAsync`. |
| `TwilioWhatsAppOptions` | Raw binding of the `Twilio` configuration section. |
| `ReminderDispatchWorker` | `BackgroundService` that ticks the dispatcher. |
| `AppointmentReminderRepository` / `ReminderRuleRepository` | Persistence incl. the claim operations. |

---

## Database

One EF Core migration adds the whole module:
**`20261005110301_AddAppointmentReminders`**. No existing migration was
modified.

### `AppointmentReminders` (the table is simultaneously the queue and the log)

| Column | Notes |
| --- | --- |
| `Id` | PK, `Guid`. |
| `AppointmentId` | FK → Appointments, cascade (rows go with a genuinely deleted appointment). |
| `CustomerId` | FK → Customers, `Restrict` — history stays readable even if customer records evolve. Always set: reminders are only planned for appointments that have a customer record. |
| `ReminderType` | `Confirmation` / `Reminder` / `Cancellation` / `Rescheduled`, stored as `nvarchar(20)`. |
| `Status` | `Pending` / `Sent` / `Failed` / `Cancelled` / `Skipped`, stored as `nvarchar(20)`. |
| `Channel` | `WhatsApp`, stored as `nvarchar(20)`. |
| `ScheduledAt` | When the message is due. Timed reminders: start − offset. Event notices: the moment they were planned. |
| `SentAt` | When a send succeeded. |
| `AttemptCount` | Total send attempts. |
| `LastAttemptAt` | Last attempt timestamp (success or failure). |
| `ProviderMessageId` | Provider receipt id (Twilio message SID). |
| `FailureReason` | Why the row is Failed/Skipped, why a Cancelled row was dropped, or the latest transient failure while a retry is owed. Capped at 500 chars. |
| `NextAttemptAt` | Earliest time a retry may run; null = due as soon as `ScheduledAt` passes. |
| `ClaimedBy` / `ClaimedAt` | Dispatch claim (see *Idempotency*). |
| `CreatedAt` / audit columns | From `AuditableEntity`. |

`FailureReason` and `ProviderMessageId` are never used to store phone numbers
or credentials — reasons are written for salon staff.

Indexes:

- `(Status, ScheduledAt)` — backs the dispatcher's due scan.
- `(CustomerId)` — customer-scoped history.
- **Unique, filtered `(AppointmentId, ReminderType, ScheduledAt) WHERE Status = 'Pending'`** — the idempotency constraint: at most one pending row per notice at a given trigger time.
- **Unique, filtered `(ProviderMessageId) WHERE ProviderMessageId IS NOT NULL`** — a provider message can never be recorded twice.

Rows are historical records and are **never deleted**. A row that will not be
sent ends `Cancelled` (superseded by the lifecycle) or `Skipped` (not
sendable — e.g. no consent), each with a reason.

### `ReminderRules`

| Column | Notes |
| --- | --- |
| `ReminderType` | One or more rules per type. |
| `OffsetMinutes` | Minutes before the appointment start. Always 0 for the event-based types (confirmation, cancellation, reschedule). |
| `IsEnabled` | Owner toggle. |

Unique index on `(ReminderType, OffsetMinutes)`. The five default rules are
seeded **through the migration** so every environment starts configured
without hardcoding timings in business logic:

| Type | Offset | Enabled |
| --- | --- | --- |
| Confirmation | 0 | yes |
| Reminder | 1440 (24 h) | yes |
| Reminder | 120 (2 h) | yes |
| Cancellation | 0 | yes |
| Rescheduled | 0 | yes |

### `Customers` — consent fields

Four columns added to the existing table:

| Column | Notes |
| --- | --- |
| `WhatsAppOptInStatus` | `Unknown` (default) / `OptedIn` / `OptedOut`, stored as string. |
| `WhatsAppOptInDate` | When consent was recorded (or last renewed). |
| `WhatsAppOptOutDate` | When consent was withdrawn. |
| `WhatsAppOptInSource` | Where consent came from (V1: `"Reception"` — the desk records it). |

`Unknown` **never** counts as consent. The dispatcher sends only when the
status is explicitly `OptedIn`.

---

## Lifecycle

Every appointment mutation funnels through `ReminderPlanner` (called from
`AppointmentService`), so the rules live in exactly one place and the worker
only ever executes rows that were planned.

### Booking

`PlanForBookingAsync` — on creation, and again on any transition to
`Confirmed` (covers `Pending → Confirmed` and `Waitlisted → Confirmed`):

- Appointment without a customer record → no rows (consent lives on the
  customer; a walk-in cannot be messaged).
- Each **enabled** `Confirmation` rule → one event row, due immediately.
- Each **enabled** `Reminder` rule → one timed row at `StartsAt − offset`,
  **unless that moment already passed** (a booking made inside the reminder
  window never fires a late message) or an identical pending row exists.

### Change (edit / drag-reschedule)

`ApplyChangeAsync` receives the previous start and customer:

- **Customer attached to a walk-in** (`previousCustomerId is null`): plan the
  normal set for the appointment's current state; terminal appointments stay
  silent.
- **Customer swapped**: pending rows re-point to the new customer in place.
- **Time changed**: pending timed rows are `Cancelled` with reason *"Cancelled
  because the appointment was rescheduled."*, then rebuilt for the new slot;
  if the `Rescheduled` rule is enabled, one `Rescheduled` notice is queued —
  collapsing onto any still-pending earlier one, so the customer only ever
  gets the final time.
- **Customer removed**: all pending rows are cancelled (*"…customer was
  removed…"*).

### Status transitions

`ApplyStatusChangeAsync`:

| Transition | Effect |
| --- | --- |
| → `Confirmed` | Re-plan the message set (idempotent — duplicates are suppressed). |
| → `Cancelled` | Cancel every pending row; queue the `Cancellation` notice if enabled. |
| → `Completed` | Cancel every pending row (reason recorded). No future messages. |
| → `NoShow` | Cancel every pending row (reason recorded). No future messages. |

### Dispatch-time re-validation

Even a correctly planned row is re-checked at send time; nothing that became
untrue silently sends:

- Appointment cancelled/completed/no-show → row `Cancelled` (the one
  exception: the `Cancellation` notice itself is *supposed* to go out for a
  cancelled appointment).
- Appointment (soft-)deleted → `Cancelled`.
- Timed reminder whose appointment already started → `Skipped`.
- Customer record missing → `Failed`.
- Consent absent or withdrawn → `Skipped` with the matching reason.
- Unnormalisable/missing phone → `Failed`.
- No template configured for the type → `Failed` (production only; Sandbox
  falls back to free-form).

---

## Timing

- Timing is data, not code: the planner reads `ReminderRules`. There is no
  hardcoded offset anywhere in the dispatch path.
- Event notices fire as soon as their planning event lands (their rules carry
  offset 0 and their timing is not editable — the settings API rejects an
  offset edit on them).
- The owner may move a timed rule anywhere between **1 minute and 43 200
  minutes (30 days)** before the start; duplicates per type are rejected.
- Late bookings never create already-passed rows (`ScheduledAt <= now` is
  skipped at planning, and the dispatcher skips a timed row whose appointment
  has started).
- Delivery is *approximately* on time: the worker ticks every
  `Reminders:DispatchIntervalSeconds` (default 60 s, minimum 5 s) after a
  15-second startup delay. A reminder due at 14:00:00 is sent on the first
  tick after that moment. This is a nudge system, not a real-time alarm.
- Everything is `DateTimeOffset` / UTC-crossing-safe: rows store absolute
  instants, so the scheduler is immune to local-time and DST changes.

---

## Customer consent (opt-in / opt-out)

- Consent is per customer, on the customer record, viewed and recorded from
  the customer drawer's **WhatsApp consent** card.
- The desk records it via `PUT /api/customers/{id}/whatsapp-opt-in` with
  `{ "status": "OptedIn" | "OptedOut" | "Unknown" }`; the response is the
  updated customer. Recording an opt-in stamps the date (and renews it on
  repeat), an opt-out stamps `WhatsAppOptOutDate`; the source is recorded as
  `Reception`. Every change is behind an explicit confirmation dialog — one
  stray click cannot silently consent a customer.
- Enforcement happens in **two** places: planning does not assume consent
  (rows still plan, so the history shows *why* nothing was sent), and the
  dispatcher refuses to send unless the status is exactly `OptedIn` —
  `Skipped` with a staff-readable reason otherwise. Staff cannot bypass this
  through any API: there is no per-appointment "send anyway".
- The operator **test send** (`POST /api/reminders/test`) is the one path
  that deliberately ignores customer consent — it takes an arbitrary operator
  phone number, never touches a customer's rows or consent state, and marks
  the message as a test (`CustomerName = "Test Message (ignore)"`).

---

## Twilio WhatsApp integration

- SDK: the official **`Twilio` 8.0.2** NuGet package, referenced **only** by
  `DeBliss.Infrastructure`.
- Sending: `MessageResource.CreateAsync` with `ContentSid` +
  `ContentVariables` (JSON of the template variables) for approved templates;
  `MessagingServiceSid` takes precedence over a `From` number when configured.
  `WhatsAppFrom` accepts `whatsapp:+…` or a bare `+…` and is normalised.
- Template mapping per reminder type resolves, in order: the configured Sid
  for that type → **Sandbox mode only**: the free-form fallback
  (`sandbox:freeform` sentinel, rendered from the same variables) → otherwise
  `null`, which the dispatcher records as *"No WhatsApp template is
  configured for this reminder type."* Production therefore cannot
  accidentally send un-templated after the 24-hour session window closes; the
  Sandbox can be exercised end to end without pre-approved templates.
- Template variables (keep names in sync with the approved templates):
  `CustomerName`, `SalonName`, `AppointmentDate` (`ddd, dd MMM yyyy`,
  invariant), `AppointmentTime` (`HH:mm`), `StaffName` (distinct first names,
  `"our team"` when none), `ServiceName` (the **bundle name** for a bundle
  appointment — never a dump of component services), `AppointmentId`.
- Failure classification for retries lives in the provider: a curated set of
  Twilio error codes is `Permanent` (invalid/unreachable recipient, sender
  not on WhatsApp, account not allowed to message the region, sandbox join
  missing, expired session window without a template, rejected credentials —
  codes 20003, 21211, 21214, 21408, 21606, 21610, 21612, 21614, 63007, 63016,
  63024), throttling and Twilio-side errors (20429, 20500–20503) are
  `Transient`, and anything else falls back to HTTP-status heuristics (429 or
  5xx → transient, else permanent). `ApiConnectionException` and unknown
  exceptions are transient.
- Error reasons are translated into operator-readable text (e.g. 63016 →
  *"The 24-hour WhatsApp session has expired; an approved template is
  required for this message."*), capped at 300 chars in the provider and 500
  on the row.

---

## Configuration

All module configuration is configuration-only — **no code change is needed
to move from Sandbox to production, and no credential ever lives in a
committed file**.

### `Twilio` section (credentials via environment variables only)

| Key | Env var | Meaning |
| --- | --- | --- |
| `Twilio:Enabled` | `Twilio__Enabled` | Master switch. **Default `false`** — a missing configuration never sends anything. |
| `Twilio:Mode` | `Twilio__Mode` | `Sandbox` or `Production`. Missing → `Production` (fail-safe: no free-form fallback). |
| `Twilio:AccountSid` | `Twilio__AccountSid` | Twilio account SID. |
| `Twilio:AuthToken` | `Twilio__AuthToken` | Twilio auth token. |
| `Twilio:WhatsAppFrom` | `Twilio__WhatsAppFrom` | Sender as `whatsapp:+…` — the sandbox number in Sandbox, the business sender in Production. |
| `Twilio:MessagingServiceSid` | `Twilio__MessagingServiceSid` | Optional; when set it takes precedence over `WhatsAppFrom`. |
| `Twilio:AppointmentConfirmationTemplateSid` | `Twilio__AppointmentConfirmationTemplateSid` | ContentSid of the approved confirmation template (Production). |
| `Twilio:AppointmentReminderTemplateSid` | `Twilio__AppointmentReminderTemplateSid` | ContentSid of the approved timed-reminder template. |
| `Twilio:AppointmentCancellationTemplateSid` | `Twilio__AppointmentCancellationTemplateSid` | ContentSid of the approved cancellation template. |
| `Twilio:AppointmentRescheduledTemplateSid` | `Twilio__AppointmentRescheduledTemplateSid` | ContentSid of the approved reschedule template. |

The committed `appsettings.json` contains the section with **empty
placeholders**, `Enabled: false`, and a comment pointing at this document. It
must never contain real values. Supply values per environment through
environment variables (double underscore is the .NET convention for `:`), a
secret store, or `dotnet user-secrets` locally.

### `Reminders` section

| Key | Env var | Meaning |
| --- | --- | --- |
| `Reminders:SalonName` | `Reminders__SalonName` | Value of the `SalonName` template variable. Default `DeBliss`. |
| `Reminders:DispatchIntervalSeconds` | `Reminders__DispatchIntervalSeconds` | Worker tick interval; default 60, minimum 5. |

### Status reporting

`GET /api/reminders/provider` returns only `{ isConfigured, description,
channel }`. The description names *which settings are missing* when
unconfigured — never their values. No endpoint reads or returns credentials.

---

## Twilio Sandbox — manual setup and test procedure

Sandbox-first is the development convention. The Sandbox is a shared Twilio
test sender; **never point it at real salon customers** — use your own phone.

1. Create/log into a Twilio account, open **Messaging → Try it out → Send a
   WhatsApp message** (the Sandbox page). Note the sandbox sender number and
   your join code (`join <two-words>`) — these are **not** committed to any
   file; the operator keeps them locally.
2. From your own WhatsApp, send the join code to the sandbox number and wait
   for the confirmation reply.
3. Configure the API locally (user-secrets or environment), e.g.:

   ```bash
   dotnet user-secrets set "Twilio:Enabled" "true"    --project backend/DeBliss.Api
   dotnet user-secrets set "Twilio:Mode" "Sandbox"    --project backend/DeBliss.Api
   dotnet user-secrets set "Twilio:AccountSid" "<sid>" --project backend/DeBliss.Api
   dotnet user-secrets set "Twilio:AuthToken" "<token>" --project backend/DeBliss.Api
   dotnet user-secrets set "Twilio:WhatsAppFrom" "whatsapp:+<sandbox-number>" --project backend/DeBliss.Api
   ```

   (Or the equivalent `Twilio__*` environment variables.)
4. Run the API and sign in as the owner. **Reminders → Settings** shows the
   connection pill *Connected* and a sandbox description.
5. **Send test message**: enter your own E.164 number, confirm, and check the
   message arrives. In Sandbox mode a missing template falls back to the
   free-form render; the result (success, provider message id, or failure
   reason) is shown on screen and logged with a masked number.
6. Exercise the real flow end to end with a **test customer** whose phone is
   your sandbox-joined number: record *Opted in* consent on the customer,
   book an appointment with the timed rule a few minutes ahead, and watch the
   reminder row move Pending → Sent in the history.

Production refuses un-templated sends, so step 5's free-form fallback
disappearing when `Mode=Production` is expected and correct.

---

## Production prerequisites (Meta / Twilio readiness checklist)

External setup that must be complete before `Mode=Production` can go live.
None of it is automated by the application:

1. **Meta Business Portfolio** verified for the salon business.
2. **WhatsApp Business Account (WABA)** created and linked to the Twilio
   account; Twilio's WhatsApp sender onboarding completed.
3. **Sender number**: the salon's business phone number registered as the
   WhatsApp sender, **phone-verified**, display name approved.
4. **Approved templates**: one approved template per notice type matching the
   variable contract above (`{{CustomerName}}`, `{{SalonName}}`,
   `{{AppointmentDate}}`, `{{AppointmentTime}}`, `{{StaffName}}`,
   `{{ServiceName}}`, `{{AppointmentId}}`). Record each `ContentSid` in the
   four `Twilio__Appointment*TemplateSid` settings. The free-form fallback
   does **not** exist in production: forgetting a Sid makes that notice type
   fail with a clear reason instead of sending non-compliant content.
5. **Opt-in compliance**: the salon must have a real consent process (the
   customer consents, the desk records it; opt-out is honored immediately
   and permanently). WhatsApp policy governs what may be sent and when; the
   24-hour session window means only templates will reliably deliver.
6. **Credentials**: production `AccountSid`/`AuthToken` delivered via the
   environment's secret store; `Enabled=true`, `Mode=Production`,
   `WhatsAppFrom` = the business sender, and HTTPS on the API. Note the Auth
   token **rotates** — the operator must be able to rotate it through config
   without a code change.
7. **Messaging limits & quality rating**: start within the account's daily
   messaging tier; monitor the WhatsApp quality rating and messaging limits
   in the Twilio console. A decline in quality can pause sending — the
   failure reasons (`63024`, `21610`, …) surface the symptoms in the history.

---

## Security

- **Authorization is server-side.** `RemindersController` requires the
  `Operational` policy (any signed-in operations user) for the history, and
  the `OwnerOnly` policy for rules, provider status and the test send. The
  frontend additionally hides the Settings tab from receptionists, but the
  API is the real gate. The customer opt-in endpoint requires an operations
  user per the customers module's policy.
- **Credentials are configuration, never data.** No endpoint reads or returns
  `AccountSid`, `AuthToken` or any template secret; the provider status
  reports only configured/not-configured plus a secret-free description. The
  options object is never logged wholesale.
- **Never log**: auth token, account SID values, API credentials, or full
  customer phone numbers. Phone numbers are masked (`+1••••••67`) in every
  log line; failure reasons written for staff contain no phone numbers.
- **Never commit**: Twilio SIDs/tokens, WhatsApp/Meta credentials, template
  secrets. `.gitignore` already excludes `.env`, `secrets.json`,
  `appsettings.*.Local.json`; the committed `appsettings.json` holds empty
  placeholders only.
- **HTTPS** is required (the API and the Vite proxy already run HTTPS in
  development).
- **No destructive deletion** of reminder history anywhere in the module.
- The test send is owner-only, requires an explicit confirmation, validates
  E.164 before calling the provider, never routes through a customer's rows,
  and cannot bypass consent *for real customers* because it never touches
  customer data at all.

---

## Idempotency

Four layers, all DB-backed — no in-memory state is trusted:

1. **Planning dedupe**: the planner suppresses a planned row when an identical
   pending row already exists (same type at the same trigger time, or any
   still-pending event notice of the same type — re-confirming or
   re-rescheduling never stacks notices).
2. **Unique filtered index** on `(AppointmentId, ReminderType, ScheduledAt)
   WHERE Status = 'Pending'`: even a concurrent double-plan (e.g. a
   double-submitted request) cannot leave two pending duplicates.
3. **Safe claiming**: the dispatcher reads due Pending rows and then
   `TryClaimAsync`s each one — a compare-and-set that wins only while the row
   is still Pending and unclaimed (or holds the exact stale claim the reader
   saw). `ClaimedBy` (machine + PID + run guid) and `ClaimedAt` mark
   ownership; a claim older than the 10-minute lease is treated as abandoned
   (crashed worker) and may be taken over. Two workers — or two overlapping
   ticks — can never both send the same row.
4. **Unique filtered index on `ProviderMessageId`**: a provider receipt can
   never be recorded twice, so a duplicated result write cannot double-count
   history.

Worker restarts, retries, API retries and deployments therefore cannot
produce duplicate messages: any re-plan finds the pending index, and any
re-dispatch finds the claim.

---

## Retry policy

Bounded, symmetric, and recorded per row by the dispatcher:

- **Transient** failures (provider timeouts, Twilio throttling, 5xx) →
  `NextAttemptAt = now + 5 min × AttemptCount`, row stays `Pending`, up to
  **3 attempts total** (`MaxAttempts = 3`).
- **Permanent** failures (invalid number, unknown template, rejected
  credentials, WhatsApp policy rejections) → `Failed` immediately, no retry.
- A permanent-classified reason or the third attempt concludes the row:
  `Status = Failed`, reason stored, `NextAttemptAt = null`.
- Every attempt records `AttemptCount` and `LastAttemptAt`; a successful send
  clears the failure reason and stores `SentAt` + `ProviderMessageId`.
- A provider that *throws* is classified transient (the engine catches and
  converts); `OperationCanceledException` propagates for host shutdown.
- Batches are capped at 50 rows per tick; the next tick picks up the rest, so
  one slow provider can never stall the loop unboundedly. A failing tick is
  logged and the loop continues; it never crashes the host.
- While the provider is not configured (the owner's connection card shows
  *Not configured*), the dispatcher does not claim anything: due rows stay
  `Pending` with zero attempts, untouched. They are picked up normally once
  credentials are configured — an unconfigured deployment never burns
  reminders into `Failed`.

---

## API surface

All under `api/reminders` unless noted. Never returns credentials.

| Endpoint | Policy | Purpose |
| --- | --- | --- |
| `GET /api/reminders/history?status&reminderType&appointmentId&customerId&scheduledFrom&scheduledTo&take` | Operational | The filtered reminder log, newest scheduled first. |
| `GET /api/reminders/rules` | OwnerOnly | The configurable rules. |
| `PUT /api/reminders/rules/{id:guid}` | OwnerOnly | Partial edit: `isEnabled` and/or `offsetMinutes` (timed rules only, 1–43 200, duplicate-timing rejected). |
| `GET /api/reminders/provider` | OwnerOnly | `{ isConfigured, description, channel }` — secret-free connection status. |
| `POST /api/reminders/test` | OwnerOnly | Operator test send; body `{ phoneNumber }`; returns `{ success, providerMessageId, failureReason, providerStatus }`. |
| `GET /api/appointments/{appointmentId:guid}/reminders` | Operational | One appointment's reminder rows, oldest scheduled first (used by the details panel). |
| `PUT /api/customers/{id:guid}/whatsapp-opt-in` | Operational (customers controller) | Records consent; body `{ status }` (`Unknown`/`OptedIn`/`OptedOut`). |

---

## Frontend

- **Reminders workspace** at `/reminders` (sidebar entry for `OWNER` and
  `RECEPTIONIST`): a **History** tab everyone sees — filters by status and
  type, rows show scheduled time, appointment, customer, status pill,
  attempts, sent time and failure reason — and a **Settings** tab only the
  owner is shown: WhatsApp connection card (*Connected* / *Not configured* +
  description), the rules table (enable/disable any rule; edit the timing of
  timed rules with a live duration preview and the 1–43 200 validation), and
  the **Send test message** card (explicit confirmation dialog, result shown
  inline including the provider message id).
- **Appointment details panel** carries a *WhatsApp reminders* section: each
  row's status pill, type, and a timing line (scheduled / sent at / failed
  with last attempt), reloaded after status actions because a status move can
  rebuild the plan.
- **Customer drawer** carries the *WhatsApp consent* card: current state
  (or *Not recorded*), record opt-in / opt-out through a confirmation dialog,
  clear the record back to `Unknown`.
- The API is the real gate for roles; the hidden tab/drawer checks are UX
  only. All UI failures flow through the shared `describeFailure` handling.

---

## Testing procedure

### Automated (no live Twilio — always)

All provider interaction in tests goes through a **fake
`INotificationProvider`**; no test ever constructs the Twilio client or
reaches the network. Backend suites in `tests/DeBliss.UnitTests`:

- `Reminders/ReminderLifecycleTests` — planning on confirm; timing and
  defaults; cancelled/rescheduled/completed/no-show behavior; late booking;
  customer link/unlink.
- `Reminders/ReminderDispatchTests` — opt-out and non-opt-in skips, invalid
  phone, retry/backoff and attempt cap, permanent vs transient, provider
  failure and success, claim/stale-claim idempotency, cancellation at send
  time.
- `Reminders/ReminderMessageComposerTests` — template variables incl. the
  bundle-name rule and invariant formatting.
- `Reminders/ReminderSettingsServiceTests` — rule edits and validation,
  provider status, test-send paths.
- `Reminders/ReminderHistoryServiceTests` — filters and mapping.
- `Api/RemindersApiTests` — endpoint contracts and RBAC (OWNER vs
  RECEPTIONIST vs anonymous), plus the appointment-scoped list.

Frontend (vitest + Testing Library): `RemindersPage.test.tsx` (history,
filters, rules, timing validation, test send), `CustomerWhatsAppOptIn.test.tsx`
(consent card flows), the customer-drawer consent integration test, and the
calendar panel reminders test.

Run everything with:

```bash
# backend
dotnet test tests/DeBliss.UnitTests --artifacts-path <writable dir>
# frontend
cd frontend/debliss-web && npm test && npm run lint && npm run build
```

### Manual (live Sandbox)

Follow the *Twilio Sandbox — manual setup and test procedure* above. Rules:
use only your own phone; **never message real salon customers during
development**; never print or paste a secret value into a report or log; a
test is successful when the message arrives and the row lands `Sent` with a
provider message id (or `Failed` with a truthful, readable reason).

---

## Future provider replacement (and future channels)

The port is the seam. To replace Twilio:

1. Implement `INotificationProvider` in `DeBliss.Infrastructure` (or a new
   infrastructure assembly): `IsConfigured`, a secret-free `Description`,
   `ResolveTemplateId(ReminderType)` and
   `SendNotificationAsync(NotificationMessage)` returning
   `NotificationSendResult`.
2. Change the one DI registration
   (`services.AddSingleton<INotificationProvider, …>()`).
3. Nothing above the port changes: the planner, dispatcher, rules, tables,
   API and UI are provider-independent. Failures must keep being returned as
   results (not thrown) with a `Transient`/`Permanent` classification so the
   retry policy keeps working.

Adding a future channel (e.g. SMS) follows the same shape: add the enum
value, resolve templates per channel, and register a provider that handles
it. That work is explicitly outside this module's scope; this module ships
WhatsApp only.

---

## Out-of-scope guarantees (guardrails)

- No marketing/promotional messaging of any kind, ever, through this engine.
- No bulk/mass send endpoints. Every message traces to one appointment and
  one customer's consent.
- No message content from free text in production — approved templates only.
- No deletion path for reminder history.
- No credential on any wire response or log line.
