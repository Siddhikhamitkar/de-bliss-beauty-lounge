# Appointments & Calendar module — specification

This document is the authoritative description of the Appointments & Calendar
module: the scheduling workflow behind the salon's day-to-day operation and the
calendar workspace the reception team lives in. Where this document and the
code disagree, this document is corrected or the code is changed — the two are
never allowed to drift apart silently.

The product principle for this module is: **Fresha-like operational workflow
and usability + De Bliss premium visual identity + our own implementation.**
The calendar is a dedicated scheduling workspace that uses the viewport, not a
generic admin page with a calendar component dropped into a card. Fresha's
source code, assets, branding and artwork are not used or copied.

---

## Scope

In scope for this module:

- The appointment booking workflow: customer (existing, new or walk-in),
  services with per-line staff assignment, start time and notes.
- The full appointment lifecycle — status transitions from creation through
  completion, cancellation and no-show, with a permanent status history.
- Drag-and-drop rescheduling on the calendar: change time, change staff,
  change duration — every change validated by the backend before it is
  committed, with the UI reverting on rejection.
- The calendar read model: staff columns, bookings and non-bookable markers
  (time off, blocked periods) for a date range, in one query.
- Availability rendering on the grid: working hours, breaks, time off,
  blocked time and off-duty periods are visually distinguished, and the
  backend availability service is the authority over what may be booked.

Deferred to later modules:

- Billing and payment against an appointment (the booking price is shown,
  not collected).
- Waitlist — the `Waitlisted` status exists in the enum and its permitted
  transitions are defined, but no UI creates waitlist entries yet.
- Online / mobile-app booking sources (`AppointmentSource.Online`,
  `MobileApp` are reserved for the future customer-facing website).
- Recurring appointments and appointment reminders.

---

## Concepts

### The appointment and its snapshot lines

An appointment has a customer **or** a walk-in name (never both), a start
time, notes, and one or more *service lines*. Each line snapshots the service
it books — name, duration and price are copied onto the line at booking time —
so later edits to the `Service` catalogue can never rewrite what was actually
booked or charged. The line also carries its assigned staff member.

`EndsAt` is **always computed server-side** as the sum of the line durations
from the start time; the client never sends an end time.

Prices, totals and the customer-facing record all come from the snapshots.

### Status lifecycle

`Pending`, `Confirmed`, `CheckedIn`, `InProgress`, `Completed`, `Cancelled`,
`NoShow`, `Waitlisted`. Every change is recorded in
`AppointmentStatusHistory` with the previous status, the new status, an
optional reason and the timestamp.

The permitted transitions are:

| From         | To                              |
| ------------ | ------------------------------- |
| `Pending`    | `Confirmed`, `Cancelled`        |
| `Confirmed`  | `CheckedIn`, `Cancelled`, `NoShow` |
| `CheckedIn`  | `InProgress`, `Cancelled`       |
| `InProgress` | `Completed`                     |
| `Waitlisted` | `Confirmed`, `Cancelled`        |
| terminal     | *(none)*                        |

`Completed`, `Cancelled` and `NoShow` are **terminal**: such appointments are
part of the historical record. Their details and their status can never be
edited again — any schedule or status change returns `409 Conflict`. The
calendar still renders them, with a subdued treatment and no resize handle.

New appointments are created as `Confirmed` with source `Reception`. Only a
cancellation accepts a reason (500 characters); the reason is stored on the
appointment and on the history row.

### Schedule changes (drag and drop)

A schedule change is a single `PATCH` carrying any subset of `startsAt`,
`staffId` and `durationMinutes` — the same endpoint behind every drag
interaction:

- **Move (vertical drag)** sends `startsAt` only when the day or time changed.
- **Reassign (horizontal drag)** sends `staffId` only when the column changed.
  The new staff member must be assigned to *every* service on the
  appointment, otherwise the change is rejected with a 400 naming the
  unassigned services.
- **Resize** sends `durationMinutes` as the *new total*; the delta is applied
  to the **last** service line, so earlier lines keep their booked durations.
  The result can never drop below 5 minutes total, or below 5 minutes on the
  last line.

