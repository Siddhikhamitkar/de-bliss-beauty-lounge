# Deployment Readiness Audit — De Bliss System

**Date:** 2026-10-05
**Scope:** Full-repository audit ahead of hosting the app at a real URL for an external tester.
**Constraints honored:** No deployment performed. No Azure resources created. No production data touched. No architecture changes made. **No secret values are reproduced in this document — files and configuration locations only.**

**Verdict: READY WITH CHANGES** — the codebase builds, tests and runs cleanly, but five deployment blockers (section 9) must be resolved before a tester can use the app.

---

## 1. Current Architecture

- **Repository:** monorepo — `backend/` (.NET 10 solution), `frontend/debliss-web/` (React 19 + TypeScript + Vite), `docs/` specifications, `README.md`.
- **Backend layering:** Domain → Application → Infrastructure → Api (clean/hexagonal layout; controllers are thin, business rules live in Application services). Modular monolith, one deployable API.
- **Persistence:** EF Core 10 / SQL Server via `DeBlissDbContext`; four migrations in Infrastructure.
- **Auth/RBAC:** JWT (HS256, 60-min lifetime, claims `sub`/`email`/`name`/`role`/`staff_id`/`jti`). Transport is either `Authorization: Bearer` or an HttpOnly cookie `debliss_auth` (`Secure`, `SameSite=Strict`, `HttpOnly`, `Path=/`). Roles: OWNER, RECEPTIONIST, STAFF. Policies: `OwnerOnly` and `Operational`. Logout revokes the token's `jti` (in-memory list).
- **Completed modules:** Services & Pricing, Staff Management & Availability, Customers CRM, Calendar/Appointments, Billing & Payments + Coupons, Receptionist Dashboard, Auth/RBAC, Service Bundles.
- **Frontend:** React 19 SPA with a small history-API router and role-guarded routes. API client calls relative `/api` with `credentials: 'same-origin'`. No environment configuration, no hardcoded backend URL. A Vite dev proxy (`/api` → `https://localhost:7019`) exists for local development only.
- **Key hosting implication:** there is **no CORS middleware** (by design) and the auth cookie is `SameSite=Strict`. The frontend and API must therefore be presented to the browser under **one origin**. The recommended deployment (section 11) satisfies this with a Vercel rewrite that proxies `/api/*` to Azure App Service — **zero code changes required**.
- **Recommended target (not implemented):** Frontend on Vercel, Backend on Azure App Service, Database on Azure SQL, Source on GitHub.

## 2. Build Status

Verified on 2026-10-03/05 with .NET SDK 10 (EF tool 10.0.12 via `dotnet-tools.json`) and Node v24.18.0.

| Gate | Command | Result |
|---|---|---|
| Backend restore | `dotnet restore` (via build) | OK |
| Backend build | `dotnet build DeBliss.sln` | **Build succeeded — 0 Warning(s), 0 Error(s)** |
| Backend release publish | `dotnet publish -c Release` | OK |
| Frontend install | `npm install` | **0 vulnerabilities** |
| Frontend build | `npm run build` (`tsc -b && vite build`) | **exit 0** — TypeScript 6 clean; Vite 8.3.2, 92 modules |
| Frontend lint | `npm run lint` (oxlint) | **exit 0** — 1 warning (see LOW-L1) |

Frontend build artifacts: `index.html` 0.47 kB, CSS 45.39 kB, JS 399.17 kB (gzip 104.86 kB). Standard static SPA output — suitable for any static host.

## 3. Test Status

| Suite | Result |
|---|---|
| Backend — `dotnet test` (xUnit; unit + API integration on in-memory TestServer/SQLite) | **642 / 642 passed** |
| Frontend — `npx vitest run` (idle machine) | **290 / 290 passed across 29 files** (44 s) |

**Frontend flakiness note:** two earlier runs executed while the machine was saturated by parallel .NET builds showed 4–7 timeout-style failures (`Test timed out in 5000ms` in different `userEvent`-heavy tests each time). A dedicated idle re-run is fully green, confirming the failures were load-induced runner flakiness, not product defects. No production impact; CI runners should be sized accordingly (options: higher `testTimeout`, or vitest `pool: 'vmThreads'` per its own performance hint).

