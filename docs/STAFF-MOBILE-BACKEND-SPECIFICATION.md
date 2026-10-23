# Staff Mobile Backend Foundation specification

The API surface and data model that a future **De Bliss Staff App** (Expo +
React Native) will consume, plus the owner's Attendance view that came with it.
This document is the authoritative description of the phase as implemented; the
rules below are enforced in the `DeBliss.Application.Staffing` services and
covered by the test suites in `tests/DeBliss.UnitTests` and
`frontend/debliss-web`.

Staff mobile UI is intentionally deferred. The current phase prepares the existing .NET API and Staff Management system for a future Expo/React Native Staff App.

## Scope

**In scope — implemented:**

- Self-scoped staff endpoints under `api/staff/me` (profile, appointments,
  schedule, attendance) that resolve the staff member from the signed-in user
- Attendance: one check-in/check-out record per staff member per day, stamped
  by the server clock, with the day's planned shift alongside
- The owner's read-only Attendance tab on the existing staff drawer
- Authorization that keeps staff to their own data, attendance visibility with
  the owner, and the receptionist on operational data only
- One EF Core migration: `20261005164948_AddStaffAttendance`

**Deferred, by instruction:** the Expo app itself, any staff web login or
dashboard, performance metrics, the commission engine (the business rule is not
final), salary, salary slips, contracts/documents, payroll, leave management,
monthly hours reporting and any worked-hours total. §"Extension points" below
records where each will plug in.

## Concepts

### The existing Staff record is the only staff identity

This phase introduced **no second staff table, entity, or management page**.
`Staff` keeps its role as the single source of truth, and a staff login links
to it through the existing `Staff.User` relationship:

```
User ── UserRole (Staff) ── Staff.User ── Staff.Id
```

The login flow (unchanged) issues a JWT whose claims include `staff_id` when
the user is linked to a staff record. Everything self-scoped derives from that
claim — **no `/me` endpoint accepts a staff id as a parameter**, so a client
can never name somebody else's record. A signed-in user without the link (the
owner, the receptionist, or a staff user whose record was removed) fails the
`StaffSelf` policy with `403`.

### Attendance is one row per staff member per day

`StaffAttendance` carries the clock-in/clock-out pair and nothing derived:

| Column | Notes |
| --- | --- |
| `Id` | GUID primary key, per solution convention |
| `StaffId` | FK to `Staff.Id`, cascade delete |
| `WorkDate` | `date`; the UTC calendar day the record belongs to |
| `CheckInAt` / `CheckOutAt` | `datetimeoffset`; the server's own stamps |
| `Status` | `CheckedIn` or `CheckedOut`, stored as `nvarchar(20)` |
| `CreatedAt` / `UpdatedAt` | standard audit columns |

A **unique index on (`StaffId`, `WorkDate`)** backs the one-record-per-day rule
at the storage level, not just in service logic. There is no worked-hours
total, break tracking, GPS, or device attestation — timestamps only.

"Today" is the **UTC date** throughout the attendance service (the same
convention the scheduling module uses for dates). A salon-local shift that
crosses midnight UTC therefore lands on two work dates; a later phase may
revisit this if the salon's timezone makes it visible.

### Check-in and check-out are server-stamped state machines

Both actions read the staff id from the token and the timestamp from the
server clock — a client-supplied time is not accepted anywhere on the surface.
The transitions and their `409` responses:

- `POST check-in` while an open record exists →
  `"You are already checked in for today."`
- `POST check-in` after the day is closed →
  `"Today's attendance has already been closed."`
- `POST check-out` with no record → `"Check in before checking out."`
- `POST check-out` on a closed record →
  `"You have already checked out for today."`

Both actions return the day's record (200), so the caller can render it
without a follow-up GET. `GET attendance/today` returns `200` with a `null`
body when the member has not clocked in — absence is not an error.

### History windows are bounded, never truncated

