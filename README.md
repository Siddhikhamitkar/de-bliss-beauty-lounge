# De Bliss Beauty Lounge

Salon management and customer experience platform.

This repository currently contains **the project foundation, the core database
layer and the first six business modules — Services & Pricing** (service
categories, services, prices and eligible-staff assignment), **Staff
Management & Availability** (staff profiles, service capabilities, working
schedules, breaks/time-off and the availability check), **Customers /
CRM** (customer profiles, duplicate-aware quick create, reception notes and
the visit summary drawn from the scheduling tables), **Appointments &
Calendar** (the scheduling workspace, the booking workflow and the full
appointment lifecycle), **Billing & Payments** (the checkout workflow,
coupons, manual discounts, payments and receipts) **and the Receptionist
Dashboard** (a role-scoped landing page: today's schedule, workload cards,
quick actions, team status and customer alerts — appointment workload only,
never money) **and Appointment Reminders** (the WhatsApp reminder engine:
lifecycle-driven confirmation/reminder/cancellation/reschedule notices,
customer consent, a background dispatcher with retries and idempotency, and
the Twilio provider behind a provider-neutral port), each with its HTTP API,
React admin UI and tests.
Authentication and the customer-facing website are deliberately not
implemented yet.

The authoritative description of the Services & Pricing module is
[docs/SERVICES-MODULE-SPECIFICATION.md](docs/SERVICES-MODULE-SPECIFICATION.md);
the Staff Management & Availability module is described in
[docs/STAFF-MANAGEMENT-SPECIFICATION.md](docs/STAFF-MANAGEMENT-SPECIFICATION.md);
the Customers / CRM module is described in
[docs/CUSTOMERS-CRM-SPECIFICATION.md](docs/CUSTOMERS-CRM-SPECIFICATION.md);
the Appointments & Calendar module is described in
[docs/APPOINTMENTS-CALENDAR-SPECIFICATION.md](docs/APPOINTMENTS-CALENDAR-SPECIFICATION.md);
the Billing & Payments module is described in
[docs/BILLING-PAYMENTS-SPECIFICATION.md](docs/BILLING-PAYMENTS-SPECIFICATION.md);
the Appointment Reminders module is described in
[docs/APPOINTMENT-REMINDERS-SPECIFICATION.md](docs/APPOINTMENT-REMINDERS-SPECIFICATION.md);
the staff mobile backend foundation (self-service API + attendance) is described in
[docs/STAFF-MOBILE-BACKEND-SPECIFICATION.md](docs/STAFF-MOBILE-BACKEND-SPECIFICATION.md);
the Excel import for customers and inventory is described in
[docs/EXCEL-IMPORT-SPECIFICATION.md](docs/EXCEL-IMPORT-SPECIFICATION.md).

The system is built as a **modular monolith**: a single deployable backend
composed of independent, cleanly separated modules, with a layered architecture
inside it. It is deliberately *not* a microservices system.

---

## Repository layout

```
DeBlissSystem/
├── DeBliss.sln                     # Solution containing all .NET projects
├── dotnet-tools.json               # Local tool manifest (dotnet-ef)
├── README.md
├── .gitignore
├── backend/
│   ├── DeBliss.Api/                # ASP.NET Core Web API — composition root / HTTP entry point
│   ├── DeBliss.Application/        # Use cases, ports, application services
│   ├── DeBliss.Domain/             # Core model — depends on nothing
│   └── DeBliss.Infrastructure/     # EF Core DbContext, configurations, migrations, SQL Server
├── frontend/
│   └── debliss-web/                # React + TypeScript + Vite single-page app
├── tests/
│   └── DeBliss.UnitTests/          # xUnit test project
└── docs/                           # Project documentation
```

In Visual Studio the projects are grouped into `backend` and `tests` solution
folders that mirror the on-disk layout.

---

## The backend projects

### DeBliss.Domain
The innermost layer. Holds the core business model — entities, value objects,
domain events and domain rules — expressed in plain C# with no dependency on
the framework, the database, or any other project in the solution.

