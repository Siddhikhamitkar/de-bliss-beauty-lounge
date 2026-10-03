# De Bliss Beauty Lounge

Salon management and customer experience platform.

This repository currently contains **the project foundation, the core database
layer and the first two business modules — Services & Pricing** (service
categories, services, prices and eligible-staff assignment) **and Staff
Management & Availability** (staff profiles, service capabilities, working
schedules, breaks/time-off and the availability check), each with its HTTP
API, React admin UI and tests. Authentication, the remaining business modules
(appointments, customers, billing) and the customer-facing website are
deliberately not implemented yet.

The authoritative description of the Services & Pricing module is
[docs/SERVICES-MODULE-SPECIFICATION.md](docs/SERVICES-MODULE-SPECIFICATION.md);
the Staff Management & Availability module is described in
[docs/STAFF-MANAGEMENT-SPECIFICATION.md](docs/STAFF-MANAGEMENT-SPECIFICATION.md).

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

Holds the eighteen core entities behind the salon's booking and billing model,
grouped into folders that match the bounded contexts they belong to:

| Folder        | Entities                                                        |
| ------------- | --------------------------------------------------------------- |
| `Common`      | `Entity`, `AuditableEntity`, `ISoftDeletable` (base types)      |
| `Identity`    | `User`, `Role`, `UserRole`                                      |
| `Customers`   | `Customer`, `CustomerNote`                                      |
| `Staffing`    | `Staff`, `StaffService`, `StaffWorkingHour`, `StaffTimeOff`     |
| `Services`    | `ServiceCategory`, `Service`                                    |
| `Scheduling`  | `Appointment`, `AppointmentService`, `AppointmentStatusHistory`, `BlockedTime` |
| `Billing`     | `Invoice`, `InvoiceItem`, `Payment`                             |

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
`ServiceCatalogue/Ports`), this layer hosts the first module:
`ServiceCatalogue` — the use cases for managing the service menu
(`ServiceCatalogueService`, `ServiceCategoryService`), their DTOs, and the
validation rules and limits the module enforces.

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
off. The Vite dev server proxies `/api` to the backend's HTTPS URL so the
browser sees a single origin.

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
- API integration tests over an in-memory TestServer, covering the endpoints,
  the RFC 9457 error contract, and permission-surface drift.

The frontend has its own suite (vitest + Testing Library, `npm test` in
`frontend/debliss-web`) covering the API client, formatters, pagination, the
`useAsync` hook, the service form's error handling, and the staff module
(list, profile form, service assignment, working-hours editor, time off and
the profile page).

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

## What is intentionally NOT here yet

To keep the foundation clean, the following are deliberately out of scope and
should be introduced only when the corresponding work begins:

- Authentication and authorisation (`User.PasswordHash` exists but is unused);
  every endpoint carries a `<remarks>` naming the permission it will require
- Business modules other than Services & Pricing and Staff Management &
  Availability: appointments, customers and billing
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
