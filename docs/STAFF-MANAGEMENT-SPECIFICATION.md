# Staff Management & Availability module specification

Staff profiles, service capabilities, working schedules, breaks/time-off and
the availability check the salon runs before booking anything. This document
is the authoritative description of the module as implemented; the rules below
are enforced in the `DeBliss.Application.Staffing` services and covered by the
test suites in `tests/DeBliss.UnitTests` and `frontend/debliss-web`.

This is the second Phase 1 module. It builds on the foundation and the
Services & Pricing module, and **does not duplicate them**: `Staff`,
`StaffService` and the staff lookup existed already, so no new tables were
introduced — see *Concepts* below for how this module uses them.

## Scope

**In scope — implemented:**

- Staff profile CRUD (name, contact details, role, calendar colour,
  sort position, active flag) with a paged, searchable, filterable list
- Activate / deactivate — always a soft status change, never a delete
- Service capability: which services each staff member can perform
  (the other side of the Services module's eligible-staff assignment)
- Recurring weekly working hours, with several intervals per day; the gaps
  between intervals are breaks
- Time off (leave, sick, holiday, training, personal, other) — removable
  until it starts and then historical record
- The availability check — "can this staff member take a slot" and "who can
  perform this service at this time"
- React admin screens for all of the above, plus a working-hours editor

**Deferred to later modules:** the appointment calendar (the availability
service already exposes the overlap seam it will plug into), customers,
billing, authentication and authorisation. Endpoints carry `<remarks>` naming
the permission each will require once authorisation exists; until then the
API is open.

## Concepts

### The staff table predates this module

`Staff`, `StaffService` and the staff lookup were created with the original
database schema, so this module **introduced no database changes and no new
migration** — it adds application behaviour over the existing tables. Job
title, email, phone number and calendar colour were already optional columns;
nothing in the schema needed to change.

### One many-to-many, one source of truth

A staff member ↔ service link lives in exactly one place: the `StaffService`
table. Both modules write through their own use case into that one table:

- Services & Pricing saves a service's whole eligible-staff set when the
  service is edited (`PUT /api/services/{id}` with `eligibleStaffIds`).
- This module saves a staff member's whole service set when their
  capabilities are edited (`PUT /api/staff/{id}/services` with
  `serviceIds`).

Each save **replaces the set**; the two writers never merge. Neither side
owns the table — the row is the truth, whichever use case last wrote it.
Because of that, the staff picker on the service form and the service picker
on the staff form always read fresh state and cannot disagree with the store.

The assignment carries no extra data and has no history. Deleting a
`StaffService` row removes the capability, not the staff member or the
service.

### Working hours and breaks are one model

A staff member's week is a recurring `StaffWorkingHour` row per day. Each day
holds a **list of intervals**, ordered by start time. A break is not stored
as its own record — it is the *gap between two consecutive intervals*, so it
is excluded from availability automatically (see below). Adding a break
means splitting one interval into two.

Two rules keep a day sane: every interval's end is after its start, and no
two intervals overlap. Violations fail with one message per day, prefixed by
the weekday. Saving the week always replaces all seven days at once, so the
week on screen is the week that is stored.

### Time off is historical record

`StaffTimeOff` entries are never physically deleted once they have **started**
— past and in-progress leave is part of the scheduling history the future
calendar reports on. Before the start instant an entry can be deleted; after
it, the delete is a `409`. Edits and new entries are validated (type, window,
reason length) but not time-policed, so back-filling a past sickness record
stays possible while deletion remains the one irreversible-history boundary.

An availability check treats any overlap with a time-off entry as
unavailable, whatever the entry's type.

### Availability is five checks today, one seam for tomorrow

`StaffAvailabilityService.IsAvailableAsync` answers "can this member take the
slot" in one pass, ordered cheap → expensive:

1. **Active** — an inactive member is never available.
2. **Assigned** — for a service-scoped check, the member must be linked to
   that service.
3. **Inside working hours** — the slot must sit inside a single working
   interval of the right weekday. A break (the gap between two intervals)
   therefore excludes any slot crossing it, because no single interval covers
   it. A slot ending exactly at midnight belongs to the previous day.
4. **No overlapping time off**.
5. **No overlapping blocked time**.

(Sixth, once appointments exist: **no overlapping appointment**. The method
is the designed seam — the future Appointment module adds one more source
inside `IsAvailableAsync` rather than the check being redesigned.)

The API exposes two shapes of the same check: *is this member free* and *who
can perform this service then*. Both take a start and end instant; the
service-scoped shape takes a service id. Both use `DateTimeOffset` and
compare instants directly, so wall-clock time is always interpreted in the
caller's timezone and never converted by business logic — the API and the
future customer site stay free to choose their display zone.

## HTTP API

Base URL `/api`. JSON in both directions, camelCase by ASP.NET Core default.
Guids are `{id:guid}` route values; the `status` filter binds by name
(`All` | `Active` | `Inactive`).

### Staff — `/api/staff`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /staff` | Paged search. Query: `search`, `status`, `page`, `pageSize` |
| `POST /staff` | Create a staff member; returns `201` + `Location` |
| `GET /staff/{id}` | One member with contact details and the services they can perform |
| `PUT /staff/{id}` | Full update of the profile |
| `PATCH /staff/{id}/status` | Body `{ isActive }`; deactivating never deletes, activating requires no extra condition |

### Services — `/api/staff/{staffId}/services`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /staff/{staffId}/services` | The services this member can perform, each flagged active or not |
| `PUT /staff/{staffId}/services` | Body `{ serviceIds }`; replaces the member's whole service set |

### Working hours — `/api/staff/{staffId}/working-hours`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /staff/{staffId}/working-hours` | The member's recurring week — seven day schedules, each a list of intervals |
| `PUT /staff/{staffId}/working-hours` | Body `{ days }`; saves the whole week, replacing every day |

### Time off — `/api/staff/{staffId}/time-off`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /staff/{staffId}/time-off` | All entries for the member, newest first |
| `POST /staff/{staffId}/time-off` | Body `{ type, startsAt, endsAt, reason? }` |
| `PUT /staff/{staffId}/time-off/{timeOffId}` | Correct an entry |
| `DELETE /staff/{staffId}/time-off/{timeOffId}` | Remove an entry that has not started; `204`, or `409` once it has |

### Availability — `/api/staff/availability`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /staff/availability?staffId&start&end` | Whether one member is free for the window |
| `GET /staff/availability?serviceId&start&end` | Which members assigned to the service can take it then |

Exactly one of `staffId` / `serviceId` is required — neither or both is a
`400`. `start` and `end` are ISO-8601 instants with offsets; the window is
rejected unless the end is strictly after the start.

### Errors

The API answers failures with an RFC 9457 problem document.

- **400** validation: no `detail`; an `errors` map keyed by request field
  (`firstName`, `lastName`, `email`, `phoneNumber`, `jobTitle`,
  `calendarColor`, `sortOrder`, `serviceIds`, `days`, `type`, `startsAt`,
  `endsAt`, `reason`, `staffId`/`serviceId`/`start`/`end`). The form renders
  each message beside its input; working-hours errors arrive keyed by `days`
  with one `"Monday: …"`-style message per broken day, shown under the
  editor.
- **404**: `detail` naming the missing record.
- **409**: `detail` for business conflicts (time off that has already
  started).
- A 400 reports *every* field problem at once; only fully valid input
  reaches the deeper checks.

## Validation rules

### Staff profile

| Field | Rule | Message |
| ----- | ---- | ------- |
| `firstName` | Required after trim; ≤ 100 chars | "First name is required." / "First name must be 100 characters or fewer." |
| `lastName` | Required after trim; ≤ 100 chars | "Last name is required." / "Last name must be 100 characters or fewer." |
| `email` | Optional; valid email format; ≤ 256 chars | "Enter a valid email address." / "Email must be 256 characters or fewer." |
| `phoneNumber` | Optional; ≤ 30 chars | "Phone number must be 30 characters or fewer." |
| `jobTitle` | Optional; ≤ 100 chars | "Job title must be 100 characters or fewer." |
| `calendarColor` | Optional; `#RGB` or `#RRGGBB` | "Use a colour in the form #RGB or #RRGGBB, for example #E91E63." |
| `sortOrder` | ≥ 0 | "Display position cannot be negative." |
| active change | Deactivating never deletes or rewrites history; activating an inactive member just flips the flag | — |

### Service assignment

| Field | Rule | Message |
| ----- | ---- | ------- |
| `serviceIds` | Duplicate and empty ids are normalised away before saving; every remaining id must exist | "{n} of the selected services no longer exist. Remove them and try again." |

### Working hours

| Field | Rule | Message |
| ----- | ---- | ------- |
| day interval | End after start, no two intervals overlapping; intervals are kept ordered | "Monday: end time must be after the start time." / "Monday: working intervals cannot overlap." |

### Time off

| Field | Rule | Message |
| ----- | ---- | ------- |
| `type` | Required and one of the six types | "Choose a time-off type." / "'{value}' is not a recognised time-off type." |
| `startsAt` | Required — a missing or malformed instant fails model binding with a 400 naming the field | — |
| `endsAt` | After `startsAt` | "End must be after the start." |
| `reason` | Optional; ≤ 500 chars | "Reason must be 500 characters or fewer." |
| lifecycle | Deletable only before its start instant; the delete is the one history boundary | 409 "Time off that has already started is part of the historical record and cannot be deleted." |

Limits live in `StaffingLimits` and mirror the column sizes, so an over-long
or malformed value is rejected with a clear 400 instead of failing inside SQL
Server.

## Frontend behaviour

React + TypeScript + Vite in `frontend/debliss-web`; the staff module lives in
`src/staff`, reuses the shared components (`Avatar`, `StatusPill`,
`LoadingState`, `EmptyState`, `ErrorState`, `ConfirmationDialog`,
`Pagination`, `FieldErrors`) and mirrors the wire types in
`src/api/contracts.ts`. No `any` is used.

- **Staff list** — search by name, role or email (applied on Apply), an
  All/Active/Inactive status filter, and a paged table whose rows carry the
  member's initials disc (backed by their calendar colour), name, role,
  service count, status pill and View / Edit / Deactivate-Activate actions. A
  member with no role shows a dash rather than a blank cell.
- **Staff form** — create/edit profile; every business rule comes back from
  the API as a per-field error beside its input. Cancel returns to the list
  (or the profile when opened from there).
- **Profile page** — the member's contact block (with the calendar-colour
  chip), their services, their working week, and their time off with
  Add/Edit/Delete. Each block has its own load state; a block that fails
  offers Try again without losing the rest of the page.