**References nothing.** This is the invariant the whole architecture rests on:
because Domain has no dependencies, everything else can change (database,
delivery mechanism, third-party services) without the core model being touched.

Holds the nineteen core entities behind the salon's booking and billing model,
grouped into folders that match the bounded contexts they belong to:

| Folder        | Entities                                                        |
| ------------- | --------------------------------------------------------------- |
| `Common`      | `Entity`, `AuditableEntity`, `ISoftDeletable` (base types)      |
| `Identity`    | `User`, `Role`, `UserRole`                                      |
| `Customers`   | `Customer`, `CustomerNote`                                      |
| `Staffing`    | `Staff`, `StaffService`, `StaffWorkingHour`, `StaffTimeOff`     |
| `Services`    | `ServiceCategory`, `Service`                                    |
| `Scheduling`  | `Appointment`, `AppointmentService`, `AppointmentStatusHistory`, `BlockedTime` |
| `Billing`     | `Invoice`, `InvoiceItem`, `Payment`, `Coupon`                   |

Every entity carries a `Guid` primary key. Money is always `decimal`, never
`float`/`double`, and points in time are `DateTimeOffset`. Booking history and
financial records implement `ISoftDeletable` so they are never physically
removed.

### DeBliss.Application
Use-case / orchestration layer. This is where a business operation ("book an
appointment") is defined as a command or query plus its handler, together with
the *ports* (interfaces) it needs — e.g. a repository contract — without knowing
how those ports are implemented.

References **Domain** only, so application logic stays free of infrastructure
concerns.

Besides the persistence-facing ports (`IUnitOfWork`, repository contracts in
`ServiceCatalogue/Ports`), this layer hosts the business modules:
`ServiceCatalogue` — the use cases for managing the service menu
(`ServiceCatalogueService`, `ServiceCategoryService`); `Staffing` — staff
profiles, service assignment, working hours, time off and the availability
check; `Customers` — customer profiles, duplicate-aware creation, notes
and the appointment-history queries; `Scheduling` — the booking
workflow, the appointment lifecycle and the calendar read model
(`AppointmentService`, `CalendarService`); and `Billing` — the checkout
workflow from a completed appointment to a paid invoice, coupon validation
and the receipt (`InvoiceService`, `CouponService`). Each carries its DTOs
and the validation rules and limits the module enforces.

### DeBliss.Infrastructure
Adapter layer. Implements the ports declared by Application: EF Core
`DbContext`, SQL Server repositories, and any external service clients.

References **Application** (to implement its interfaces) and **Domain** (to map
and persist its types).

This is the only project that carries the Entity Framework Core and SQL Server
packages. It contains:

- `Persistence/DeBlissDbContext` — the `DbContext` and `IUnitOfWork`
- `Persistence/Configurations/**` — one `IEntityTypeConfiguration<T>` per entity
- `Persistence/Interceptors/AuditableEntityInterceptor` — stamps `CreatedAt` /
  `UpdatedAt` on save using `TimeProvider`
- `Persistence/DesignTimeDbContextFactory` — lets the EF Core tools build a
  context without starting the API
- `Persistence/Migrations/**` — the Code First migrations
- `DependencyInjection.AddInfrastructure(...)` — registers everything above

No schema is ever written by hand: the database is generated from the C# model
through EF Core migrations.

### DeBliss.Api
The ASP.NET Core Web API and the application's composition root. It exposes HTTP
endpoints, wires up dependency injection, and hosts the application.

References **Application** (to dispatch use cases) and **Infrastructure** (to
register concrete implementations). This is the only place where the outermost
and innermost layers meet, and the dependency direction still points inward.

Currently exposes:

- `GET /health` — health check endpoint, including a live check that SQL Server
  is reachable
- `/openapi/v1.json` — OpenAPI document, development environment only
- `/api/services`, `/api/service-categories`, `/api/staff-members` — the
  Services & Pricing module (see
  [docs/SERVICES-MODULE-SPECIFICATION.md](docs/SERVICES-MODULE-SPECIFICATION.md)
  for endpoints, rules and the error contract)
- `/api/staff` — the Staff Management & Availability module: profiles, service
  assignment, working hours, time off and the availability check (see
  [docs/STAFF-MANAGEMENT-SPECIFICATION.md](docs/STAFF-MANAGEMENT-SPECIFICATION.md)).
  `/api/staff-members` remains the read-only lookup the service form uses
- `/api/staff/me/*` — the staff mobile backend foundation: a linked staff
  user's own profile, appointments, weekly schedule and attendance
  (check-in/check-out, server-stamped), plus the owner's per-staff attendance
  history route. The staff's own view is privacy-narrowed — no prices, no
  other staff, no business analytics (see
  [docs/STAFF-MOBILE-BACKEND-SPECIFICATION.md](docs/STAFF-MOBILE-BACKEND-SPECIFICATION.md))
- `/api/customers` — the Customers / CRM module: paged search, profiles,
  the soft status toggle, reception notes and the appointment history and
  visit summary the profile page renders (see
  [docs/CUSTOMERS-CRM-SPECIFICATION.md](docs/CUSTOMERS-CRM-SPECIFICATION.md))
- `/api/calendar`, `/api/appointments` — the Appointments & Calendar module:
  the calendar read model (staff columns, working hours, bookings and
  non-bookable markers), booking, full updates, schedule patches for
  drag-and-drop and the status lifecycle (see
  [docs/APPOINTMENTS-CALENDAR-SPECIFICATION.md](docs/APPOINTMENTS-CALENDAR-SPECIFICATION.md))
- `/api/invoices`, `/api/coupons`, `/api/payments` — the Billing & Payments
  module: the paged invoice list with filters, invoice detail, starting and
  resuming checkout, the atomic checkout with coupon and manual discount,
  the receipt, coupon administration and payment records (see
  [docs/BILLING-PAYMENTS-SPECIFICATION.md](docs/BILLING-PAYMENTS-SPECIFICATION.md))
- `/api/dashboard/receptionist` — the Receptionist Dashboard module: one
  server-aggregated, role-scoped snapshot of the front desk (today's
  schedule, workload counts, upcoming, team status, customer alerts,
  popular-services workload). The endpoint deliberately carries no money —
  nothing owner-level can leak onto the front desk; the role separation it
  enforces today (no revenue/profit/marketing fields exist in its contract)
  is documented in the controller `<remarks>` as the standing-in for the
  permission checks that arrive with authorisation.

Endpoints are deliberately open: each carries a `<remarks>` naming the
permission it will require once authorisation exists.

Development URLs are configured in `Properties/launchSettings.json`:
`https://localhost:7019` and `http://localhost:5132`.

### Dependency direction

```
        ┌──────────────┐
        │ DeBliss.Api  │
        └──┬────────┬──┘
           │        │
           ▼        ▼
┌────────────────┐  │
│  Application   │  │
└───────┬────────┘  │
        │           ▼
        │   ┌────────────────┐
        │   │ Infrastructure │
        │   └───┬────────┬───┘
        ▼       │        │
   ┌─────────┐  ▼        │
   │ Domain  │◀──────────┘
   └─────────┘
```

Dependencies only ever point **inward**, toward Domain. Domain references
nothing; Application references only Domain.

### Project references

| Project               | References                            |
| --------------------- | ------------------------------------- |
| `DeBliss.Api`         | `DeBliss.Application`, `DeBliss.Infrastructure` |
| `DeBliss.Application` | `DeBliss.Domain`                      |
| `DeBliss.Domain`      | *(none)*                              |
| `DeBliss.Infrastructure` | `DeBliss.Application`, `DeBliss.Domain` |
| `DeBliss.UnitTests`   | all four backend projects             |

---

## The frontend

`frontend/debliss-web` is a React single-page application built with Vite and
written in TypeScript with strict mode enabled.

It implements the Services & Pricing admin screens: the paged, filterable
service list (search, category/status filters, Bookable-only, pagination),
the service editor with per-field server errors and the eligible-staff picker,
and the category list/editor.

It also implements the Staff Management & Availability admin screens: the
paged staff list (search, status filter, activate/deactivate), the staff
profile editor, the member profile page with service assignment,
working-hours editor (multiple intervals per day, breaks as gaps) and time
off.

And the Customers / CRM admin screens: the paged customer list (search,
status filter, activate/deactivate), the create/edit form with per-field
server errors and the duplicate-mobile confirm step, and the customer
profile page with contact details, the visit summary, reception notes and
the appointment history.

It also implements the Appointments & Calendar workspace at `/calendar`: a
dedicated, full-height scheduling grid (Day / 3 Days / Week / Month) with
staff columns, booking cards sized by duration, availability shading and
markers, drag-to-reschedule (time, staff, duration) validated by the
backend with revert-on-rejection, click-to-create, the booking modal and
the details panel with status quick actions. The Vite dev server proxies
`/api` to the backend's HTTPS URL so the browser sees a single origin.

And the Billing & Payments screens: the billing list (search by invoice or
customer, status and payment-method filters, pagination), the invoice
drawer (services, separate coupon/manual discount lines, payment record,
resume-checkout for drafts and receipt reprint for paid invoices), the
full-screen checkout workspace (services prefilled from the appointment,
coupon validation against the backend, fixed or percentage manual
discount, Cash / Card / Bank Transfer with an optional reference and the
printed receipt), and the coupon administration screens.

And the Receptionist Dashboard, the app's landing page: a greeting and
today's date, six appointment-workload cards (never revenue), the
clickable timeline of today's schedule opening the existing appointment
details, quick actions that land in the existing booking, customer and
billing workflows, the team's live statuses, customer alerts (birthdays,
first visits, latest notes) and the popular-services workload — every
number aggregated server-side from the read endpoint.

