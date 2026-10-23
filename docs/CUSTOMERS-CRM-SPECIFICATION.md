# Customers / CRM module specification

Customer records for the reception desk: who the customer is, how to reach
them, reception notes and the visit counts the staff quote at a glance. This
document is the authoritative description of the module as implemented; the
rules below are enforced in the `DeBliss.Application.Customers` service and
covered by the test suites in `tests/DeBliss.UnitTests` and
`frontend/debliss-web`.

This is the third Phase 1 module. It builds on the foundation laid by the
Services & Pricing and Staff Management & Availability modules, and **does
not duplicate them**: the `Customer` and `CustomerNote` tables existed with
the original database schema, so no new tables were introduced — see
*Concepts* below for how this module uses them.

## Scope

**In scope — implemented:**

- Customer profile CRUD (name, mobile, email, date of birth, gender,
  address, active flag) with a paged, searchable, filterable list
- Activate / deactivate — always a soft status change, never a delete
- Reception notes: add, view and edit, newest first, preserved as history
- Customer summary — total / completed / cancelled appointment counts and
  last visit, read live from the scheduling tables
- Appointment history on the profile — every booking with its date,
  services, staff and status
- Duplicate-mobile detection with an explicit confirm step rather than a
  silent block
- React admin screens for all of the above: the list, the create/edit form
  and the profile page
- The API contract and UI architecture the future appointment screen needs
  for its "+ New Customer" compact form

**Deferred to later modules:** the appointment calendar (the scheduling
tables this module reads stay the system of record the calendar will write
into), billing, packages, loyalty, reviews, offers and product/service
purchase history, authentication and authorisation. Endpoints carry
`<remarks>` naming the permission each will require once authorisation
exists; until then the API is open.

## Concepts

### The customer tables predate this module

`Customer` and `CustomerNote` were created with the original database
schema, so this module **introduced no database changes and no new
migration** — it adds application behaviour over the existing tables. Name,
email, phone number, date of birth, gender, address and the active flag were
already columns; nothing in the schema needed to change.

### One row per person — duplicate detection, not duplicate rejection

A phone number is how the salon recognises a returning customer, so two
profiles sharing one number split the appointment history between them.
When a save carries a mobile number whose digits already belong to another
customer, the API answers `409 Conflict` naming that customer and explaining
the risk; nothing is written. The client shows the message and asks. Saving
again with `allowDuplicatePhone: true` acknowledges the warning and the
record is created or updated — family members genuinely do share a phone,
so the duplicate is a warning with an explicit confirm step, never a silent
block and never an automatic rejection.

Phone numbers are compared by their **digits only**: spaces, dashes,
parentheses and a leading `+` are formatting, so `+971 50 123 4567`,
`971501234567` and `(971) 50-123-4567` are the same number for duplicate
detection. A number must hold at least five digits to be a phone number at
all.

### Notes are corrected, not deleted

A note can be edited but never deleted: reception knowledge (allergies,
preferences, incidents) is part of the customer record the salon relies
on, so the history is preserved rather than removable. The edit keeps the
same row and its timestamp; nothing is appended and nothing is lost.

### History and summary come from the scheduling tables

The customer summary (total, completed, cancelled, last visit) and the
appointment history (date, services, staff, status) are read straight from
the `Appointment`, `AppointmentService` and `AppointmentStatusHistory`
tables — the same records the future appointment calendar will write. The
service names and staff names shown in the history come from the
booking-time snapshots, so a later rename of a service or a staff member
cannot rewrite a past visit. Until the appointment module starts writing
bookings the counts are simply zero and the history is an empty state; the
queries need no change when real data arrives.

The summary DTO leaves room for spending, packages, reviews, offers and
product/service history. The read-only Loyalty tab now loads
`GET /api/customers/{id}/loyalty` and paginated `/loyalty/transactions`: available
points, lifetime earned, gross redeemed, any points debt, and immutable history.
Only Owner and Receptionist can read these internal routes. Customer identity
is reused; there is no manual balance editor or public loyalty endpoint. See
[Loyalty & Rewards](LOYALTY-REWARDS-SPECIFICATION.md) for earning/redemption rules.