`GET .../attendance` (own history and the owner's view alike) takes optional
`from`/`to` dates. Unspecified, the window defaults to the **last 90 days
ending today**; an inverted range or one wider than **366 days** is refused
with a `400` naming the `to` field rather than silently narrowed.

### The staff's own appointment list is privacy-narrowed

`StaffAppointmentDto` exposes the booking-time snapshot name, duration and
operational note for **only the service lines the member personally performs**
— a shared booking never reveals a colleague's work. It carries no prices, no
customer contact details, no other staff members and no business analytics:
just the customer's display name, the window, the status, and the bundle name
when the appointment is one. Three scopes replace date arithmetic on the
client: `Today` (from the start of the UTC day), `Upcoming` (the next 31
days), `History` (the last 90 days, newest first).

### Shifts stay in the owner's weekly pattern

There is **no `StaffShift` entity**. `GET me/schedule` derives today plus the
following six days from the existing recurring weekly pattern, with dated time
off listed alongside the day it covers, and returns the weekly pattern itself
so the app can render a month without further calls. The owner keeps
maintaining the pattern through the existing Staff Management UI; nothing in
this phase writes to it.

## API surface

All routes are camelCase JSON; errors are RFC 9457 problem documents, with
field messages keyed by request field on validation failures.

| Route | Policy | Notes |
| --- | --- | --- |
| `GET api/staff/me` | StaffSelf | Own profile, deliberately narrower than the owner's staff DTO |
| `GET api/staff/me/appointments?scope=` | StaffSelf | `Today` (default) / `Upcoming` / `History` |
| `GET api/staff/me/schedule` | StaffSelf | Today + 6 days derived from the weekly pattern |
| `GET api/staff/me/attendance/today` | StaffSelf | `204 No Content` when not checked in; the record otherwise |
| `GET api/staff/me/attendance?from&to` | StaffSelf | Default last 90 days; >366 days or inverted → `400` on `to` |
| `POST api/staff/me/attendance/check-in` | StaffSelf | Server timestamp; duplicate → `409` |
| `POST api/staff/me/attendance/check-out` | StaffSelf | Server timestamp; wrong state → `409` |
| `GET api/staff/{staffId}/attendance?from&to` | OwnerOnly | The owner's staff-management view |

### Authorization matrix

Backend-enforced — frontend or mobile restrictions are never the only guard:

| Caller | `me/*` routes | Owner attendance route | Existing staff routes |
| --- | --- | --- | --- |
| Staff (linked) | Own record only | `403` | `403` |
| Owner | `403` (no staff link) | Any staff member | Full management |
| Receptionist | `403` (no staff link) | `403` | Operational read (unchanged) |
| Anonymous | `401` | `401` | `401` |

The owner's attendance route is `OwnerOnly` rather than operational on
purpose: clock records are private staff data, and the front desk keeps
everything it needs for booking from the availability check it already had.

## The owner's Attendance tab

The existing staff drawer (Staff Management → a member) gained an
**Attendance** tab between Time off and Staff details. It is read-only — a
historical clock record is the record of what happened — and shows exactly the
§-required columns: Date, Scheduled shift (from the weekly pattern, e.g.
`10:00–13:00, 14:00–18:00`, or `Not scheduled`), Check-in, Check-out (`—`
while open) and Status. From/To date filters re-query as they change; an empty
result says whether the whole history is empty or just the chosen range. No
totals, no payroll, no editing.

The tab renders only for the owner (`StaffPage` passes `canViewAttendance`
from the signed-in user's roles, matching the `OwnerOnly` policy). A
receptionist opening the same drawer sees the existing tabs — Overview,
Services, Working hours, Time off, Staff details — and never the Attendance
tab or its data.

## Database

Migration `20261005164948_AddStaffAttendance` adds the single table above.
Per project convention, migrations are applied manually; it does not modify
any earlier migration or existing table.

## Testing

- `StaffAttendanceServiceTests` — the state machine (all four conflicts),
  server timestamps, one-record-per-day, window defaults and bounds
- `StaffMobileServiceTests` — profile projection, the three appointment
  scopes and their windows, schedule derivation from the weekly pattern,
  privacy narrowing of shared bookings
- `StaffMobileApiTests` — the authorization matrix above over HTTP, duplicate
  check-in/check-out rejections, range validation, `null`-today semantics
- `DeBlissDbContextModelTests` — the 24-entity census including
  `StaffAttendance`, its unique index and audit columns
- `ApiSurfaceTests` — the new routes and their policies
- Frontend: `AttendanceSection.test.tsx` (states, columns, filters, retry)
  and the extended `StaffDrawer.test.tsx` lazy-load coverage

## Extension points for the deferred phases

- **Performance** — a `GET me/performance` route slots next to the others
  behind `StaffSelf`; no DTO was created for it, so no shape is promised yet.
- **Commission** — deliberately untouched. The rule must come from the
  business first; attendance and appointment data are already enough to
  compute from when it does.
- **Saved salary slips (2026-10-23 extension)** — `GET /api/staff/me/salary-slips?page=1&pageSize=20`
  returns the existing paged envelope with only `id`, `salaryMonth`, `salaryYear`, `payDate`,
  and `createdAt` per item. `GET /api/staff/me/salary-slips/{id}/pdf` returns the exact stored
  historical PDF as an attachment. Both require STAFF plus the StaffSelf policy, reject an
  empty staff GUID with 403, and derive ownership exclusively from the token. Unauthenticated
  requests return 401; wrong roles or missing/invalid links return 403. Another member's PDF
  and a missing PDF return the same sanitized 404. Successful responses use no-store.
  Owner preview/save APIs remain Owner-only; previews are never listed until explicitly saved.
  Separate salary and banking fields remain excluded from mobile DTOs. No schema change is
  required, and attendance still does not automatically determine deductions.
- **The Expo app** — authenticate against `/api/auth/login` (bearer token),
  keep the token, and call only the routes above. `staff_id` never leaves the
  server. Every success shape is a plain camelCase DTO; every failure is a
  problem document, so one error path covers validation (`400`), auth (`401`/
  `403`) and business-rule conflicts (`409`).
