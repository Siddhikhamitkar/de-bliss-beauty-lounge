# Staff Management & Availability module specification

Staff profiles, service capabilities, working schedules, breaks/time-off and
the availability check the salon runs before booking anything. This document
is the authoritative description of the module as implemented; the rules below
are enforced in the `DeBliss.Application.Staffing` services and covered by the
test suites in `tests/DeBliss.UnitTests` and `frontend/debliss-web`.

The original staffing module builds on the foundation and Services & Pricing
module, reusing `Staff`, `StaffService` and the staff lookup. The salary
extension adds private financial fields to `Staff` and historical
`SalarySlips`; it does not implement payment processing or bank transfers.

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

**Authorization:** operational staff endpoints require Owner or Receptionist.
Salary and banking field reads, salary writes, and administration salary-slip endpoints require
Owner. The application has no separate Admin role. Staff may list and download only their
own saved PDF slips through `GET /api/staff/me/salary-slips` and `GET /api/staff/me/salary-slips/{id}/pdf`.
Both require the STAFF role and a nonempty token-derived staff link. The list exposes only document ID,
period, pay date and creation time; ordinary staff-mobile and public-customer responses still contain
no salary amounts or banking fields. PDF documents themselves retain their authorized historical content.

Salary slips are an administrative document utility, not a payroll engine.
Attendance does not automatically calculate deductions; leave deduction is
entered manually. Bank transfers, payment execution and automatic commissions
remain out of scope.

## Concepts

### The staff table predates this module

`Staff`, `StaffService` and the staff lookup were created with the original
database schema. The original profile/scheduling implementation required no
schema change. The salary extension adds six nullable columns to `Staff` and
one `SalarySlips` table through
`20261023144139_AddStaffSalaryAndSalarySlips`; existing staff have unset
financial details, not invented zero salaries or bank information.
The migration was separately approved and applied to the existing
`DeBlissDb-Demo` database on 2026-10-23. No unrelated migrations were applied.
Migrations must not run automatically at startup or target a replacement database.

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

## Salary and historical payslips

### Private staff financial details

The existing Owner staff editor adds Total Salary, Basic Salary, Other
Allowances, IBAN Number, Bank Name and Bank Account Number. The existing
`POST /api/staff` and `PUT /api/staff/{id}` accept an optional `salary` object.
Omitting it preserves existing financial values. Providing it replaces those
six fields atomically with the ordinary staff update. Receptionists submitting
this object receive `403`; their normal staff editing remains available.

The `salary` properties are `totalSalary`, `basicSalary`, `otherAllowances`,
`ibanNumber`, `bankName` and `bankAccountNumber`. Each is nullable. Financial
readback is deliberately separate: `GET /api/staff/{id}/salary` is Owner-only.
Shared staff detail, list, status-change and mobile DTOs do not expose them.

Salary amounts are decimal AED values from zero through 999,999,999.99, with
at most two decimal places. IBAN (50 characters), bank name (200) and account
number (100) are optional text, trimmed only at the edges; leading zeros and
internal formatting are preserved. The agreed Total Salary is stored
independently. A component mismatch is an editor warning, never an implicit
recalculation or overwrite.

### Owner workflow and API

Salary Slips appears under Operations for Owner only. Active employee
selection prefills saved Basic Salary and Other Allowances; house rent,
commission and deductions default to zero. Saved bank information is reviewed
without retyping. Employee ID is entered per slip because the existing Staff
model has no employee-code field. Account name uses the employee's full name;
no sample account holder or banking information is supplied as a default.

| Method & path | Purpose |
| --- | --- |
| `GET /api/staff/{id}/salary` | Private financial details for the staff editor and slip prefilling |
| `POST /api/salary-slips/preview` | Validate and generate PDF plus an encrypted preview token; no database write |
| `POST /api/salary-slips` | Explicitly save `{ previewToken }`; returns `201` |
| `GET /api/salary-slips?staffId&year&page&pageSize` | Paged history, without bank details |
| `GET /api/salary-slips/{id}` | Reopen the saved snapshot |
| `GET /api/salary-slips/{id}/pdf` | Download the original saved PDF bytes |

All these routes require Owner and send no-store cache directives.
Authenticated Receptionist, Staff and Customer requests are forbidden; anonymous
requests are challenged. The creator is taken from the authenticated `sub`,
never from submitted employee or creator fields.

A preview accepts staff ID, salary month/year, pay date, optional employee ID,
working days, total leave, leave without pay, late marks, Basic Salary, House
Rent Allowance, Other Allowance, Commission, Leave Deduction and Other
Deductions. Backend validation is authoritative: month 1–12, salary year
2000–2100, pay-date year 2000–2101, attendance between zero and the calendar
days in the period with at most one decimal place, and integer late marks
within that range. Leave without pay cannot exceed total leave. There is no
inferred working-day/leave policy or automatic salary deduction.

- Total Earnings = Basic + House Rent + Other Allowance + Commission.
- Total Deductions = Leave Deduction + Other Deductions.
- Net Salary = Total Earnings − Total Deductions; a negative net is rejected.
- Agreed Total Salary is a reference, not an enforced monthly net.
- Backend arithmetic uses `decimal`; UI totals use integer cents.

Generate displays the PDF for review without creating a saved record. Back to
edit requires a fresh preview. Save accepts only the protected token, not
editable totals or a caller-supplied snapshot. Tokens expire after 30 minutes
and are bound to their creator; invalid, expired or altered tokens require
regeneration. The protected payload contains both the snapshot and PDF bytes,
so profile changes between preview and save cannot silently change the reviewed
document. No preview tokens or banking values are stored in browser storage.