And the Reminders workspace at `/reminders` (owner and receptionist): the
operational reminder history every operations user monitors (status and type
filters, failure reasons, attempt counts) plus — for the owner alone — the
settings tab: WhatsApp connection status, the configurable reminder rules
(enable/disable, timing with a live preview) and an explicit-confirmation
test send. The appointment details panel shows each booking's WhatsApp
reminder rows, and the customer drawer records WhatsApp consent
(opt-in / opt-out, confirmed).

| Tool       | Version |
| ---------- | ------- |
| React      | 19.2.x  |
| TypeScript | 6.0.x   |
| Vite       | 8.3.x   |
| Tests      | vitest + Testing Library + jsdom |
| Linter     | oxlint  |

Dev server runs on `http://localhost:5173` by default.

---

## Database

**SQL Server**, database name `DeBlissDb`, schema owned entirely by EF Core Code
First migrations.

Local development uses Windows authentication, so no password is committed:

```
Server=(local);Database=DeBlissDb;Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true
```

The string lives in `appsettings.Development.json` only. The base
`appsettings.json` deliberately has none, so a deployed environment fails fast
unless `ConnectionStrings__DeBlissDb` is supplied through an environment
variable or user secrets.

Migrations are never applied automatically at startup — run them explicitly.
The EF Core tools are pointed at Infrastructure as *both* the project and the
startup project, which keeps design-time tooling out of the API's dependency
graph:

