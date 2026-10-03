# De Bliss Beauty Lounge

Salon management and customer experience platform.

This repository currently contains **the project foundation only**. No business
modules (customers, appointments, services, staff, billing), no database, no EF
Core migrations and no authentication have been implemented yet.

The system is built as a **modular monolith**: a single deployable backend
composed of independent, cleanly separated modules, with a layered architecture
inside it. It is deliberately *not* a microservices system.

---

## Repository layout

```
DeBlissSystem/
├── DeBliss.sln                     # Solution containing all .NET projects
├── README.md
├── .gitignore
├── backend/
│   ├── DeBliss.Api/                # ASP.NET Core Web API — composition root / HTTP entry point
│   ├── DeBliss.Application/        # Use cases, ports, application services
│   ├── DeBliss.Domain/             # Core model — depends on nothing
│   └── DeBliss.Infrastructure/     # EF Core, SQL Server, external integrations
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

Currently intentionally empty.

### DeBliss.Application
Use-case / orchestration layer. This is where a business operation ("book an
appointment") is defined as a command or query plus its handler, together with
the *ports* (interfaces) it needs — e.g. a repository contract — without knowing
how those ports are implemented.

References **Domain** only, so application logic stays free of infrastructure
concerns.

Currently intentionally empty.

### DeBliss.Infrastructure
Adapter layer. Implements the ports declared by Application: EF Core
`DbContext`, SQL Server repositories, and any external service clients.

References **Application** (to implement its interfaces) and **Domain** (to map
and persist its types).

This is the only project that carries the Entity Framework Core and SQL Server
packages. The packages are present as groundwork; **no `DbContext`, no
migrations and no database exist yet.**

### DeBliss.Api
The ASP.NET Core Web API and the application's composition root. It exposes HTTP
endpoints, wires up dependency injection, and hosts the application.

References **Application** (to dispatch use cases) and **Infrastructure** (to
register concrete implementations). This is the only place where the outermost
and innermost layers meet, and the dependency direction still points inward.

Currently exposes only:
- `GET /health` — health check endpoint (ASP.NET Core health checks)
- `/openapi/v1.json` — OpenAPI document, development environment only

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

The Vite starter demo (counter, logos, marketing links) has been removed; the
app renders a minimal placeholder shell. No API client, routing or state
management has been added yet.

| Tool       | Version |
| ---------- | ------- |
| React      | 19.2.x  |
| TypeScript | 6.0.x   |
| Vite       | 8.3.x   |
| Linter     | oxlint  |

Dev server runs on `http://localhost:5173` by default.

---

## Tests

`tests/DeBliss.UnitTests` is an xUnit project referencing all four backend
projects. It currently contains a single structure test verifying that every
backend assembly is produced and loadable by the test host — a smoke check that
the solution is wired correctly. Business tests will be added alongside the
modules they cover.

---

## Prerequisites

- .NET SDK **10.0**
- Node.js **24.x** and npm **11.x**
- SQL Server — *not required yet; no database is created by this foundation*

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

### Frontend

```bash
cd frontend/debliss-web
npm install                       # first time only
npm run dev                       # Vite dev server with HMR
npm run build                     # type-check (tsc -b) then production build
npm run lint                      # oxlint
npm run preview                   # serve the production build locally
```

---

## What is intentionally NOT here yet

To keep the foundation clean, the following are deliberately out of scope and
should be introduced only when the corresponding work begins:

- Business entities and modules — Customer, Appointment, Service, Staff, Billing
- SQL Server database and connection strings
- EF Core `DbContext` and migrations
- Authentication and authorisation
- Microservices — the system stays a modular monolith

---

## Configuration and secrets

`appsettings.json` is committed and should hold only non-sensitive defaults.
Secrets (connection strings, API keys) must never be committed:

- Use `dotnet user-secrets` for local development secrets.
- Use environment variables or a secret store for deployed environments.
- `.env`, `*.key`, `*.pfx`, `secrets.json` and `appsettings.*.Local.json` are
  all ignored by `.gitignore`.

If a setting must be documented, commit an example file (e.g. `.env.example`)
with placeholder values only.