## 4. Configuration Findings

| Item | Location | State | Deployment implication |
|---|---|---|---|
| Production settings | `backend/DeBliss.Api/appsettings.json` | Deliberately contains only `Logging` + `AllowedHosts: "*"` — **no secrets** | Production must supply connection string + JWT key via environment variables (fail-fast design). |
| Development settings (tracked in git) | `backend/DeBliss.Api/appsettings.Development.json` | Contains `ConnectionStrings:DeBlissDb` (local dev DB, Windows-integrated auth — no password) and `Authentication:Jwt:SigningKey` (development-only key). Values **not** reproduced here. | Production must generate a **fresh** signing key. Keep the GitHub repo **private**. See HIGH-H1. |
| Launch profile | `backend/DeBliss.Api/Properties/launchSettings.json` | Dev-only ports `https://localhost:7019` / `http://localhost:5132`, env `Development` | Ignored in deployment (App Service provides its own). |
| Frontend API base | `frontend/debliss-web/src/api/client.ts` | Relative `/api`, `credentials: 'same-origin'`, no env vars anywhere | Hosting must proxy `/api` to the backend (see sections 7 and 13). |
| Vite dev proxy | `frontend/debliss-web/vite.config.ts` | `/api` → `https://localhost:7019` (dev only) | None; production rewrite is configured on the host, not here. |
| Hardcoded URLs | `frontend/debliss-web/src/` | None (grep-verified: no localhost/ports/env access) | None. |
| SPA routing | `frontend/debliss-web/src/app/router.ts` | History API (`pushState`) | Static host needs an SPA fallback rewrite (all paths → `index.html`). |
| `.gitignore` | repo root | Covers `node_modules/`, `dist/`, `.env*`, .NET outputs | `appsettings.Development.json` is intentionally tracked (documented in README). |
| README | `README.md` | Intro and endpoint list are stale: still says auth "not implemented yet" / "endpoints are deliberately open"; module list incomplete | Documentation drift only — see MEDIUM-M1. |
| Dev seed users | `backend/DeBliss.Api/DevData/DevAuthSeeder.cs`, `DevDataSeeder.cs` | Development-only, double-guarded (env check + empty-table checks); contain dev passwords (values not reproduced) | Never run in Production. Production starts with **zero users** — see BLOCKER-B1. |

## 5. Security Findings

**Authorization surface — verified live against the local dev API (2026-10-03):**

| Request | Observed |
|---|---|
| Anonymous `GET /api/services` | 401 |
| Anonymous `GET /api/auth/me` | 401 |
| Anonymous `GET /health` | 200 (by design) |
| ANONYMOUS `GET /openapi/v1.json` | 200 in Development only; not mapped in Production |
| Receptionist login → `/api/auth/me`, `/api/dashboard/receptionist`, `/api/service-bundles/bookable`, appointment detail | 200 |
| Receptionist → `GET /api/owner/workspace` | 403 |
| Staff login → `/api/auth/me` | 200 |
| Staff → `GET /api/dashboard/receptionist` | 403 |

- Every business controller carries `[Authorize(Policy = AuthPolicies.Operational)]`; the owner controller is `OwnerOnly`; only `/health`, `/api/auth/login` and `/api/auth/logout` are anonymous (grep-verified across all controllers).
- Login uses ASP.NET Core `PasswordHasher` (PBKDF2) verification and returns a generic failure message (no user enumeration). **No rate limiting exists anywhere** — login is brute-forceable if exposed (see HIGH-H3).
- Token design: HS256, 60-min lifetime, no refresh token, token not exposed in the login response body (cookie-only). Logout revokes `jti` — but the revocation list is **in-memory, per-process** (see HIGH-H2).
- Missing signing key throws fail-fast (`JwtTokenIssuer` names `Authentication:Jwt:SigningKey`) until configured.
- No CORS middleware exists. This is correct for a same-origin proxy deployment. Do **not** naively add `AllowAnyOrigin` with credentials later (invalid and unsafe); see MEDIUM-M4.
- Dev-only surfaces (OpenAPI, seeders) are strictly `IsDevelopment()`-gated. `public partial class Program` is a test hook, harmless in Production.
- Secrets in source control: only development values (`appsettings.Development.json`, `DevAuthSeeder.cs`) — locations reported, values withheld. Mitigation: private repo + fresh production key (BLOCKER-B2 / HIGH-H1).
- Error handling leaks nothing: validation → 400 `ValidationProblemDetails`, 404, 409, framework 401/403, unexpected → generic 500 (`ApiExceptionHandler`).