### Active / inactive is a soft status, never a delete

Customers are deactivated, not deleted. Booking and financial history will
reference the customer, so the record must stay; the flag only controls
whether the customer appears as a normal choice in future workflows. The
customer's `Id` never changes — every reference a past or future
appointment, invoice or note holds stays valid.

### The "+ New Customer" seam is nullable `isActive`

`SaveCustomerRequest.IsActive` is nullable so the compact create form the
future appointment screen posts can omit the field entirely: on create a
missing flag means *active*; on edit it means *leave the flag as it is*.
The same request shape therefore serves both the full profile form and the
quick-create compact form, with nothing to coordinate between them.

## HTTP API

Base URL `/api`. JSON in both directions, camelCase by ASP.NET Core default.
Guids are `{id:guid}` route values; the `status` filter binds by name
(`All` | `Active` | `Inactive`). Search, filter and paging are applied in
the database, never by loading the table into the client.

### Customers — `/api/customers`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /customers` | Paged search. Query: `search`, `status`, `page`, `pageSize` |
| `POST /customers` | Create a customer; returns `201` + `Location` |
| `GET /customers/{id}` | One customer with the appointment summary the profile renders |
| `PUT /customers/{id}` | Full update of the profile |
| `PATCH /customers/{id}/status` | Body `{ isActive }`; deactivating never deletes, activating requires no extra condition |

`GET /customers` returns a paged envelope (`items`, `page`, `pageSize`,
`totalCount`, `totalPages`, `hasNextPage`, `hasPreviousPage`). `pageSize`
defaults to 50 and is capped at 200; `page` is clamped to at least 1 — the
normalisation happens before the query reaches the database.

### Notes — `/api/customers/{customerId}/notes`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /customers/{customerId}/notes` | The customer's notes, newest first |
| `POST /customers/{customerId}/notes` | Body `{ note }` |
| `PUT /customers/{customerId}/notes/{noteId}` | Correct a note; notes are never deleted |

### Appointments — `/api/customers/{customerId}/appointments`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /customers/{customerId}/appointments` | The customer's past and upcoming appointments, newest first |

### Errors

The API answers failures with an RFC 9457 problem document.

- **400** validation: no `detail`; an `errors` map keyed by request field
  (`firstName`, `lastName`, `email`, `phoneNumber`, `dateOfBirth`,
  `gender`, `address`, `note`). The form renders each message beside its
  input.
- **404**: `detail` naming the missing record.
- **409**: `detail` for business conflicts — the duplicate-mobile warning
  naming the customer who already holds the number, or a note operation on
  a missing note. The client surfaces the duplicate message in a confirm
  dialog and retries with `allowDuplicatePhone: true` only when the user
  explicitly accepts.
- A 400 reports *every* field problem at once; only fully valid input
  reaches the deeper checks.

## Validation rules

### Customer profile

| Field | Rule | Message |
| ----- | ---- | ------- |
| `firstName` | Required after trim; ≤ 100 chars | "First name is required." / "First name must be 100 characters or fewer." |
| `lastName` | Required after trim; ≤ 100 chars | "Last name is required." / "Last name must be 100 characters or fewer." |
| `phoneNumber` | Required after trim; ≤ 30 chars; digits only after normalisation, at least 5 digits | "Mobile number is required." / "Mobile number must be 30 characters or fewer." / "Enter a valid mobile number." |
| `email` | Optional; valid email format; ≤ 256 chars | "Enter a valid email address." / "Email must be 256 characters or fewer." |
| `dateOfBirth` | Optional; not in the future | "Date of birth cannot be in the future." |
| `gender` | Optional; ≤ 20 chars | "Gender must be 20 characters or fewer." |
| `address` | Optional; ≤ 500 chars | "Address must be 500 characters or fewer." |
| duplicate phone | Another customer already holds the digits and `allowDuplicatePhone` is false | 409 "{First} {Last} already uses this mobile number. A second record under the same number can split the appointment history between two profiles. Create the record anyway only if you are sure this is a different person." |
| active change | Deactivating never deletes or rewrites history; activating an inactive customer just flips the flag | — |

