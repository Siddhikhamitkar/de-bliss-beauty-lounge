# Services & Pricing module specification

The service menu the salon sells: categories, bookable services, the staff
members allowed to perform each one, and the prices charged. This document is
the authoritative description of the module as implemented; the rules below are
enforced in `ServiceCatalogueService` / `ServiceCategoryService` and covered by
the test suites in `tests/DeBliss.UnitTests` and `frontend/debliss-web`.

## Scope

**In scope — implemented:**

- CRUD for service categories (name, description, display position, active flag)
- CRUD for services (name, category, description, duration, price, online-bookable
  flag, display position, active flag, eligible staff)
- Replacing the whole eligible-staff assignment set on every save
- A paged, filterable service list and a flat category list
- The `bookable` projection the future appointment calendar will consume
- React screens for both lists and both editor forms

**Deferred to later modules:** staff management (the staff table is read-only
here), appointments, customers, billing, authentication and authorisation.
Endpoints carry `<remarks>` naming the permission each will require once
authorisation exists; until then the API is open.

## Concepts

### Bookable

A service is *bookable* when all of the following hold
(`Service.CanBeBooked()`):

1. the service is active, **and**
2. its category is active, **and**
3. at least one **active** staff member is assigned to it.

`IsOnlineBookable` is deliberately **not** part of bookability — it controls
whether the future public website may offer the service, not whether the salon
can perform it. The API computes `isBookable` on every read; the UI only
reports it.

A service can therefore be active yet not bookable. That happens only when the
world changes *after* the service was saved: a category is deactivated, or
every assigned staff member leaves. The UI flags this state with a
"not bookable" row warning so it can be triaged; the API does not forbid it,
because deactivating a category or losing staff must not corrupt a saved
service.

### Status changes are soft

Deactivating a service or category hides it from new work but never deletes or
rewrites history — past appointments keep pointing at it. Activating a service
is refused until someone active can perform it.

## HTTP API

Base URL `/api`. JSON in both directions, camelCase by ASP.NET Core default.
Guids are `{id:guid}` route values; the `status` filter binds by name
(`All` | `Active` | `Inactive`).

### Services — `/api/services`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /services` | Paged search. Query: `search`, `categoryId`, `status`, `bookableOnly`, `page`, `pageSize` |
| `GET /services/bookable` | Flat list of bookable services with full details — the calendar's menu |
| `GET /services/{id}` | One service, with its eligible staff |
| `POST /services` | Create; returns `201` + `Location` |
| `PUT /services/{id}` | Full update; `eligibleStaffIds` replaces the assignment set |
| `PATCH /services/{id}/status` | Body `{ isActive }`; activating requires active assigned staff |

### Categories — `/api/service-categories`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /service-categories` | All categories with a live `serviceCount`, optionally `?status=` |
| `GET /service-categories/{id}` | One category |
| `POST /service-categories` | Create; returns `201` + `Location` |
| `PUT /service-categories/{id}` | Full update |
| `PATCH /service-categories/{id}/status` | Body `{ isActive }` |

### Staff lookup — `/api/staff-members`

| Method & path | Purpose |
| ------------- | ------- |
| `GET /staff-members?includeInactive=true` | Staff for the eligible-staff selector. Inactive staff are included (marked) so an assignment to someone who left survives the next save; without them the form would silently drop it |

### Paging and filtering

- `page` is clamped to `>= 1`; `pageSize` defaults to **50**, maximum **200**.
- `search` matches name and description, case-insensitive, trimmed.
- `status` narrows by record state; `bookableOnly` applies the bookability rule
  in SQL so it filters *before* paging.
- Ordering is category position, then service position, then name — the order
  the menu is displayed in.
- The response carries `items`, `totalCount`, `page`, `pageSize`, `totalPages`,
  `hasPreviousPage`, `hasNextPage`; the pager UI is driven entirely by that
  metadata.

### Errors

The API answers failures with an RFC 9457 problem document.

- **400** validation: no `detail`; an `errors` map keyed by request field
  (`name`, `categoryId`, `durationMinutes`, `price`, `sortOrder`,
  `eligibleStaffIds`, `description`). The form renders each message beside its
  input.