## 6. Database Readiness

- **Engine:** SQL Server via EF Core 10. Migrations (4): `20261003140708_InitialCreate`, `20261003203643_AddCustomerIsActive`, `20261004111115_AddCouponsAndInvoiceDiscounts`, `20261004175803_AddServiceBundles`.
- **Clean-environment creation proven:** `dotnet ef database update` against a fresh database applied all four migrations in order, and `dotnet ef migrations list` confirmed them. The audit used a scratch database (`DeBlissDb-AuditCheck`) on the local dev SQL Server — empty schema only; the drop was intentionally not executed because destructive DB operations require explicit approval. It is local-only and unrelated to deployment. **No production data was touched — none exists yet.**
- **No auto-migrate:** no `Migrate()`/`EnsureCreated()` in the startup path (grep-verified). Applying migrations to Azure SQL is a mandatory, explicit deployment step (BLOCKER-B4).
- **Seed data:** Development-only and idempotent (empty-table checks). A production database starts empty of business data and users.
- **First user in Production is not creatable today (BLOCKER-B1):** `IAuthService` exposes only `AuthenticateAsync`/`GetUserAsync`; there is no register or admin endpoint, and the seeders are Development-gated. Without a one-time bootstrap step, nobody can log in (resolution options in section 10).
- FKs, indexes and nullability are defined via EF configurations and covered by the green backend suite (including `DeBlissDbContextModelTests`).

## 7. Frontend Readiness

- Production build is clean and static (section 2). No API base URL configuration is needed **if** the host proxies `/api` (recommended); a naive cross-site deployment would break auth (BLOCKER-B3).
- **Required on the static host:**
  1. SPA fallback: all client routes → `/index.html` (history-API router).
  2. API proxy: `/api/*` → `https://<app-name>.azurewebsites.net/api/*`.
- Recommended Vercel configuration (sketch — to be added at deploy time, not implemented in this audit):

```json
{
  "rewrites": [
    { "source": "/api/:path*", "destination": "https://<app-name>.azurewebsites.net/api/:path*" },
    { "source": "/(.*)", "destination": "/index.html" }
  ]
}
```

  With this proxy the browser sees a single origin: the `Secure` + `SameSite=Strict` cookie works unchanged and CORS is never exercised — **zero frontend and backend code changes**.
- Alternative (custom domains under one registrable domain, e.g. `app.example.com` + `api.example.com`) would additionally require CORS configuration and `credentials: 'include'` code changes — not recommended for v1.
- Assets/metadata ready: title "De Bliss Beauty Lounge", `favicon.svg` present. Modern evergreen browsers only (React 19) — fine for a tester.

## 8. Backend Readiness

- Targets `net10.0`; release publish verified. App Service (Linux, .NET 10) is the recommended host.
- **Health:** `/health` is anonymous and includes a database check — use it for App Service Health Check and for smoke tests.
- **Configuration surface:** environment variables with `__` nesting (`ConnectionStrings__DeBlissDb`, `Authentication__Jwt__SigningKey`) — see section 12.
- **Behind TLS termination:** set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so `UseHttpsRedirection` and secure-cookie decisions see the original scheme.
- **Errors:** uniform ProblemDetails responses; unexpected exceptions return a generic 500 without internals.
- **Logging:** default console logging is adequate for v1; Application Insights optional.
- **OpenAPI:** intentionally absent in Production (acceptable; note for tester onboarding).
- **Hosting topology:** run **one instance** — the logout revocation list and (absence of) distributed state assume a single process (HIGH-H2).