### Notes

| Field | Rule | Message |
| ----- | ---- | ------- |
| `note` | Required after trim; ≤ 2000 chars | "A note cannot be empty." / "Note must be 2000 characters or fewer." |
| lifecycle | Editable but never deletable | 404 when the note does not belong to the customer |

Limits live in `CustomerLimits` and mirror the column sizes, so an over-long
value is rejected with a clear 400 instead of failing inside SQL Server.

## Frontend behaviour

React + TypeScript + Vite in `frontend/debliss-web`; the customer module
lives in `src/customers`, reuses the shared components (`StatusPill`,
`LoadingState`, `EmptyState`, `ErrorState`, `ConfirmationDialog`,
`Pagination`, `FieldErrors`) and mirrors the wire types in
`src/api/contracts.ts`. No `any` is used.

- **Customer list** — search by name, mobile or email (applied on Apply),
  an All/Active/Inactive status filter, and a paged table whose rows carry
  the name, mobile, email, status pill and View / Edit /
  Deactivate-Activate actions. Search and paging run in the API and
  database, never client-side over the full table.
- **Customer form** — create/edit profile; every business rule comes back
  from the API as a per-field error beside its input. The create form also
  carries a Notes box whose content is posted as the customer's first note
  right after the customer is created — the customer exists by then, so a
  failed note save must not fail the whole save (a retry would create a
  second customer). Cancel returns to the list (or the profile when opened
  from there).
- **Duplicate warning** — a `409` on save opens a confirm dialog quoting
  the server's message (which names the existing customer). "Save anyway"
  retries the very same draft with `allowDuplicatePhone: true`; Cancel
  keeps the form intact. Nothing is written until the user confirms.
- **Profile page** — a header with the name, mobile, email and
  Edit / Deactivate-Activate actions; a Customer Information panel (phone,
  email, date of birth, gender, address, customer since, status); the
  Customer Summary counts; the Notes panel with Add Note and per-note Edit;
  and the Appointment History panel. Each panel owns its load state, so a
  panel that fails offers Try again without losing the rest of the page.
- **Notes** — listed newest first with their timestamps; Add Note and Edit
  per note (edit asks for no confirmation — a note edit preserves history
  by design). A blank note is caught on the client before any request.
- **Appointment history** — a table of date, services, staff and status.
  Until the appointment module starts writing bookings this panel shows a
  clean "No appointments yet." empty state; it reads the same scheduling
  tables the calendar will write, so it lights up with real data the day
  bookings exist. No fake data is seeded.
- **Data loading** — the same `useAsync` hook and `describeFailure` copy as
  the rest of the app; a failed toggle reports the error inline without
  leaving the page stale.

## Testing

- `tests/DeBliss.UnitTests` — xUnit: the customer application service (run
  against a real SQLite database): list paging, search and status filters,
  profile CRUD and the field rules, the duplicate-phone conflict and its
  confirm-retry, the soft status toggle, note create/edit with its limits,
  and the appointment history/summary queries over the scheduling tables;
  plus API integration tests over an in-memory TestServer covering every
  route above and the RFC 9457 error contract.
- `frontend/debliss-web` — vitest + Testing Library + jsdom: the API
  client, the customers module (list, filters, pagination, create, edit,
  field errors, the duplicate confirm flow, the profile page, notes and
  the empty and failure states).

Every rule in this document has at least one automated test on one side or
the other, and the golden paths were additionally verified by hand in the
browser against a seeded `DeBlissDb`.