### Persistence, branding and deployment

`SalarySlips` contains the staff reference, displayed name/employee ID, period,
pay date, attendance, agreed salary, each earning/deduction, computed totals,
displayed bank details, creator, creation timestamp, template version and PDF
bytes. A unique staff/year/month index prevents duplicates, including competing
saves. Duplicates return `409`; there is no overwrite or update/delete endpoint.
The staff foreign key is restrictive, preserving history. Saved downloads read
the stored bytes and never regenerate from current employee details.

The PDF is text-based A4, generated with PDFsharp 6.2.4 (MIT) and embedded
Noto Sans Regular/Bold fonts (SIL Open Font License 1.1). The original De Bliss
logo's eight resource streams are preserved byte-for-byte in a branding-only PDF;
no sample employee or bank details are embedded. Normal slips fit one page;
maximum-length names and identifiers wrap onto additional A4 pages without
truncation. Complex-script shaping is not implemented and requires separate
validation before using scripts that need shaping.

The document includes employee and attendance sections, earnings/deductions,
Net Salary in AED and full banking details when available. List screens exclude
banking details and the editor's read-only banking summary masks account
identifiers. Treat PDFs and database backups as confidential financial records.

ASP.NET Core Data Protection protects preview payloads. Multi-instance hosting
must share and protect its key ring; key loss invalidates outstanding previews,
not saved PDFs. PDF generation must not rely on external services or fonts
installed on the operating system. No production deployment, database reset,
replacement database, business-data seeding or bank transfer is part of this
feature.

### Verification constraints

Salary-specific backend tests use object-list repositories, ephemeral protection
keys and TestServer, without creating a database. The legacy backend suite
contains SQLite-creating fixtures and must not be run under the current
no-new-database instruction. Model/migration inspection may construct EF metadata
without opening a connection. Read-only SQL checks are restricted to the existing
`DeBlissDb-Demo`. The approved additive migration and matching API are active.
Live checks load existing staff and generate an unsaved PDF preview; permanent
salary/profile writes remain reserved for an Owner entering actual business data.

## Testing

Salary verification on 2026-10-23:

- Backend: 195 passed, 0 failed, 0 skipped: 91 salary tests plus 104
  database-free service-import regression tests. This is not the complete
  legacy backend suite; its SQLite-creating fixtures were intentionally excluded.
- Frontend: 491 passed across 47 files, including 71 new salary tests.
  The default worker count exhausted worker startup/time budgets; rerunning
  with `--maxWorkers=2` passed without changing assertions or timeouts.
- API build: 0 errors and 0 warnings. Frontend TypeScript/production build passed;
  lint had 0 errors and the existing `AuthContext.tsx` fast-refresh warning.
- EF model matches the migration. After explicit approval, only the salary
  migration was applied to `DeBlissDb-Demo`: six nullable Staff columns, the
  snapshot table, restrictive foreign key and unique employee/year/month index.
  All 28 existing business-table row counts remained unchanged; six staff remain,
  all new financial fields are unset, and no salary records were inserted.
- Real PDF rendering: normal slip one A4 page; maximum-length slip two A4 pages.
  Text extraction preserved amounts, identifiers and leading zeros. Rendered
  pages were visually inspected; eight original logo resource streams matched.
- Pre-activation browser checks used temporary mocked API persistence and real
  PDF bytes. Compact 339px checks covered prefill, validation, preview without
  save, back/edit/regeneration, token-only save, duplicate errors, read-only
  history/reopen/download, blank new-staff finances and owner edits.
- Post-activation headless Edge checks used real login, API and existing data:
  all six staff salary reads, Owner Staff Edit, salary selection, negative-net
  validation, PDF preview, back/edit and empty history passed. No save was sent.
  Desktop 1440px and mobile 390px form screenshots were visually inspected;
  mobile had no horizontal overflow. Native PDF viewer rendering was not verified.
- Twelve existing read-only API endpoints and Staff, Customers, Services,
  Calendar, Billing and Inventory screens passed without browser console or
  page errors. Receptionist editing and the staff mobile profile still work;
  direct Receptionist/Staff salary requests return `403`.
- The standard frontend on port 5173 initially still reached an outdated API.
  The standard Debug API was rebuilt and restarted on port 5132, preserving
  the existing proxy and session configuration. The same live checks then
  passed on port 5173 with Erica K, including Staff Edit and PDF preview.
  No further migration or business-data writes were performed. Both API
  instances (5132/5138) and frontends (5173/5176) remain available, with
  development seeders disabled.

Reproducible database-free gates:

```bash
dotnet test tests/DeBliss.UnitTests/DeBliss.UnitTests.csproj --configuration SalaryVerification --filter 'FullyQualifiedName~Salary|FullyQualifiedName~ServiceImportServiceTests|FullyQualifiedName~ServiceImportApiTests'
dotnet build backend/DeBliss.Api/DeBliss.Api.csproj --configuration SalaryVerification
npm test --prefix frontend/debliss-web -- --maxWorkers=2
npm run lint --prefix frontend/debliss-web
npm run build --prefix frontend/debliss-web
```

For the remaining real-data persistence walkthrough, an authorized Owner can
edit/reload an existing staff member's real salary/banking fields; open Operations
> Salary slips; select that employee and enter the actual period and amounts;
generate, edit/regenerate, explicitly save, then reopen/download from History.
Change the staff profile and confirm the saved snapshot/PDF remains unchanged;
retry the same period and expect a conflict. A Receptionist/Staff account must
not see financial fields or the salary route, and authenticated direct requests
to the protected salary endpoints must return `403`.