```bash
dotnet tool restore
dotnet ef migrations add <Name> \
  --project backend/DeBliss.Infrastructure \
  --startup-project backend/DeBliss.Infrastructure \
  --output-dir Persistence/Migrations
dotnet ef database update \
  --project backend/DeBliss.Infrastructure \
  --startup-project backend/DeBliss.Infrastructure
```

Modelling conventions worth knowing before touching the schema:

- Enumerations are stored as readable `nvarchar(20)` strings, not integers.
- Money columns are `decimal(18,2)`; `InvoiceItem.Quantity` is `decimal(18,3)`.
- Bookings snapshot the service name, duration and price at the moment of
  booking, so later edits to a `Service` cannot rewrite history.
- Every relationship is configured exactly once, from the dependent entity.
- Deleting a parent removes only the rows it truly owns. Links *into* booking
  and financial history use `Restrict`, so referenced records cannot be deleted
  out from under an appointment or invoice.

---

## Tests

`tests/DeBliss.UnitTests` is an xUnit project referencing all four backend
projects. It covers:

- the shape of the EF Core model (table and column naming, `Guid` primary keys,
  decimal precision on money, enum-to-string conversion, `DateTimeOffset`
  timestamps, unique business identifiers, cascade versus restrict rules, and
  the relationships the future booking workflow depends on) — model tests that
  never open a database connection;