- **404**: `detail` naming the missing record.
- **409**: `detail` for business conflicts (duplicate name in a category).
- A 400 reports *every* field problem at once; the duplicate-name check only
  runs once the rest is valid, so 400 wins over 409 when both apply.

## Validation rules

### Service

| Field | Rule | Message |
| ----- | ---- | ------- |
| `name` | Required after trim; ≤ 150 chars | "Service name is required." / "Service name must be 150 characters or fewer." |
| `description` | ≤ 1000 chars; blank becomes `null` | "Description must be 1000 characters or fewer." |
| `categoryId` | Required and must exist | "Choose a category for this service." / "The selected category no longer exists. Choose another one." |
| `durationMinutes` | 1–1440 (a line cannot exceed a day) | "Duration must be greater than zero minutes." / "Duration cannot be more than 1440 minutes." |
| `price` | 0–100,000, at most two decimal places | "Price cannot be negative." / "Price cannot be more than 100,000.00." / "Price can have at most two decimal places." |
| `sortOrder` | ≥ 0 | "Display position cannot be negative." |
| `eligibleStaffIds` | Unknown ids rejected; saving as *active* requires ≥ 1 **active** assignee | "…of the selected staff members no longer exist. Remove them and try again." / "An active service needs at least one active staff member who can perform it." |
| name + category | Unique pair | 409: "A service named '…' already exists in the '…' category." |

`PATCH status` to active without active staff: 400 on `eligibleStaffIds`,
"Assign at least one active staff member who can perform this service before
activating it."

Limits live in `ServiceCatalogueLimits` and mirror the column sizes, so an
over-long or absurd value is rejected with a clear 400 instead of failing
inside SQL Server. The 100,000 price cap sits comfortably below `decimal(18,2)`.

### Category

Name required (≤ 100), description ≤ 500, position ≥ 0; duplicate names are
refused with 409. Deactivating a category does **not** deactivate its services —
they stay active but stop being bookable (rule 2 above).

## Frontend behaviour

React + TypeScript + Vite in `frontend/debliss-web`; `src/api/contracts.ts`
mirrors the wire types field for field.

- **Service list** — search (applied on Apply), category/status filters and the
  Bookable-only checkbox (applied immediately), paged table with duration
  ("1 hr 30 min"), price (always two fraction digits, no currency symbol — the
  catalogue carries no currency), staff count, status, online flag, and an
  Edit / Deactivate-Activate action per row. An active-but-not-bookable row is
  highlighted with a "not bookable" note.
- **Service form** — client-side checks only where the server cannot help
  (blank/non-numeric duration and price, since JSON cannot carry NaN);
  everything else, including every business rule, comes back from the API as
  per-field errors beside the inputs. The eligible-staff picker marks inactive
  staff. `ServiceEditor` loads the record, categories and staff, then hands a
  complete draft to a stateless `ServiceForm`, remounted by key so an id change
  or a save resets it.
- **Category list** — position-ordered table with a live service count per
  category and the same edit/status-toggle pattern.
- **Data loading** — `useAsync` keeps the latest loader in a ref (so inline
  closures don't restart the request every render) and re-fetches when its
  dependency snapshot changes; failures render a plain sentence produced by
  `describeFailure` ("Cannot reach the De Bliss API. Is the backend running?"
  when fetch rejects with a `TypeError`).
- **Dev proxy** — Vite forwards `/api` to `https://localhost:7019` with
  `secure: false` to accept the ASP.NET Core development certificate.

## Testing

- `tests/DeBliss.UnitTests` — xUnit: EF model shape, solution architecture
  rules, ServiceCatalogue service tests against SQLite, and API integration
  tests over an in-memory TestServer covering endpoints, the error contract,
  and permission-surface drift.
- `frontend/debliss-web` — vitest + Testing Library + jsdom (30 tests): the API
  client (`toQueryString`, `ApiError`, `describeFailure`, `request`), formatters,
  pagination, `useAsync`, and the service form's client- and server-side error
  paths.

Every rule in this document has at least one automated test on one side or the
other, and the golden paths were additionally verified by hand in the browser
against a seeded `DeBlissDb`.