An empty patch, a past start time, or a duration below the minimum are 400s.
Once syntax is valid, the backend re-runs the **availability check** for every
distinct staff member on the appointment (excluding the appointment itself)
and returns `409 Conflict` naming the member if any part of the new window
collides with another booking, working-hours, break, time off or blocked
period. Nothing is partially applied: a rejected patch leaves the appointment
exactly as it was.

### The calendar read model

`GET /api/calendar` composes everything the grid renders for a `[from, to)`
range in one response — the grid never stitches data client-side:

- **Columns** are every active staff member, plus anyone with a booking in
  range (a deactivated member's remaining appointments must not vanish);
  active members first, booked-but-inactive members appended, each group in
  display order.
- **Working hours** travel as each member's recurring week pattern
  (Monday-first, `HH:mm` intervals).
- **Markers** are dated, one-off periods: time off and blocked periods. A
  block with no staff id is salon-wide; the per-member queries would repeat
  it per column, so markers are deduplicated by id. Each marker carries its
  `Kind` (`TimeOff` or `Blocked`), its `Type` (the `BlockedTimeType` for
  blocks, the time-off type for absences) and an optional label.

The range is capped at 62 days so no view can ask for an unbounded slice of
history.

### Availability is the authority

The scheduling module reuses the Staffing module's availability service as
the single authority for what may be booked. The grid *renders* working
hours, breaks, time off and blocks; the *backend* decides. Click-to-create in
a shaded (non-working) slot is still validated server-side and rejected with
409 if the window is unavailable — the client-side shading is guidance, not a
guarantee.

---

## HTTP API

All endpoints are JSON over `/api`. Errors follow the RFC 9457 problem
details contract used across the system: validation failures are `400` with
per-field messages, missing records are `404`, lifecycle and availability
conflicts are `409`. Endpoints are deliberately open; each names the
permission it will require once authorisation exists.

| Method | Route                               | Purpose                                                        |
| ------ | ----------------------------------- | -------------------------------------------------------------- |
| GET    | `/api/calendar?from=&to=`           | Calendar read model: columns, working hours, bookings, markers |
| GET    | `/api/appointments/{id}`            | One appointment with its snapshot lines and status history     |
| POST   | `/api/appointments`                 | Book a new appointment (`201 Created` + `Location` header)     |
| PUT    | `/api/appointments/{id}`            | Full update: customer, walk-in, start, services, notes         |
| PATCH  | `/api/appointments/{id}/schedule`   | Partial schedule change: `startsAt`, `staffId`, `durationMinutes` |
| PATCH  | `/api/appointments/{id}/status`     | One step along the transition matrix, with optional reason     |

Permissions named in the endpoint remarks: `Scheduling.ViewCalendar`,
`Scheduling.ViewAppointments`, `Scheduling.CreateAppointment`,
`Scheduling.EditAppointment`, `Scheduling.RescheduleAppointment`,
`Scheduling.ChangeAppointmentStatus`.

### Calendar response

`CalendarDto` carries `From`, `To`, the staff `Columns`, the `Appointments`
in range (mapped to the same `AppointmentDto` the appointments endpoints
return) and the `Markers`. Each column carries the member's id, name, job
title, calendar colour, sort order, `IsActive` and the `WorkingHours` week
pattern. Each marker carries its id, optional staff id, window, `Kind`,
`Type` and `Label`.

Range errors are returned as 400s on `from` / `to`.

### Appointments request shapes

`SaveAppointmentRequest` (POST body, PUT body):
`CustomerId`, `WalkInCustomerName`, `WalkInCustomerPhone`, `StartsAt`,
`Services` (each: `ServiceId`, `StaffId`), `Notes`.

`UpdateScheduleRequest` (schedule PATCH body): any subset of `StartsAt`,
`StaffId`, `DurationMinutes` (the new total).

`SetAppointmentStatusRequest` (status PATCH body): `Status` (name, case
insensitive), `Reason` (cancellations only).

`AppointmentDto` exposes the appointment with its snapshot lines (service
name, duration, price, staff), the computed window, status, source, notes,
cancellation reason and customer summary.

---

## Validation rules

### Booking (create and full update)

| Field                 | Rule                                                            | Message                                              |
| --------------------- | --------------------------------------------------------------- | ---------------------------------------------------- |
| `customerId`          | A customer or a walk-in name is required                        | `Choose a customer or enter a walk-in name.`          |
| `customerId`          | Customer and walk-in name are mutually exclusive                | `Choose either a customer or a walk-in name, not both.` |
| `customerId`          | Must reference an existing customer                             | `Customer '{id}' was not found.` (404)               |
| `walkInCustomerName`  | At most 200 characters                                          | `Walk-in name must be 200 characters or fewer.`       |
| `walkInCustomerPhone` | At most 30 characters                                           | `Walk-in phone must be 30 characters or fewer.`       |
| `startsAt`            | Required                                                        | `Start time is required.`                             |
| `startsAt`            | Cannot be in the past                                           | `Start time cannot be in the past.`                   |
| `services`            | At least one line                                               | `At least one service is required.`                   |
| `services`            | Each service must still be bookable                             | `'{service}' is no longer bookable.`                  |
| `services`            | Each staff member must be assigned to the line's service        | `{First} {Last} is not assigned to '{service}'.`      |
| `notes`               | At most 2000 characters                                         | `Notes must be 2000 characters or fewer.`             |

After validation, the availability gate may reject the booking with
`409 Conflict`: `{First} {Last} is not available at the selected time.`

### Schedule patch

| Field             | Rule                                                       | Message                                              |
| ----------------- | ---------------------------------------------------------- | ---------------------------------------------------- |
| (body)            | At least one field must be supplied                        | `Nothing to change.`                                 |
| `startsAt`        | When supplied, cannot be in the past                       | `Start time cannot be in the past.`                   |
| `durationMinutes` | Total, and the resulting last line, at least 5 minutes     | `Duration must be at least 5 minutes.`                |
| `staffId`         | Must exist                                                 | `Staff member '{id}' was not found.` (404)           |
| `staffId`         | Must be assigned to every service on the appointment       | `{First} {Last} is not assigned to {services}.`       |

Availability is then re-checked (excluding the appointment itself) and a
conflict is `409`: `{First} {Last} is not available at the selected time.`

### Status change

| Field    | Rule                                             | Message                                                                  |
| -------- | ------------------------------------------------ | ------------------------------------------------------------------------ |
| `status` | Required                                         | `Choose a status.`                                                        |
| `status` | Must be a known status name                      | `'{status}' is not a recognised appointment status.`                      |
| `reason` | At most 500 characters                           | `Reason must be 500 characters or fewer.`                                 |
| —        | Terminal appointments are immutable              | `A {status} appointment is part of the historical record and cannot change status.` (409) |
| —        | The move must follow the transition matrix       | `An appointment cannot move from {from} to {to}.` (409)                    |
| —        | Terminal appointments cannot be edited at all    | `A {status} appointment cannot be changed.` (409)                          |

### Calendar range

| Field | Rule                                    | Message                                                |
| ----- | --------------------------------------- | ------------------------------------------------------ |
| `from` | Required                                | `A start date is required.`                             |
| `to`   | Required, after `from`, at most 62 days | `The end date must be after the start date.` / `The calendar range cannot span more than 62 days.` |

---

## Frontend behaviour

The calendar lives at `/calendar` as a **dedicated scheduling workspace**: it
uses the viewport height, has no page-level card wrapper, and keeps the
operational density of professional salon software. The De Bliss identity is
carried by the palette (warm ivory surfaces, blush/dusty-rose and
champagne-gold accents, soft borders and shadows) — not by decoration.

### Header

Compact, operational controls: previous / today / next date, the current
range label, the view switcher (**Day / 3 Days / Week / Month**), search,
staff filter, status filter and service filter, and **+ New Appointment**.
No oversized decorative buttons.

### Time grid (Day, 3 Days, Week)

- One column per staff member per day; each column head shows the colour
  stamp and initial, the name and the working status (e.g.
  `Working 09:00–18:00`, `Off`, `Inactive`). Column heads stay visible while
  the grid scrolls vertically.
- The vertical scale is **1.2 px per minute** and card heights are exactly
  proportional to duration — a 30-minute service is half the height of a
  60-minute one. Cards below a minimum height collapse to a compact
  one-liner instead of being forced to identical heights.
- Working hours render on the ivory surface; outside working hours is
  shaded; time off and blocked periods render as labelled overlays;
  off-duty members' columns are marked. Overlapping bookings in one column
  are packed side-by-side into lanes.
- Appointment cards show customer name, service (with `· n/m` when the
  booking has multiple lines), time, status chip and the staff tint via the
  column colour; status is indicated subtly, not with saturated blocks.

### Interactions

- **Click an empty slot** — opens the booking modal with that staff member,
  date and 15-minute-snapped time prefilled.
- **Click a card** — opens the details panel (the click is suppressed when a
  drag moved more than 4 px).
- **Drag a card vertically** — reschedules the time; the drop snaps to
  5 minutes. Rows that were somehow saved off-grid snap back onto the grid.
- **Drag a card horizontally** — changes the staff member.
- **Drag the bottom handle** — resizes; the new duration snaps to 5 minutes
  and is applied to the last line by the backend.
- A semi-transparent ghost shows the proposed time (and staff) while
  dragging. On drop the frontend sends the patch and **the backend decides**:
  `200` → the calendar refetches; `409`/`400` → the card returns to where it
  was and the server's message is surfaced. Nothing is optimistically
  committed.

### Booking modal

One workflow, no page navigation: customer (search existing, create new, or
walk-in with optional phone) → services (browse by category, search,
multi-select; each row shows duration, price and eligible staff) → staff →
date and time → notes. Field-level server errors render under their inputs;
the availability conflict is surfaced as a 409 message. Service durations
and the start time produce the end time; the backend recomputes it
authoritatively.

### Details panel

A side panel, not a navigation: customer, phone, services with their
snapshots, staff, date, time, duration, price, status, notes and the status
history. Quick actions expose exactly the transitions the matrix permits
from the current status — confirm, check in, start, complete, cancel (with
reason) and no-show — plus edit, which reopens the booking modal. Terminal
appointments show actions disabled with the immutability explained.

### Month view

A month grid where each day lists its appointments compactly (time,
customer, colour-coded by staff); a day click focuses that day in the Day
view. Past and terminal bookings remain visible for scanning history.

---

## Testing

Backend (`tests/DeBliss.UnitTests`):

- EF Core model tests for the appointment, service-line and status-history
  configurations — table and column naming, cascade versus restrict rules,
  enum-to-string conversion, `DateTimeOffset` timestamps — never opening a
  database connection.
- `AppointmentService` behaviour against SQLite: booking creation and
  snapshots, the customer-XOR-walk-in rule, computed end times, the
  transition matrix including terminal immutability, schedule-patch
  semantics (duration to last line, staff to all lines), the availability
  409, past-start rejection and the empty-patch rejection.
- API integration tests over an in-memory `TestServer`: calendar range
  validation and composition, the RFC 9457 error contract, and
  permission-surface drift for the new endpoints.

Frontend (`frontend/debliss-web`, `npm test`):

- `calendarTime` — pixel/minute conversion, snapping rules (15-minute click
  snap, 5-minute drag snap, grip-preserving and result-snapped moves),
  resize arithmetic and lane packing.
- `BookingModal` — the customer/walk-in flow, multi-service selection,
  field-level server errors and the availability conflict surfaced from the
  API client.
- `CalendarPage` — rendering of columns, markers and cards; view switching;
  click-to-create; the 409 revert path (card unchanged, no refetch); and a
  regression test asserting a resize from the bottom handle produces a
  duration-only patch (the handle must not let the card's move handler steal
  the drag).

### Manual browser verification

The module was verified in a real browser (Chromium, via an automated
browser session) against a Development-only demo database seeded at
realistic density: multiple staff, multiple appointments of different
durations, multi-service bookings, mixed statuses, a salon-wide block,
working-hours boundaries and near-overlapping bookings. Verified live:
click-to-create and the full booking modal workflow; the details panel with
a status change; Day / 3 Days / Week / Month rendering; proportional card
heights; lane splitting for overlaps; vertical drag with result snapping
(including an off-grid row snapping back); horizontal staff reassignment
and the availability 409 with the card reverting; resize (before and after
the handle fix); the success path refetching the calendar. Pixel
screenshots could not be captured because the automation browser's visible
viewport was unavailable to the session; the verification therefore used
the live accessibility/DOM tree, the network log (every PATCH and its
status code) and the console, all against the running backend and seeded
data.