- solution architecture rules (layering and reference direction);
- the ServiceCatalogue application services, run against SQLite;
- the Staffing application services (staff profiles, service assignment,
  working hours, time off and the availability check), run against SQLite;
- the Customers application services (paged search, profile CRUD, the
  duplicate-phone conflict, notes and the appointment-history queries), run
  against SQLite;
- the Scheduling application services (booking with snapshot lines, the
  customer/walk-in rule, computed end times, the status transition matrix
  with terminal immutability, schedule-patch semantics and the availability
  conflict), run against SQLite;
- the Billing application services (invoice start/resume, the checkout
  validation order with its exact error messages, coupon normalisation and
  validity windows, the discount math and the receipt), run against SQLite;
- the Reminders application services (lifecycle planning, dispatch with a fake
  provider — consent skips, retries, idempotent claiming, template variables),
  run against SQLite;
- API integration tests over an in-memory TestServer, covering the endpoints,
  the RFC 9457 error contract, and permission-surface drift.

The frontend has its own suite (vitest + Testing Library, `npm test` in
`frontend/debliss-web`) covering the API client, formatters, pagination, the
`useAsync` hook, the service form's error handling, the staff module
(list, profile form, service assignment, working-hours editor, time off and
the profile page), and the customers module (list, filters, pagination,
create/edit, the duplicate confirm flow, the profile page, notes and the
empty and failure states), and the calendar module (time and snap math,
the booking modal, the grid with drag-and-drop and the 409 revert path,
the details panel and the month view), and the billing module (the list
with its filters, the invoice drawer, the checkout screen with coupon,
manual discount, payment method, reference and the receipt, and the
coupon administration screens), and the reminders module (the history list
and filters, the owner settings with rule timing, the test send, the
customer consent card and the appointment panel section).

---

## Prerequisites

- .NET SDK **10.0**
- Node.js **24.x** and npm **11.x**
- SQL Server **2022** (or later) reachable as `(local)`, or set
  `ConnectionStrings__DeBlissDb` to point somewhere else

---

## Common commands

Run all of these from the repository root.

### Backend

```bash
dotnet restore DeBliss.sln        # restore NuGet packages
dotnet build DeBliss.sln          # build every project
dotnet test DeBliss.sln           # run the xUnit suite
dotnet run --project backend/DeBliss.Api   # start the API (dev)
```