- **Service assignment** — checkbox list of every service, grouped by
  category, the service's active/bookable state shown per row so a link to a
  retired service is visible; Save replaces the member's set.
- **Working-hours editor** — a read-only week rota (with breaks visible as
  the gaps), and an edit mode with a working-day toggle plus any number of
  HH:mm intervals per day; "Add interval" splits a day further, and removing
  one widens the break. Per-day server errors show under the editor.
- **Time off** — the entries (newest first) with type, span and reason, edit
  and delete per entry (delete asks for confirmation; the 409 case surfaces
  as a banner), and a form for adding or correcting an entry. Blank dates
  are caught on the client before any request.
- **Data loading** — the same `useAsync` hook and `describeFailure` copy as
  the rest of the app; a failed toggle reports the error inline without
  leaving the list stale.

## Testing

- `tests/DeBliss.UnitTests` — xUnit: the staffing application services (run
  against a real SQLite database): profile CRUD and the field rules, the
  replace-set service assignment, the working-hours week save and its
  per-day errors, time-off CRUD with the historical-delete rule, and the
  availability check (working hours and breaks, time off, blocked times,
  inactive staff, unknown services); plus API integration tests over an
  in-memory TestServer covering every route above, the RFC 9457 error
  contract and permission-surface drift.
- `frontend/debliss-web` — vitest + Testing Library + jsdom (65 tests): the
  API client, formatters, pagination, `useAsync`, the service form, and the
  staff module (list, profile form, service assignment, working-hours
  editor, time off, profile page).

Every rule in this document has at least one automated test on one side or
the other, and the golden paths were additionally verified by hand in the
browser against a seeded `DeBlissDb`.