## 9. Deployment Blockers

Categories: **BLOCKER = deployment should not proceed until resolved.** HIGH/MEDIUM/LOW are non-blocking risks.

### BLOCKER

| # | Finding | Resolution (details in section 10/13) |
|---|---|---|
| B1 | **No first-user path in Production.** Fresh DB has zero users; seeders are Development-only; no register/admin API exists. The tester cannot log in. | Choose and execute a one-time bootstrap before hand-off (options in section 10). |
| B2 | **Production secrets not configured (and none exist yet):** `Authentication__Jwt__SigningKey` (API fails on first authenticated request without it) and `ConnectionStrings__DeBlissDb`. The tracked development key must **not** be reused. | Generate a fresh ≥256-bit random key; set both as App Service settings. |
| B3 | **Auth requires one origin:** `SameSite=Strict` + `Secure` cookie, `credentials: 'same-origin'`, and no CORS mean a naive cross-site Vercel→Azure deployment silently breaks login. | Deploy behind the Vercel `/api` rewrite proxy (section 7) — zero code change. |
| B4 | **Database does not exist and migrations never auto-apply.** Azure SQL provisioning + `dotnet ef database update` (or a migration bundle) is a mandatory manual step; first deploy order matters. | Follow deployment sequence (section 13), steps 3–6. |
| B5 | **Source never pushed to GitHub.** `origin` remote is configured but has zero branches — neither Vercel nor an App-Service GitHub workflow can deploy, and the only copy of the code is the local machine. | Push the repository (make it **private**) before anything else. |

### HIGH

| # | Finding | Resolution |
|---|---|---|
| H1 | Development JWT key + local connection string are tracked in `appsettings.Development.json` (values not reproduced). If the repo ever becomes public, those are readable. | Keep repo private; never reuse the dev key in Production; optionally migrate dev settings to user-secrets later (non-blocking cleanup). |
| H2 | Logout revocation list is in-memory per process — a second App Service instance would silently break revocation semantics (revoked tokens live until expiry, ≤60 min). | Run a single instance for the tester deployment; move to distributed state before scale-out. |
| H3 | No login rate limiting / lockout anywhere. | Mitigate at the platform (App Service access restrictions / WAF) for v1; consider an application-level limiter later. |
| H4 | No production user management: creating receptionist/staff accounts after go-live also requires the bootstrap mechanism (or SQL) until an admin UI exists. | Acceptable for tester phase; document the operational procedure chosen for B1. |

### MEDIUM

| # | Finding | Notes |
|---|---|---|
| M1 | README drift: says auth is "not implemented yet" and "endpoints are deliberately open"; module list incomplete. | Misleads the external tester and future readers; update docs (non-blocking). |
| M2 | Frontend tests are load-sensitive (5 s `userEvent` timeouts under CPU saturation; idle run green). | Size CI runner; optional `testTimeout`/pool tweaks. No product impact. |
| M3 | React StrictMode dev double-invoke causes a double checkout POST in dev (pre-existing, documented earlier, benign). | Dev-only artifact of `main.tsx` StrictMode; no production impact. |
| M4 | No CORS by design. | With the rewrite proxy this stays correct. If custom domains are adopted later, add strict CORS + `credentials: 'include'` — never `AllowAnyOrigin` with credentials. |
| M5 | OpenAPI/Swagger unavailable in Production. | Tester won't have interactive API docs; provide the README/endpoint list instead. |

### LOW

| # | Finding | Notes |
|---|---|---|
| L1 | oxlint: 1 warning `react(only-export-components)` at `frontend/debliss-web/src/auth/AuthContext.tsx:72` (fast-refresh DX). | Exit code 0; cosmetic. |
| L2 | JS bundle 399 kB raw / 105 kB gzip. | Fine for v1; optional code-splitting later. |
| L3 | Audit scratch DB `DeBlissDb-AuditCheck` remains on the **local** dev SQL Server (empty migrated schema). | Cleanup (drop) blocked pending explicit approval; unrelated to deployment. |
| L4 | `AllowedHosts: "*"` in `appsettings.json`. | Optionally restrict to the Azure host + custom domain. |
| L5 | Vitest recreates jsdom per file (~44 s wall time). | Test-infra only; vitest suggests `pool: 'vmThreads'`. |