The `dotnet ef` commands are listed in the [Database](#database) section.

### Frontend

```bash
cd frontend/debliss-web
npm install                       # first time only
npm run dev                       # Vite dev server with HMR
npm run build                     # type-check (tsc -b) then production build
npm run test                      # vitest + Testing Library suite
npm run lint                      # oxlint
npm run preview                   # serve the production build locally
```

---

## Authentication and roles

Sign-in is JWT-based: `POST /api/auth/login` returns the safe user projection and
sets an HttpOnly, SameSite=Strict, Secure `debliss_auth` cookie holding a 60-minute
signed token. Non-browser clients can send the same token as `Authorization: Bearer`.
`GET /api/auth/me` returns the current user; `POST /api/auth/logout` revokes the
presented token and drops the cookie. Passwords are hashed with ASP.NET Core
Identity's `PasswordHasher` (PBKDF2-SHA256); the plaintext is never stored or logged.

Three stable role names gate the API via authorization policies:

- `OWNER` — full access, including the future owner workspace (`OwnerOnly` policy)
- `RECEPTIONIST` — day-to-day operations (`Operational` policy: dashboard, calendar,
  customers, services, staff, billing)
- `STAFF` — self-service only (`StaffSelf` policy): the linked staff member's
  own profile, appointments, schedule and attendance via `/api/staff/me/*`.
  No admin web pages; the surface is the foundation for the future staff
  mobile app (see
  [docs/STAFF-MOBILE-BACKEND-SPECIFICATION.md](docs/STAFF-MOBILE-BACKEND-SPECIFICATION.md))

Policies are the extension point for finer permissions (reports, campaigns, revenue).

### Development sign-ins

When the API runs in the `Development` environment with an empty `Users` table,
`DevAuthSeeder` creates one account per role (hashed passwords, `*.dev` addresses —
never real credentials):

| Role         | Email                    | Password             |
|--------------|--------------------------|----------------------|
| OWNER        | `owner@debliss.dev`      | `DeBliss#Owner2026!` |
| RECEPTIONIST | `receptionist@debliss.dev` | `DeBliss#Front2026!` |
| STAFF        | `staff@debliss.dev`      | `DeBliss#Team2026!`  |

The seeder never runs outside `Development` and never touches a table that already
has users. The development JWT signing key in `appsettings.Development.json` is
Development-only configuration; every other environment must supply
`Authentication:Jwt:SigningKey` via environment variables or a secret store, and the
API refuses to start without it.

---

## What is intentionally NOT here yet

To keep the foundation clean, the following are deliberately out of scope and
should be introduced only when the corresponding work begins:

- The Owner Dashboard, Staff workspace and user/role administration UIs — the
  authorization foundation (`OwnerOnly`/`Operational` policies, owner workspace
  probe endpoint) protects them, but the modules themselves are future phases
- Business modules other than Services & Pricing, Staff Management &
  Availability, Customers / CRM, Appointments & Calendar, Billing &
  Payments and Appointment Reminders: packages, loyalty, inventory, expenses
  and reports (plus the scheduling leftover — waitlist — and the Billing
  Phase 2 items: split payments, gateways, refunds and tax)
- SMS, marketing/promotional WhatsApp and any bulk messaging — the reminder
  engine intentionally delivers only the four transactional appointment
  notices
- The public customer-facing website (a separate future phase)
- Microservices — the system stays a modular monolith

---

## Configuration and secrets

`appsettings.json` is committed and should hold only non-sensitive defaults.
Secrets (credentials, API keys) must never be committed:

- Use `dotnet user-secrets` for local development secrets.
- Use environment variables or a secret store for deployed environments.
- `.env`, `*.key`, `*.pfx`, `secrets.json` and `appsettings.*.Local.json` are
  all ignored by `.gitignore`.

If a setting must be documented, commit an example file (e.g. `.env.example`)
with placeholder values only.

The development connection string in `appsettings.Development.json` is safe to
commit because it uses `Integrated Security=true` — it authenticates as the
Windows user running the API and contains no password. A SQL-authenticated
string must never be committed; supply it through user secrets or an
environment variable instead.

The `Twilio` section in `appsettings.json` follows the same rule: it is
committed with `Enabled: false` and empty placeholders only. WhatsApp
credentials (`Twilio__AccountSid`, `Twilio__AuthToken`, `Twilio__WhatsAppFrom`,
template SIDs) must arrive via environment variables, a secret store, or user
secrets — never a committed file. See
[docs/APPOINTMENT-REMINDERS-SPECIFICATION.md](docs/APPOINTMENT-REMINDERS-SPECIFICATION.md)
for the full variable list and the Sandbox/production setup procedures.