## 10. Required Changes

Ordered checklist (nothing below has been implemented — this is the change list for the deployment task):

1. **Push source to GitHub** (private). `origin` is already configured; zero branches exist remotely (B5).
2. **Generate a fresh production JWT signing key** (≥256-bit random; e.g. `openssl rand -base64 48`). Do not reuse any tracked development value (B2/H1).
3. **Provision Azure SQL** (section 11) with a SQL-auth application user; capture the connection string securely (B4).
4. **Create the App Service** and set environment variables (section 12).
5. **Deploy the API** (`dotnet publish` zip or GitHub Actions) — order relative to migrations is flexible because the app never auto-migrates; migrations must be applied before first user traffic.
6. **Apply migrations to Azure SQL** — from the dev machine with `ConnectionStrings__DeBlissDb` pointing at Azure SQL: `dotnet ef database update` (built artifact), or generate a self-contained bundle: `dotnet ef migrations bundle` and run it against the target (B4).
7. **Bootstrap the first user (B1)** — pick one before go-live:
   - **(a) One-time seeding run (fastest for a tester demo):** start the API once with `ASPNETCORE_ENVIRONMENT=Development` against the new database. Both seeders are empty-table-guarded, so they create the dev accounts **and** demo business data (useful for a tester), then never run again. Immediately revert to `Production` afterwards. Trade-off: demo data lands in the tester DB and Development was briefly live.
   - **(b) Guarded admin-bootstrap step (cleanest long-term, small code change):** add an opt-in, one-shot owner-provisioning path (e.g. env-var-gated at startup) and deploy it. Requires a code change — deliberately not done in this audit.
   - **(c) Manual SQL insert:** generate a password hash locally with the existing `AuthService.HashPassword` helper and insert one OWNER row. Fully manual; error-prone.
   - Recommended: (a) for the immediate tester hand-off, (b) before any real usage.
8. **Create the Vercel project** (root directory `frontend/debliss-web`) and add the `vercel.json` rewrites from section 7.
9. **Smoke-test** per the checklist in section 13 before handing the URL to the tester.
10. **Documentation hygiene (M1/M5):** update the README's stale auth/endpoint text; provide the tester a short credential + URL note (delivered via a secure channel, not chat/email).

## 11. Recommended Azure Resources

| Resource | Recommendation | Notes |
|---|---|---|
| Azure SQL Server + single database | Lowest tier sufficient for a single tester: Basic DTU **or** serverless `GP_S_Gen5_1` with auto-pause | If auto-pause is enabled, expect cold-start latency (health check hits the DB) — disable or accept it. Region closest to the tester (e.g. UAE North). Allow Azure services + the dev machine's IP through the firewall. |
| App Service (API) | **Linux, .NET 10 runtime, B1+**, single instance | Set `ASPNETCORE_ENVIRONMENT=Production` explicitly; enable Always On (paid tiers); HTTPS-only ON; TLS terminated by the platform. |
| App Service settings | See section 12 | Stored encrypted at rest, access-controlled. |
| Vercel | Hobby/Pro project for `frontend/debliss-web` | Free tier fine for a tester; custom domain optional (not required for hand-off). |
| GitHub | **Private** repository | Contains development credentials in tracked files (H1). |
| Optional | Application Insights; Azure Key Vault | Not required for v1; App Service settings are sufficient. Skip Key Vault to keep the deploy surface small. |
| Not needed for v1 | Front Door/WAF, autoscaling, deployment slots | Add only when moving beyond tester usage. |

## 12. Recommended Environment Variables

**Backend (App Service → Configuration → Application settings)** — values supplied at deploy time, never committed:

| Name | Value guidance | Required |
|---|---|---|
| `ConnectionStrings__DeBlissDb` | Azure SQL connection string, SQL auth app user, e.g. `Server=tcp:<server>.database.windows.net,1433;Initial Catalog=DeBlissDb;User ID=<app-user>;Password=<secret>;Encrypt=True;TrustServerCertificate=False;` | **Yes** |
| `Authentication__Jwt__SigningKey` | Fresh random secret, ≥32 bytes (base64); unique to production | **Yes** |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Yes (explicit; App Service defaults to Production but set it to be unambiguous) |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | Recommended |
| `WEBSITE_RUN_FROM_PACKAGE` | `1` (if using zip deploy) | Optional |

**Frontend (Vercel):** none. The API base is the same-origin `/api` path handled by the rewrite; no `VITE_*` variables exist or are needed.

## 13. Deployment Sequence

1. Push the repository to GitHub (private). Confirm CI/build hooks if used.
2. Provision Azure SQL server + database; create the app user; note the connection string.
3. Create the App Service (Linux, .NET 10, single instance) and set the section 12 settings — including the **fresh** JWT key; keep `ConnectionStrings__DeBlissDb` pointing at the new database.
4. Build and publish the API (`dotnet publish -c Release`); deploy via GitHub Actions or zip (`WEBSITE_RUN_FROM_PACKAGE=1`). Verify the process starts.
5. Apply migrations to Azure SQL (step 6 of section 10 — `dotnet ef database update` with the env var, or run a migration bundle). Verify with `dotnet ef migrations list` against the target.
6. Bootstrap the first user(s) using the approved option from section 10 step 7. If option (a) was chosen, revert `ASPNETCORE_ENVIRONMENT` to `Production` immediately and restart.
7. Create the Vercel project from the same repo (root `frontend/debliss-web`), add the `vercel.json` rewrites from section 7 with the real App Service hostname; deploy.
8. Smoke test (minimum):
   - `GET https://<app>.azurewebsites.net/health` → 200 anonymous.
   - `GET https://<vercel-domain>/` loads the login page; no console CORS/cookie errors.
   - Anonymous API call → 401; login as the bootstrapped OWNER → cookie set (`debliss_auth`), dashboard loads.
   - Role matrix spot-checks: STAFF → receptionist dashboard 403; RECEPTIONIST → owner workspace 403.
   - Menu shows services **and** bundles; `GET /api/service-bundles/bookable` → 200 through the proxy.
   - Book an appointment from a bundle; complete a checkout; logout → subsequent call 401.
9. Hand the URL to the tester; deliver credentials over a secure channel; note that OpenAPI docs are not available in Production (M5).
10. Keep the GitHub repo private and the JWT key out of any file.

## 14. Rollback Considerations

| Layer | Rollback mechanism | Notes |
|---|---|---|
| Frontend (Vercel) | Instant rollback to the previous deployment | No data implications; redeploy after fixing. |
| Backend (App Service) | Redeploy the previous publish artifact (keep zips) or swap back a slot if slots are used; environment-variable changes revert instantly | No schema coupling for this initial deploy. |
| Database | **Before every migration: take a backup/bacpac** (Azure SQL also keeps automated backups/PITR). Migrations in this repo are additive; a previous API build remains compatible with a newer schema in most cases. | A true schema revert = restore from backup → any tester data written since the backup is lost. Avoid unless necessary. |
| Secrets | Rotating `Authentication__Jwt__SigningKey` invalidates all sessions (users re-login; cookie ≤60 min) | Acceptable emergency lever. |
| Session state | In-memory revocation list is lost on restart — revoked tokens become usable again until they expire (≤60 min) | Known limitation, single-instance v1. |
| Custom domain/DNS (if used) | Re-point records; mind TTL | Not used for the initial tester URL. |

---

**Audit notes:** all live checks ran against the local Development API only; no production environment or data was touched. The only artifact created during the audit was the local scratch database `DeBlissDb-AuditCheck` (empty schema); its removal is pending explicit approval. No secret values appear in this document — only file paths and configuration keys.
