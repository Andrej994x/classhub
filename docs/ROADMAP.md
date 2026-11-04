# ClassHub — Master Architecture & Implementation Plan

**Version:** 0.2 (revised for .NET 10 + React + SQL Server; approved for the Master Implementation Task)
**Date:** 2026-11-04
**Status:** Plan only. No repository, packages, SQL Server changes, or application code exist yet.
**Inputs:** *ClassHub — Master Product Scope & Business Requirements v1.0* (M01–M17), the *Master Architecture & Full Implementation Plan* brief, and the *Revise Master Implementation Plan for .NET + React + SQL Server* brief.
**Environment facts:** collected on this machine on 2026-11-04 with read-only commands only: tool versions, NuGet/npm registry lookups, `SELECT` queries, and connection tests.

## Changes from v0.1

| Area | v0.1 | v0.2 |
|---|---|---|
| Backend | NestJS on Node.js (TypeScript) | **ASP.NET Core Web API on .NET 10 LTS (C#)**, modular monolith, Minimal APIs |
| Frontend | Next.js App Router | **React + TypeScript + Vite + React Router** SPA, plus **Razor Pages** for public teacher sites (SEO) with React islands |
| Data access | TypeORM + `mssql`/`tedious` | **EF Core 10 + Microsoft.EntityFrameworkCore.SqlServer**, EF Core migrations |
| Authentication | Custom Node session module (Argon2id) | **ASP.NET Core Identity**: cookie sessions, built-in hashing, lockout, tokens, TOTP |
| Authorization | Custom guards | **Policy-based authorization**: fallback policy, requirements and handlers |
| DB connection | SQL logins + Mixed Mode + TCP 1433 | **Windows Authentication over Shared Memory. No server changes.** Verified (B.4) |
| Monorepo tooling | pnpm workspaces, shared `contracts` package | .NET solution (`ClassHub.slnx`) + one npm package (`web/`). Contracts flow through **OpenAPI → generated TypeScript types** |
| Tests | Vitest everywhere | **xUnit v3** (backend unit, integration, architecture), **Vitest** (frontend), **Playwright** (E2E) |
| Integration DB | Local test DB or CI container | **Testcontainers SQL Server 2022**, locally and in CI (Docker required) |
| Workflow | Plan approval for each phase, manual merge | **Autonomous**: Implement → Tests → Codex Review → Fixes → GitHub Actions → **automatic squash merge** → next phase |
| Docs | — | Added `docs/PROGRESS.md` and `docs/RECOVERY.md` |

The business scope, release structure and phase IDs from v0.1 are unchanged.

---

## Contents

- A. Executive Summary
- B. Technology and Compatibility Assessment
- C. Architecture Proposal
- D. Complete Release Roadmap
- E. Detailed Release 0 Plan
- F. Release 1–3 Plans
- G. MVP Beta, V1 and V2
- H. Testing and CI Strategy
- I. GitHub and Codex Workflow (autonomous)
- J. Decisions, Risks and Blockers
- K. Recommended First Phase
- Appendix 1 — Local SQL Server: no changes required
- Appendix 2 — Documentation plan (including progress and recovery)
- Appendix 3 — Module → release traceability

---

# A. Executive Summary

ClassHub is built as **one deployable ASP.NET Core application on .NET 10 LTS**, served from a single origin:

- `/api/*` — REST API (Minimal APIs, grouped by module), authenticated with **ASP.NET Core Identity** cookies and protected by **policy-based authorization**.
- `/app`, `/student`, `/admin` and the auth pages — a **React + TypeScript + Vite + React Router** single-page application (SPA). Its production build is served from `wwwroot`.
- `/`, `/{slug}`, `/{slug}/tests/{testSlug}`, `/{slug}/book` — **public teacher websites rendered server-side with Razor Pages**, so search engines and social cards get complete HTML. Small **React islands** (built by Vite) handle the interactive parts: contact form, diagnostic test, booking widget, abuse report.

Data lives in **SQL Server 2022 Express** (`ClassHubDb`) and is accessed with **EF Core 10**. The developer connects with **Windows Authentication over Shared Memory**. That works today without enabling TCP/IP, Mixed Mode or SQL logins; it was verified on this machine (B.4).

**Tenant isolation** (one workspace per teacher) has several layers:
- Workspace-scoped routes guarded by a membership + permission authorization handler.
- A request-scoped tenant context.
- **EF Core 10 named global query filters** that fail closed.
- A `SaveChanges` interceptor that blocks cross-tenant writes.
- **Composite foreign keys** in the database.
- A deny-by-default fallback policy with an endpoint-metadata test.
- An automated cross-tenant test kit that every phase extends.

**Delivery** is about 45 small phases, each on its own branch and PR. Every phase runs **autonomously**: implement → local checks → commit → **Codex CLI review** (`codex review --base origin/main`) → fixes → push/PR → **GitHub Actions** → **automatic squash merge** once every required quality and security gate passes → next phase. `docs/PROGRESS.md` and `docs/RECOVERY.md` keep the work resumable.

**Before MVP Beta, these items still need a decision or an account** (J.4): hosting and domain, final legal wording for minors' consent, final Free/Pro limit values, data export/deletion, and a Resend account. They do not block Releases 0–3; provisional, configurable defaults are used until then. One conditional check at R0-P2 can stop work early: Microsoft.Data.SqlClient must connect over Shared Memory.

---

# B. Technology and Compatibility Assessment

## B.1 Environment findings (this machine)

| Item | Finding | Impact |
|---|---|---|
| Project folder | `C:\dev\ClassHub`; contains only this plan file; not a git repo | OK. GitHub repo: `Andrej994x/classhub` (public) |
| .NET SDKs | **10.0.302**, 10.0.201, 9.0.312, 8.0.201 | Pin `global.json` to 10.0.302 (`rollForward: latestFeature`) |
| .NET runtimes | Microsoft.NETCore.App up to **10.0.14**; Microsoft.AspNetCore.App up to **10.0.10** | Sufficient for ASP.NET Core 10 |
| EF tool | Global `dotnet-ef` 10.0.10 | Repo pins `dotnet-ef` 10.0.14 in a local tool manifest (`dotnet-tools.json`) |
| Node.js / npm | v22.17.0 / 10.9.2 | Meets Vite 8 (`^20.19.0 \|\| >=22.12.0`). Node 24 LTS recommended before Beta (D-03) |
| Git | 2.43.0; global email is a **work address**; `core.autocrlf` unset | Repo-local GitHub noreply email (D-07); `.gitattributes` for line endings |
| GitHub CLI | 2.103.3, logged in as **Andrej994x** (`repo`, `workflow`, `read:org`, `gist`) | Can create the repo, push workflows, manage rulesets and PRs |
| Codex CLI | **0.164.0**, logged in with ChatGPT; `codex review` supports `--base`, `--commit`, `--uncommitted`, `--title`, custom prompt | `codex review --base origin/main` is valid |
| Docker | Docker Desktop installed | **Required** for integration and E2E tests (Testcontainers) |
| SQL Server | **SQL Server 2022 Express 16.0.1210.3**, the local `SQLEXPRESS` instance (`<machine>\SQLEXPRESS`), running | Supported by EF Core 10 / Microsoft.Data.SqlClient |
| SQL protocols | Shared Memory and Named Pipes available; **TCP/IP disabled**; SQL Browser disabled | Windows auth over Shared Memory works (B.4) |
| SQL auth mode | Windows Authentication only | Fits the approved approach. No Mixed Mode needed |
| SQL permissions | the developer's Windows domain account is `sysadmin`, has `CREATE ANY DATABASE` | EF can create `ClassHubDb` |
| Server collation | `Macedonian_FYROM_90_CI_AS` | DB collation set explicitly by EF (D-35) |
| `ClassHubDb` | Does not exist yet | Created by `dotnet ef database update` in R0-P2 |
| Reference project | `C:\dev\FamilyHub` (same owner; .NET 8 + React/Vite; xUnit v3; Testcontainers; CI with SHA-pinned actions, gitleaks, migration checks, i18n parity) | Its proven patterns are reused; no code is copied blindly |

## B.2 Recommended versions (registries, 2026-11-04)

**Backend (NuGet; managed centrally in `Directory.Packages.props`, with lock files):**

| Area | Package | Version | Note |
|---|---|---|---|
| Framework | .NET SDK / ASP.NET Core | 10.0.302 / 10.0.x | .NET 10 is LTS |
| ORM | Microsoft.EntityFrameworkCore.SqlServer, .Design | 10.0.14 | Brings Microsoft.Data.SqlClient (latest 7.1.1) transitively |
| Identity | Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.14 | |
| Data Protection | Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.x | Key storage (D-52) |
| OpenAPI | Microsoft.AspNetCore.OpenApi + Microsoft.Extensions.ApiDescription.Server | 10.0.14 | OpenAPI document generated at build time |
| API UI (dev only) | Scalar.AspNetCore | 2.18.7 | Development environment only |
| Validation | FluentValidation (+ DependencyInjectionExtensions) | 12.2.0 | Apache-2.0 |
| Logging | Serilog.AspNetCore | 10.0.0 | D-48 |
| Images | SkiaSharp | latest stable | MIT; profile-photo re-encoding (D-49) |
| Tests | xunit.v3 | 4.0.2 | |
| Tests | Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | `WebApplicationFactory<Program>` |
| Tests | Testcontainers.MsSql | 4.16.0 | SQL Server 2022 container |
| Tests | Respawn | 7.0.0 | Fast database reset between test classes |
| Tests | Shouldly | 4.3.0 | Assertions (FluentAssertions v8 requires a commercial license) |
| Tests | NetArchTest.Rules | 1.3.2 | Module-boundary rules |
| Later | AWSSDK.S3 (R2), Resend | 4.0.x / 0.24.0 | Behind interfaces; only when accounts exist |
| Tooling | dotnet-ef (local tool) | 10.0.14 | `dotnet tool restore` |

**Avoided on purpose:** MediatR and AutoMapper (commercial licenses since 2025; plain services and explicit mappers are enough), FluentAssertions v8 (commercial), ImageSharp (split license, unclear for a commercial SaaS), Hangfire/Redis/brokers (not needed yet), the EF Core InMemory provider (does not behave like SQL Server).

**Frontend (npm, single package in `web/`, `package-lock.json`):**

| Area | Package | Version |
|---|---|---|
| Build | vite / @vitejs/plugin-react | 8.3.5 / 6.2.1 |
| UI | react / react-dom | 19.3.1 |
| Routing | react-router | 8.5.1 (data router, SPA mode) |
| Styling | tailwindcss (+ @tailwindcss/vite), shadcn/ui (CLI, copied components) | 4.3.4 |
| Forms | react-hook-form + @hookform/resolvers, zod | 7.91.0, 4.6.7 |
| Server state | @tanstack/react-query | 5.105.3 |
| API client | openapi-typescript (types) + openapi-fetch (client) | 7.13.0 / 0.17.0 |
| i18n | i18next + react-i18next | 26.7.1 / 17.1.12 |
| Lint | oxlint | 1.91.0 |
| Language | typescript | pin **~6.0** (7.0.2 is latest; re-evaluate later, D-04) |
| Tests | vitest, @testing-library/react, jsdom | 5.1.0 |
| E2E | @playwright/test | 1.64.1 |
| PWA (R3) | vite-plugin-pwa | 2.0.0 |
| Tools (R3) | @excalidraw/excalidraw (MIT), jsxgraph (MIT OR LGPL-3.0, used under MIT) | 0.18.1 / 1.14.2 |

## B.3 EF Core + SQL Server conventions

| Topic | Risk | Convention |
|---|---|---|
| Schema changes | Silent or unreviewed changes | EF Core migrations only, reviewed in the PR. **Never applied on application startup** in Development (D-50); only the `Testing`/`E2E` environments migrate automatically, against disposable containers. CI fails on model changes without a migration |
| Unicode | Cyrillic corruption | All strings `nvarchar` (the EF default); explicit max lengths |
| Unique + NULL | SQL Server allows only one NULL in a unique index | EF adds `IS NOT NULL` filters automatically for unique indexes on nullable columns; other filters are explicit `HasFilter(...)` |
| Cascades | "Multiple cascade paths" errors; accidental mass deletes | `DeleteBehavior.NoAction` as the global convention; archive instead of delete |
| Keys | Index fragmentation | `Guid` keys with EF's SQL Server sequential GUID generator; no client-side `Guid.NewGuid()` for keys |
| Date/time | Ambiguous local times | UTC `DateTime` in `datetime2(3)` via a value converter that enforces `DateTimeKind.Utc`; IANA time zone stored separately; `TimeProvider` injected for testability |
| Money | Rounding errors | `long` minor units + ISO 4217 `char(3)` currency; never summed across currencies |
| JSON | No native JSON type in SQL Server 2022 | `nvarchar(max)` with `ISJSON` check constraint; EF complex/JSON mapping where it helps |
| Concurrency | Lost updates, races | `rowversion` on editable aggregates (`DbUpdateConcurrencyException` → 409); `READ_COMMITTED_SNAPSHOT ON`; `sp_getapplock` + unique constraints for critical invariants |
| RCSI migration | `ALTER DATABASE` cannot run inside a transaction | `migrationBuilder.Sql("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE", suppressTransaction: true)` |
| Collation | Server default differs from intent | `modelBuilder.UseCollation("Macedonian_FYROM_100_CI_AS_SC")`, applied when EF creates the database (D-35) |
| Retries | `EnableRetryOnFailure` conflicts with user transactions | No retry strategy locally; reconsider with cloud hosting (then via `CreateExecutionStrategy`) |
| Index keys | 1700-byte limit | Indexed strings ≤ `nvarchar(450)`; emails `nvarchar(256)` (Identity default) |
| Parameters | 2100-parameter limit | Batch large `Contains` lists |
| Express limits | 10 GB per DB, ≤ 4 cores, ~1.4 GB buffer pool, no SQL Agent | Fine for development and early use; scheduled work runs in-process (`BackgroundService`); production engine chosen with hosting |
| Encryption | Microsoft.Data.SqlClient encrypts by default; local self-signed certificate | `TrustServerCertificate=True` only in Development/CI connection strings |

## B.4 Windows Authentication — verification and approach

**Verified on 2026-11-04 (read-only `SELECT`s, Windows PowerShell SqlClient):**

| Connection | Result |
|---|---|
| `Server=<machine>\SQLEXPRESS;Integrated Security=True` | OK — transport **Shared memory**, auth NTLM, the developer's domain login, sysadmin |
| `Server=.\SQLEXPRESS;Integrated Security=True` | OK — Shared memory |
| `Server=np:\\.\pipe\MSSQL$SQLEXPRESS\sql\query;Integrated Security=True` | OK — Named pipe |
| `Server=tcp:localhost,1433;Integrated Security=True` | Fails — TCP/IP disabled (expected) |

**Approach (D-02, confirmed):**
- Development connection string (no secret; kept in `appsettings.Development.json`, overridable with `ConnectionStrings__ClassHub` or user-secrets):
  `Server=.\SQLEXPRESS;Database=ClassHubDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True`
  `.\` keeps the machine name out of the public repository.
- On Windows, Microsoft.Data.SqlClient uses the native network layer, which supports Shared Memory and Named Pipes. **R0-P2 must confirm this with EF Core before anything else** (first acceptance criterion). If it fails, work stops and the user is asked whether to enable TCP/IP. Windows auth also works over TCP, so even then **Mixed Mode and SQL logins are not needed**.
- `dotnet ef database update` creates `ClassHubDb` with the configured collation, running under the developer's Windows account. Only the database is created; no server-level setting is changed.
- **Accepted local risk:** the app runs as a `sysadmin` Windows account in development. This can hide permission problems, so a least-privilege runtime identity is defined with hosting (D-28) and checked in B-P2.
- **CI** has no Windows auth: integration and E2E tests use SQL Server 2022 Linux containers with a per-run generated `sa` password. The local SQL Server is never used by automated tests.

## B.5 Authentication — ASP.NET Core Identity (confirmed)

- **Identity stores** on EF Core (`AppUser : IdentityUser<Guid>`, `IdentityRole<Guid>`), in the same `ClassHubDbContext` and migration history.
- **Custom account endpoints** built on `UserManager`/`SignInManager`, not `MapIdentityApi`. Reasons: registration must create the workspace in the same transaction; emails must be localized (mk/en); we want cookie sessions only (no bearer tokens); responses must follow our ProblemDetails conventions and avoid account enumeration.
- **Passwords:** Identity `PasswordHasher` (PBKDF2-HMAC-SHA512, Identity V3 format). Policy: minimum length 10, no composition rules, plus a custom `IPasswordValidator` with an embedded common-password list. `RequireUniqueEmail = true`.
- **Lockout:** enabled for new users (for example 5 failures → 15 minutes), together with rate limiting.
- **Cookie sessions:** cookie `.ClassHub.Auth`; `HttpOnly`, `Secure`, `SameSite=Lax`, path `/`. Sliding expiration of 30 days for teachers/students and 12 hours for platform admins (D-36). For `/api` the API returns 401/403 instead of redirecting.
- **Revocation:** `SecurityStampValidator` interval of 1 minute (D-51), plus an account-status check in `OnValidatePrincipal`. Suspension, password reset and "log out everywhere" rotate the security stamp.
- **Tokens:** Identity token providers for email confirmation and password reset (single use, time-limited). **Data Protection keys** are persisted with EF (`PersistKeysToDbContext`) and protected with DPAPI locally, so cookies and tokens survive restarts (D-52). Keys never go in the repo.
- **MFA:** Identity's authenticator (TOTP) with recovery codes; **required** for `PlatformOwner`/`PlatformAdmin` through an authorization policy that checks the `amr=mfa` claim (R1-P9).
- **CSRF:** SameSite=Lax plus ASP.NET Core **antiforgery**. The SPA calls `GET /api/auth/antiforgery`, which sets a JS-readable `XSRF-TOKEN` cookie, and sends `X-XSRF-TOKEN` on unsafe requests. An endpoint filter validates it on all cookie-authenticated unsafe endpoints. Razor forms get antiforgery automatically.
- **Rate limiting:** built-in `Microsoft.AspNetCore.RateLimiting`, partitioned by IP and by account, for login, register, forgot/reset, resend-confirmation and the public forms.
- **Later:** ASP.NET Core external login providers (Google/Microsoft) if approved in V1.

## B.6 Local and CI database testing

- **Integration tests** (xUnit v3 + `WebApplicationFactory<Program>`): one **Testcontainers** SQL Server 2022 container per test run (image pinned to a CU tag). Migrations are applied once. Respawn resets data between test classes, and tests create their own users/workspaces with factories.
- **Safety guards** (pattern proven in FamilyHub): a `TestDatabaseGuard` accepts only the container's `host,port` and a `ClassHubTest_<guid>` database name, and refuses Integrated Security, `.`, named instances, pipes, LocalDB and `ClassHubDb`. A `DockerEndpointGuard` allows only local Docker endpoints. There is **no fallback** to SQLEXPRESS: if Docker is unavailable, the tests fail and the autonomous workflow stops (I.3).
- **E2E (Playwright):** a script (`scripts/e2e/start-sql.ps1` / `.sh`) starts a disposable SQL Server container. The API runs in the `E2E` environment, which migrates the empty DB on startup and serves the built SPA; Playwright runs against it. The same scripts run locally and in CI.
- **Migrations job in CI:** `has-pending-model-changes` → idempotent script → apply all migrations to an empty SQL Server 2022 container → re-apply the idempotent script (must be a no-op).

## B.7 SEO for public teacher websites — evaluation and solution

**Requirements:** public pages at `/{slug}` must be indexable and shareable. That means complete HTML on first response, unique title and description, canonical URL, Open Graph/Twitter cards (photo, name, subject), structured data, sitemap, robots rules, fast mobile loading, a 404 for unpublished sites, and `noindex` for previews. A client-rendered Vite SPA meets these only partially: social crawlers do not run JavaScript, and indexing is slower and less reliable.

| Option | Verdict |
|---|---|
| SPA + server-injected meta tags | Cards work, but page content still depends on JavaScript to be indexed. Rejected |
| Build-time prerender per publish | Strong SEO, but adds a render pipeline and cache invalidation. Rejected for MVP |
| SSR framework (Node) | Adds a second server runtime. Rejected |
| **Razor Pages for public sites + React islands** | Full server HTML from the same ASP.NET Core app, one origin and one cookie domain, minimal JavaScript. **Chosen (D-43, confirmed)** |

**Design:**
- Razor Pages in `ClassHub.Api/Pages/Public/`: landing page `/`, teacher site `/{slug}`, public test `/{slug}/tests/{testSlug}`, booking `/{slug}/book`. Each template is a set of partial views (2–3 templates).
- Data comes from **public query services** that return allow-listed projections: published only, not hidden by moderation, owner active.
- Head: `<title>`, meta description, canonical, `og:*`/`twitter:*`, `hreflang` (future bilingual sites), and **JSON-LD** (schema.org `Person`, `Service`/`Offer`, `FAQPage` where applicable).
- `/sitemap.xml` (published sites and public tests) and `/robots.txt` (disallow `/app`, `/student`, `/admin`, `/api`).
- **Performance:** Tailwind CSS v4 build that scans `.cshtml` files (`@source`), responsive images with `srcset`, no SPA bundle on public pages. **OutputCache** (in-memory) is cleared by tag when the teacher publishes or edits.
- **React islands:** Vite multi-entry build (`web/src/islands/*`) with a `manifest.json`. A small tag helper (`<vite-island name="contact-form" props="...">`) emits hashed script/style tags in production and Vite dev-server URLs in Development.
- **Preview:** the SPA editor shows the Razor page in an iframe using a short-lived, signed **preview token** that renders the draft with `noindex`. This keeps a single renderer.
- **Page views:** counted on the server in the Razor handler. No cookies, no IP storage (D-32).

---

# C. Architecture Proposal

## C.1 Repository structure

```
classhub/
├─ ClassHub.slnx                      .NET 10 solution (XML format)
├─ global.json                        SDK 10.0.302, rollForward latestFeature
├─ Directory.Build.props              net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors, analyzers
├─ Directory.Packages.props           central package versions; RestorePackagesWithLockFile
├─ dotnet-tools.json                  dotnet-ef 10.0.14
├─ src/
│  ├─ ClassHub.Api/                   ASP.NET Core host (composition root)
│  │  ├─ Program.cs                   services, middleware, auth, endpoint mapping, CLI commands
│  │  ├─ Pages/Public/                Razor Pages: landing, teacher sites, public tests, booking
│  │  ├─ TagHelpers/                  Vite island/asset tag helper
│  │  ├─ appsettings*.json            no secrets
│  │  └─ wwwroot/dist/                Vite build output (git-ignored)
│  └─ ClassHub.Core/                  all modules + shared kernel + infrastructure
│     ├─ SharedKernel/                ITenantContext, ITenantOwned, errors, Money, clock, permissions
│     ├─ Infrastructure/              ClassHubDbContext, conventions, interceptors, Migrations/,
│     │                               email (outbox, dev mailbox), files (IFileStorage), jobs
│     └─ Modules/<Module>/
│        ├─ Contracts/                public DTOs/interfaces other modules may use
│        ├─ Domain/                   entities, value objects, state machines, rules
│        ├─ Application/              services (use cases), validators
│        ├─ Endpoints/                Minimal API route groups (IModule.MapEndpoints)
│        └─ Persistence/              EF entity configurations, query services
├─ tests/
│  ├─ ClassHub.UnitTests/             xUnit v3 + Shouldly; no database
│  ├─ ClassHub.IntegrationTests/      xUnit v3 + WebApplicationFactory + Testcontainers + Respawn
│  └─ ClassHub.ArchitectureTests/     NetArchTest + endpoint-metadata + source-scan rules
├─ web/                               npm package (Vite + React + TS)
│  ├─ src/app/                        router, layouts (auth, teacher /app, student, admin)
│  ├─ src/features/<module>/          screens, hooks, Zod form schemas
│  ├─ src/islands/                    contact-form, diagnostic-test, booking-widget, report-abuse
│  ├─ src/styles/public.css           Tailwind entry for Razor public pages (@source ../../src/ClassHub.Api/Pages)
│  ├─ src/api/                        schema.d.ts (generated from OpenAPI), openapi-fetch client
│  ├─ src/i18n/{mk,en}.json           translations + parity check script
│  ├─ src/components/ui/              shadcn/ui components
│  └─ e2e/                            Playwright tests
├─ scripts/                           e2e SQL container, OpenAPI type generation, i18n check
├─ docs/                              ARCHITECTURE, DEVELOPMENT, DATABASE, TESTING, SECURITY, ROADMAP, PROGRESS, RECOVERY
├─ .github/                           workflows, dependabot.yml, pull_request_template.md
└─ README.md  CLAUDE.md  AGENTS.md  .gitignore  .gitattributes  .editorconfig
```

**Why two backend projects (D-46):** a solo developer gets clear boundaries without per-module project overhead. `ClassHub.Api` is only the host; `ClassHub.Core` contains the modules. **NetArchTest rules** enforce that a module uses another module only through its `Contracts` namespace, and that `Domain` does not depend on `Infrastructure`/ASP.NET. A module can be split into its own project later if it grows.

## C.2 Backend modules

Each module implements `IModule` (`AddServices`, `MapEndpoints`), owns its entities and EF configurations, and exposes only its `Contracts`. Cross-module reactions use in-process domain events or the outbox.

| Module | Product modules | Introduced | Owns |
|---|---|---|---|
| SharedKernel / Infrastructure | cross-cutting | R0 | DbContext, conventions, tenancy, audit, outbox/jobs, email, files |
| `Identity` | M01 | R0-P4 | AppUser, Identity stores, account endpoints, MFA |
| `Access` | roles/permissions | R0-P5 | platform roles, workspace memberships, permission map, policies |
| `Workspaces` | M01 | R0-P5 | workspace, settings (timezone, currency, locale), onboarding state |
| `TeacherProfile` | M01 | R1-P1 | profile, subjects/languages/levels, photo |
| `Catalog` | M15 reference data | R1-P1 | subjects, categories, levels |
| `PublicSite` | M02 | R1-P3/P4 | site, template, sections, slug, publish state, page views, public queries |
| `Offers` | M05 | R1-P2 | offers |
| `Leads` | M03 | R1-P5 | leads, statuses, notes |
| `Diagnostics` | M04 | R1-P6/P7 | tests, questions, options, attempts |
| `Admin` | M15 | R1-P9 | admin endpoints and aggregate queries |
| `Subscriptions` | M14 | R1-P10 | plans, limits, subscriptions, entitlements |
| `Moderation` | M15 | R1-P11 | abuse reports, hidden content |
| `Students` | M06 | R2-P1–P3 | students, groups, invitations |
| `Scheduling` | M07 | R2-P4–P6 | availability, booking requests, recurrence, conflict checks |
| `Lessons` | M08 | R2-P4/P7 | lessons, participants, attendance, notes |
| `Materials` | M10 | R2-P8 | materials, assignments |
| `Homework` | M10 | R2-P9 | homework, submissions, reviews |
| `Finance` | M11 | R2-P10 | packages, consumptions, payments, adjustments |
| `Notifications` | M12 | R2-P11 (outbox R0-P4) | templates, preferences, reminders |
| `Dashboards` | M13 | R1-P8 / R2-P12 | read-only query services |
| `Toolbox` | M09 | R3-P1–P5 | tool catalog, activities |
| `Analytics` | M13/M15 usage | R3-P6 | aggregated usage counters |

## C.3 API conventions

- **Minimal APIs with route groups per module** (D-47): `/api/auth/*`, `/api/me/*`, `/api/w/{workspaceId:guid}/<module>/*`, `/api/public/*` (anonymous, rate-limited), `/api/admin/*`.
- **Validation:** FluentValidation validators per request, run by an endpoint filter → `ValidationProblem` (400). Business rules live in domain/application services.
- **Errors:** `AddProblemDetails` + `IExceptionHandler` → RFC 9457 ProblemDetails with extensions `code` (stable error code, for example `LEAD_NOT_FOUND`) and `traceId`. No stack traces outside Development.
- **DTOs:** explicit request/response records. **Entities are never serialized directly.**
- **OpenAPI → TypeScript:** the OpenAPI document is generated at build time to `artifacts/openapi/classhub.json`. `openapi-typescript` generates `web/src/api/schema.d.ts`, and CI fails if the regenerated file differs (contract-drift check). The SPA uses `openapi-fetch` with `credentials: 'include'` and the XSRF header.
- **Frontend validation:** hand-written Zod schemas for forms, matching the API rules for UX. The server is authoritative.
- Pagination: `page`/`pageSize` with a maximum; UTC ISO 8601 timestamps; `If-Match`/rowversion for concurrent edits where relevant.

## C.4 Frontend organization

- **SPA** (`web/`): React Router data router with route groups and layouts:
  - `/login`, `/register`, `/verify`, `/reset` (auth layout)
  - `/app/*` (teacher: sidebar on desktop, drawer/bottom navigation on mobile)
  - `/student/*` (simple portal)
  - `/admin/*` (admin console)
- **Data:** TanStack Query for all server state; query keys per workspace; mutations invalidate queries.
- **Forms:** React Hook Form + `zodResolver`; server ProblemDetails field errors are mapped onto form fields.
- **i18n:** i18next/react-i18next with `mk` (default) and `en`. The user's language is stored on the account and in a cookie. A CI script checks key parity and placeholders.
- **Formatting:** `Intl` date/time/number/currency in the viewer's time zone; currency always shown with its code or symbol.
- **Mobile-first:** designed at 360 px first; Playwright mobile projects for critical flows.
- **Islands** (`web/src/islands/`): small React entry points mounted into Razor pages. They share UI components and i18n with the SPA but load only what they need.
- **Lazy routes** for heavy tools (Excalidraw, JSXGraph) in Release 3.

## C.5 Single origin and routing

| Path | Served by |
|---|---|
| `/api/*` | API endpoints |
| `/app/*`, `/student/*`, `/admin/*`, `/login`, `/register`, `/verify`, `/reset` | SPA (`MapFallbackToFile("dist/index.html")` for these prefixes) |
| `/`, `/{slug}`, `/{slug}/tests/{testSlug}`, `/{slug}/book`, `/sitemap.xml`, `/robots.txt` | Razor Pages |
| `/dist/*` | static SPA and island assets (hashed, long cache) |
| `/dev/mailbox` | Development-only dev mailbox page |

- **Reserved slugs** (`app`, `student`, `admin`, `api`, `login`, `register`, `verify`, `reset`, `dist`, `dev`, `sitemap.xml`, `robots.txt`, …) are a C# constant list, enforced on slug creation and exposed to the SPA via an endpoint.
- **Development:**
  - ASP.NET Core on `http://localhost:5080` (Razor public pages, API).
  - Vite on `http://localhost:5173` for the SPA with HMR, proxying `/api` to 5080. Cookies on `localhost` are shared across ports, and both origins are same-site.
- **E2E/CI and production-like runs:** the built SPA is served by ASP.NET Core from one origin.

## C.6 Authorization

- **Deny by default:** `FallbackPolicy = RequireAuthenticatedUser`. An architecture test enumerates `EndpointDataSource` and fails if any endpoint lacks either explicit `AllowAnonymous` or a **named ClassHub policy**, so relying on the fallback alone is not allowed.
- **Platform roles:** Identity roles `PlatformOwner`, `PlatformAdmin`.
  - Policies `Platform.Owner` and `Platform.Admin` also require MFA (`amr=mfa`) and a recent sign-in for critical actions (step-up).
  - Platform roles grant **no** workspace access.
- **Workspace permissions:** code-defined permissions (`students.read`, `lessons.manage`, `finance.manage`, …) mapped from workspace roles (`Owner` now; `AcademyManager`, `AcademyTeacher` later).
  - `WorkspacePermissionRequirement` + handler: reads `{workspaceId}` from the route, verifies an active membership, checks the permission, and fills `ITenantContext`.
  - Policies are created on demand by a custom `IAuthorizationPolicyProvider` (`ws:<permission>`).
- **Student portal:** policy `Student.Self`. Data is resolved through `Students.UserId = currentUser` and assignment tables; never by route-supplied student IDs alone.
- **Cross-tenant access returns 404**, so the existence of other tenants' data is not revealed.

## C.7 Multi-tenant isolation

| Layer | Mechanism | Protects against |
|---|---|---|
| 1. Routing + policy | `/api/w/{workspaceId}` + membership/permission handler | Using a workspace you do not belong to |
| 2. Context | Scoped `ITenantContext`, set only by the handler after verifying membership; never from headers or body | Spoofed tenant IDs |
| 3. Reads | **EF Core 10 named query filter `"Tenant"`** on every `ITenantOwned` entity, applied by a model-building convention. With no tenant set it matches nothing (fail closed). A separate `"Archived"` filter can be switched off independently | Forgotten `WHERE WorkspaceId` |
| 4. Writes | `SaveChangesInterceptor`: sets `WorkspaceId` on insert, rejects Added/Modified/Deleted tenant entities from another workspace, forbids changing `WorkspaceId` | Cross-tenant writes |
| 5. Database | **Composite tenant FKs**: parents `HasAlternateKey(WorkspaceId, Id)`, children `HasForeignKey(WorkspaceId, ParentId).HasPrincipalKey(WorkspaceId, Id)`; per-workspace unique indexes | Cross-tenant links even if code is wrong |
| 6. Bypass control | Source-scan test: `IgnoreQueryFilters`, `ExecuteSqlRaw/Interpolated` and `SqlQuery` are allowed only in allow-listed classes (`*PublicQueries`, `*AdminQueries`, `Jobs/*`, migrations), each with explicit predicates | Silent filter bypass |
| 7. Output | Explicit DTOs; separate allow-listed public projections | Leaking private fields |
| 8. Tests | **Tenant isolation kit**: two teachers; teacher B tries list/get/update/delete/link on teacher A's resources → 404 and no change. Extended in every phase | Regressions |
| 9. Audit | Admin, role, plan, finance and suspension actions recorded with actor, time and before/after | Undetected misuse |

**Optional later (B-P2):** SQL Server Row-Level Security with `SESSION_CONTEXT` as defense in depth.

**Platform admins:** admin endpoints return account metadata and aggregates only, never student records, notes, materials, homework or payments. No impersonation in MVP (D-31).

**Future academies:** memberships with more roles in the same workspace. Every rule is already "membership + permission in this workspace", and a user can belong to several workspaces (the workspace is in the route). Adding academies does not require a redesign.

## C.8 Cross-cutting concerns

- **Logging:** Serilog (console JSON in CI, readable locally), request logging with `TraceId`. Destructuring policies redact passwords, tokens, cookies, authorization headers and security stamps. **No student personal data or lead contents in logs**; emails masked.
- **Configuration:** strongly typed options validated on start (`ValidateDataAnnotations`/`ValidateOnStart`). Secrets only in user-secrets/environment variables. `appsettings.json` contains no secrets.
- **Security headers:** CSP (strict for SPA and public pages; dev allows the Vite server), `X-Content-Type-Options`, `Referrer-Policy`, `frame-ancestors` (`'self'` only for the preview iframe), HSTS outside Development.
- **Localization:** SPA via i18next. Server-side `IStringLocalizer` with `.resx` (mk/en) for emails and Razor public-page chrome. Public site content renders in the site's language (D-15). `RequestLocalizationMiddleware` for Razor.
- **Time:** UTC storage, IANA zones (.NET uses ICU on Windows), `TimeProvider`. Recurrence is expanded in the workspace zone (DST-safe for Europe/Skopje).
- **Files:** `IFileStorage` (`SaveAsync`, `OpenReadAsync`, `DeleteAsync`).
  - `LocalDiskFileStorage` under `App_Data/uploads` (git-ignored, outside `wwwroot`); an R2 implementation (AWSSDK.S3) later.
  - Uploads: size limits, extension + magic-byte checks, random storage keys.
  - Images re-encoded with SkiaSharp (strips EXIF).
  - Downloads only through authorized endpoints (`Results.File`, `Content-Disposition: attachment`, `nosniff`).
- **Background processing:** `BackgroundService` polling an outbox/jobs table and claiming rows with `UPDATE TOP (n) … WITH (UPDLOCK, READPAST, ROWLOCK) … OUTPUT`. Idempotency keys. Used for emails (R0-P4), subscription expiry (R1-P10), reminders and recurrence horizon (R2).
- **Email:** `IEmailSender` abstraction. **Dev mailbox** (stored in DB, visible at `/dev/mailbox` in Development only, logged without content). Resend implementation when an account and domain exist (D-10).
- **Concurrency-critical invariants** (no overlapping confirmed lessons per teacher, group capacity, one package consumption per lesson participant, plan limits): a transaction with `sp_getapplock` (per teacher/group/workspace), **plus** unique constraints, **plus** parallel-request integration tests.
- **Money:** minor units + currency; price snapshots copied to assigned packages.

## C.9 Conventions

- C#: nullable enabled, warnings as errors, analyzers (`AnalysisLevel latest`), file-scoped namespaces, `sealed` by default, `record` DTOs, async all the way with `CancellationToken`.
- TypeScript: `strict`, no `any` without a comment, oxlint clean.
- Status transitions are explicit state machines with unit tests.
- Each phase adds only the migrations, permissions, translations (mk + en), docs and tests for its own scope.
- Conventional Commits; Codex review summary in every PR.

---

# D. Complete Release Roadmap

Branch prefixes: `chore/` (setup, maintenance, hardening), `feature/` (business features), `fix/` (bug fixes). One phase = one branch = one PR = one squash commit on `main`.

| Release | Phase ID | Phase name | Business objective | Main functionality | Depends on | Definition of Done |
|---|---|---|---|---|---|---|
| R0 | R0-P0 | Local & GitHub prerequisites | Ready environment | SDK/Node/Docker check, git identity, create public repo, repo settings, bootstrap commit, Codex login | plan approval | Repo exists with `main`; tools verified |
| R0 | R0-P1 | Repository foundation & CI | Stable base | slnx, props, Api `/health`, Core, test projects, Vite web app, CI + security workflows, PR gate, base docs, ruleset | R0-P0 | CI green; ruleset active; first auto-merge |
| R0 | R0-P2 | EF Core & SQL Server foundation | Reliable persistence | DbContext, conventions, local `dotnet-ef`, first migrations (incl. RCSI), DB health, Windows-auth verification, Testcontainers harness, migrations CI job | R0-P1 | `ClassHubDb` created via EF; integration + migration jobs green |
| R0 | R0-P3 | API platform baseline | Consistent, secure API | ProblemDetails, FluentValidation filter, Serilog/redaction, security headers, rate limiting, fallback policy + metadata test, antiforgery, OpenAPI + TS types + drift check, module registration, architecture tests | R0-P2 | Platform tests green |
| R0 | R0-P4 | Authentication (Identity) | Teachers can have accounts | Register, confirm email, login, logout, me, forgot/reset, lockout, cookie, stamp validation, Data Protection, outbox + dev mailbox | R0-P3 | Auth integration tests incl. abuse cases |
| R0 | R0-P5 | Workspaces, roles & tenancy | Isolated workspaces | Workspace provisioning, memberships, platform roles, permission policies, tenant context, named filters, interceptor, composite FKs, bypass test, isolation kit, audit, owner CLI | R0-P4 | Cross-tenant tests green |
| R0 | R0-P6 | Web foundation & localization | Bilingual, responsive shell | React Router layouts, Tailwind/shadcn, i18next mk/en + parity, TanStack Query + openapi-fetch, error mapping, build into `wwwroot` + SPA fallback | R0-P3 | Shells in mk/en on mobile and desktop |
| R0 | R0-P7 | Auth UI & first E2E | End-to-end sign-up | Register/verify/login/logout/reset screens, protected areas, empty dashboard, Playwright (desktop + mobile), E2E CI job | R0-P5, R0-P6 | Playwright: register → verify → login → workspace |
| R0 | R0-P8 | Foundation hardening & closeout | Release 0 sign-off | Whole-repo Codex security review, docs complete, ruleset/PROGRESS/RECOVERY verified, fresh-clone test | R0-P7 | Release 0 DoD met |
| R1 | R1-P1 | Teacher profile & onboarding | Teacher identity | Profile, photo (SkiaSharp, IFileStorage), subjects/languages/levels, timezone/currency, onboarding checklist, catalog seed | R0-P8 | Profile complete; checklist reflects data |
| R1 | R1-P2 | Offers & programs | Present services | Single lesson, package, group program; price, duration, mode, capacity, active | R1-P1 | Offers CRUD with rules; isolation tests |
| R1 | R1-P3 | Public site model & editor | No-code website | Slug, templates, sections, colors, show/hide, SPA editor, Razor preview via token | R1-P2 | Teacher edits and previews site |
| R1 | R1-P4 | Public site rendering, SEO & islands | Online presence | Razor rendering, publish/unpublish, SEO head + JSON-LD, sitemap/robots, OutputCache, page views, Vite island infrastructure | R1-P3 | Published site indexable at `/{slug}`; unpublished 404 |
| R1 | R1-P5 | Leads management | Capture interest | Contact/lesson-request island, spam protection, lead inbox, statuses, notes, search/filter, email to teacher | R1-P4 | Visitor sends lead; teacher manages it |
| R1 | R1-P6 | Diagnostic test builder | Lead magnet | Tests, MCQ questions, correct answers, points, difficulty, publish/deactivate | R1-P1 | Teacher publishes a valid test |
| R1 | R1-P7 | Public test taking & results | Attract students | Razor test page + island, server-side scoring, result, optional contact → lead, abuse protection | R1-P5, R1-P6 | Visitor completes test; correct score; lesson request |
| R1 | R1-P8 | Basic teacher dashboard | Business overview | New leads, attempts, page visits, offers, onboarding progress | R1-P7 | Numbers match data |
| R1 | R1-P9 | Platform admin foundation | Operate platform | TOTP MFA, admin console, users/workspaces overview, suspend/reactivate, role management, audit viewer | R0-P8 | Owner manages users with MFA; audited |
| R1 | R1-P10 | SaaS plans & entitlements | Subscription model | Plans/limits, Free on registration, entitlements, manual Pro activation/extension, expiry job, SaaS payment records | R1-P9 | Limits enforced; no data loss on expiry |
| R1 | R1-P11 | Moderation & reference data admin | Safe public content | Abuse report island, moderation queue, hide content, catalog admin | R1-P9, R1-P4 | Report → hide works; audited |
| R1 | R1-P12 | Release 1 hardening | Release 1 sign-off | E2E publish → lead, test → lead, suspension; isolation sweep; SEO checks; mobile; docs | R1-P1…P11 | Release 1 DoD met |
| R2 | R2-P1 | Students | Student records | CRUD, archive, notes, contacts, lead → student conversion with dedupe, Free student limit | R1-P12 | No duplicate conversions |
| R2 | R2-P2 | Groups | Group teaching | Groups, members, archive | R2-P1 | Student in many groups |
| R2 | R2-P3 | Student accounts & invitations | Student access | Invitation tokens, account linking, student portal shell, minors safeguards | R2-P1 | Student sees only own data |
| R2 | R2-P4 | Lessons core & calendar | Organized schedule | Lessons, participants, status machine, overlap prevention (`sp_getapplock`), day/week/month calendar | R2-P2 | Concurrent overlaps impossible |
| R2 | R2-P5 | Recurring lessons | Weekly routines | Series, materialized occurrences, edit scopes, conflicts | R2-P4 | Recurrence + DST tests green |
| R2 | R2-P6 | Availability & booking | Self-service booking | Working hours, slots, booking island on Razor page, confirm/decline, reschedule, cancel | R2-P5, R1-P5 | Booking confirmed without conflicts |
| R2 | R2-P7 | Attendance, notes & history | Lesson records | Complete lesson (idempotent), attendance, notes, history | R2-P4 | Completed exactly once |
| R2 | R2-P8 | Materials | Share resources | Upload, private storage, assign to student/group, student download, quota | R2-P3 | Only assigned students access |
| R2 | R2-P9 | Homework | Assignments | Homework, submissions with files, review, statuses | R2-P8 | Assign → submit → review |
| R2 | R2-P10 | Packages & payments | Financial records | Price snapshots, consumption ledger, reversal, manual/partial payments, balances, adjustments, report | R2-P7 | Consumption exactly once; balances correct |
| R2 | R2-P11 | Notifications | Timely information | Templates mk/en, lesson events, reminders (BackgroundService), preferences, dedupe | R2-P6, R2-P9 | No duplicates; no reminders for cancelled lessons |
| R2 | R2-P12 | Dashboards (teacher + student) | Visibility | Student dashboard; teacher finance/lesson metrics | R2-P10, R2-P11 | Correct per-user numbers |
| R2 | R2-P13 | Release 2 hardening | Release 2 sign-off | E2E lead → student → booking → lesson → package → payment; concurrency + isolation sweep | all R2 | Release 2 DoD met |
| R3 | R3-P1 | Toolbox catalog | Discover tools | Admin catalog, categories, featured, search, official links | R2-P13 | Teacher finds and opens tools |
| R3 | R3-P2 | Activity Library | Reuse work | Activities (versioned JSON documents), CRUD, duplicate, plan limits | R3-P1 | Save/reopen/edit/delete own activities |
| R3 | R3-P3 | Drawing Board | Visual teaching | Excalidraw (lazy route), save/reopen | R3-P2 | Drawing round-trips |
| R3 | R3-P4 | Geometry Board — constructions | Math teaching | JSXGraph primitives, coordinate system, own format, save/reopen | R3-P2 | Constructions round-trip |
| R3 | R3-P5 | Geometry Board — functions & graphs | Math teaching | Safe expression parser, plotting, persistence | R3-P4 | Graphs round-trip |
| R3 | R3-P6 | Usage analytics | Product insight | Aggregated counters, teacher and admin views | R3-P3, R3-P5 | Correct, no PII |
| R3 | R3-P7 | PWA & mobile improvements | Install on phone | vite-plugin-pwa (SPA scope), offline fallback, install prompt, mobile audit | R3-P6 | Installable; safe offline fallback |
| R3 | R3-P8 | Release 3 hardening | Release 3 sign-off | E2E toolbox flows, review, docs | all R3 | Release 3 DoD met |
| Beta | B-P1…B-P7 | MVP Beta | Real-teacher validation | Regression, security, isolation, performance, mobile/UX, release readiness, pilot | R3-P8 + blockers in J.4 | See G |
| V1 | V1-* | Advanced Teaching | High level | AI assistant, learning journey, parent portal, interactive assignments, calendar integrations, push, online payments | Beta | Planned later |
| V2 | V2-* | Academy & Courses | High level | Academy roles, multiple teachers, course builder, recorded courses, memberships, marketplace, marketing automation, referrals | V1 | Planned later |

**Order:** phases run **strictly in sequence** in the autonomous workflow; each phase starts from the freshly merged `main`. The admin track (R1-P9 → P11) depends only on R0-P8, but it is scheduled after R1-P8 to keep one active branch at a time.

---

# E. Detailed Release 0 Plan

**Release 0 Definition of Done:**
- A teacher can register, confirm their email, log in, and use their own isolated workspace, in Macedonian or English, on phone and desktop.
- A second teacher cannot access the first teacher's workspace.
- All CI gates pass: build, unit, architecture, integration (Testcontainers), migrations, frontend, E2E, security, PR gate.
- Every phase was auto-merged through the ruleset.
- Setup, progress and recovery are documented.

**Common Definition of Done for every phase (all releases):**
1. Only the phase scope is implemented; no unrelated refactoring; no future-release features.
2. Locally green:
   - `dotnet build -warnaserror`
   - `dotnet test` (unit, architecture, integration)
   - `npm run lint`, `npm run i18n:check`, `npm test`, `npm run build` (in `web/`)
   - Playwright, when the phase touches a critical flow
3. New tenant-owned entities implement `ITenantOwned`, use composite tenant FKs, and are covered by the isolation kit.
4. New endpoints have explicit `AllowAnonymous` or a named policy (metadata test passes).
5. Migrations are generated, reviewed, applied to an empty database in CI, and have a working `Down`. No model changes without a migration.
6. UI text exists in **mk and en**; screens checked at mobile and desktop widths.
7. Codex review run on the final branch state; critical/high findings fixed; others fixed or justified in the PR.
8. Docs updated: `docs/PROGRESS.md` (always), plus other `docs/*`, `AGENTS.md`/`CLAUDE.md` when conventions change.
9. GitHub Actions green, **PR auto-squash-merged** by the ruleset gates, and local `main` synced.

---

### R0-P0 — Local & GitHub Prerequisites (no feature branch)

1. **Objective:** remove environment blockers before the first phase.
2. **Business functionality:** none (enabler).
3. **User flows:** n/a.
4. **Scope:**
   - Verify `dotnet --version` (10.0.302), `node -v` (≥ 22.12), `docker info` (Linux containers running), `gh auth status`, `codex login status`.
   - `git init`; set a **repo-local** identity: `user.name` and the GitHub noreply email `<id>+Andrej994x@users.noreply.github.com`, using the ID from `gh api user --jq .id` (D-07).
   - `gh repo create Andrej994x/classhub --public --description "ClassHub — teacher growth platform"`.
   - Repository settings via `gh api`: allow **squash merge only**, **allow auto-merge**, **automatically delete head branches**, secret scanning + push protection, Dependabot alerts and security updates, private vulnerability reporting.
   - **Bootstrap commit on `main`** (D-09, the only commit not made through a PR): `.gitignore` (.NET, Node, `wwwroot/dist`, `App_Data`, `.codex-review/`, `*.mdf/*.ldf/*.bak/*.bacpac`, test results, Playwright reports), `.gitattributes`, `README.md` stub, this plan as `docs/ROADMAP.md`, initial `docs/PROGRESS.md`. Then push.
   - **No SQL Server steps** (Appendix 1).
5. **Exclusions:** application code, packages, rulesets (they need check names from R0-P1).
6. **Dependencies:** approval of this plan (authorizes repo creation and the listed settings).
7. **Business rules:** no secrets, database files or personal data in the repo.
8. **Acceptance criteria:** public repo exists with `main`; settings applied; local identity uses the noreply email; Docker and Codex available.
9. **Testing:** command checks above, recorded in `docs/PROGRESS.md`.
10. **Security:** work email not published; push protection on.
11. **DoD:** prerequisites recorded as complete in `docs/PROGRESS.md`.

### R0-P1 — Repository Foundation & CI

Full detail in **section K**. Branch: `chore/r0-p1-repository-foundation`.

### R0-P2 — EF Core & SQL Server Foundation

1. **Objective:** reliable, reviewable persistence on SQL Server, locally (Windows auth) and in CI (containers).
2. **Business functionality:** none directly; every module depends on it.
3. **User flows:** developer runs `dotnet tool restore`, `dotnet ef migrations add <Name>`, `dotnet ef database update`, `dotnet test`.
4. **Scope:**
   - `ClassHubDbContext` in `ClassHub.Core/Infrastructure`, registered in the Api host with `UseSqlServer(connectionString)`; migrations assembly `ClassHub.Core`.
   - **Conventions:** global `NoAction` deletes, UTC `DateTime` converter (`datetime2(3)`), string length defaults, sequential GUID keys, `rowversion` base support, `UseCollation("Macedonian_FYROM_100_CI_AS_SC")`, `ApplyConfigurationsFromAssembly`.
   - `dotnet-tools.json` pinning `dotnet-ef` 10.0.14.
   - First migrations: `InitialTechnical` (`SchemaInfo` table) and `EnableReadCommittedSnapshot` (`suppressTransaction: true`).
   - Health: `/health/live` (process) and `/health/ready` (DB, via `AddDbContextCheck`), anonymous, with no details in responses.
   - **Windows-auth verification:** the local `dotnet ef database update` creates `ClassHubDb` over Shared Memory; `SELECT net_transport, auth_scheme` is recorded in `docs/DATABASE.md`.
   - **Integration test harness:** `ClassHubApiFactory : WebApplicationFactory<Program>`, Testcontainers SQL Server (pinned image), `TestDatabaseGuard`, `DockerEndpointGuard`, migrations applied once, Respawn checkpoints, test data builders.
   - CI jobs `integration` and `migrations` (B.6).
   - `docs/DATABASE.md` (conventions from B.3, connection, migrations workflow, test databases).
5. **Exclusions:** business tables, Identity, tenancy, RLS, seed data.
6. **Dependencies:** R0-P1.
7. **Business rules:** none; technical: explicit migrations only (D-50).
8. **Acceptance criteria:**
   - **(gate)** EF Core connects to `.\SQLEXPRESS` with Integrated Security over Shared Memory. If not, **stop** and ask about enabling TCP/IP (J.4).
   - `ClassHubDb` exists with the configured collation and RCSI on.
   - `/health/ready` reports healthy locally and in integration tests.
   - The `migrations` CI job passes: pending-model check, idempotent script, apply to an empty DB, re-apply no-op.
   - Integration tests refuse to run against non-container databases (guard tests).
9. **Testing:**
   - Integration: connection; migration up/down on an empty container DB; UTC round-trip; Cyrillic `nvarchar` round-trip; filtered unique index with NULLs; collation; RCSI on.
   - Guard unit tests (no Docker).
10. **Security:** no credentials anywhere (Windows auth locally; generated CI passwords masked); connection failures logged without connection strings.
11. **DoD:** common DoD.

### R0-P3 — API Platform Baseline

1. **Objective:** every future endpoint inherits validation, errors, logging, security headers, rate limits and deny-by-default authorization.
2. **Business functionality:** none directly.
3. **User flows:** n/a (developer-facing).
4. **Scope:**
   - `AddProblemDetails` + global `IExceptionHandler`; error-code catalog; `DomainException` types → status codes.
   - FluentValidation endpoint filter → `ValidationProblem`.
   - Serilog bootstrap, request logging, redaction policies, `TraceId` in responses.
   - Security headers middleware (CSP, nosniff, referrer policy, frame-ancestors), HSTS outside Development, `ForwardedHeaders` configuration (off by default; ready for hosting).
   - `AddRateLimiter` with named policies (`auth`, `public-form`, `public-read`, `default`).
   - `FallbackPolicy` + `ClassHubPolicies` registry; **endpoint-metadata architecture test**.
   - Antiforgery services + `GET /api/auth/antiforgery` + unsafe-method validation filter (wired to authenticated groups in R0-P4).
   - `IModule` registration pattern; NetArchTest module-boundary rules.
   - OpenAPI document generation at build + Scalar UI (Development only) + `scripts/generate-api-types` + CI drift check.
   - Typed options with validation on start.
5. **Exclusions:** Identity, users, permissions content.
6. **Dependencies:** R0-P2.
7. **Business rules:** none.
8. **Acceptance criteria:**
   - Invalid input → 400 ValidationProblem with field errors and `code`.
   - Unhandled error → 500 ProblemDetails with `traceId` and no stack trace.
   - An endpoint without explicit authorization metadata fails the architecture test.
   - Rate limit → 429.
   - Logs never contain redacted fields (test sink).
   - OpenAPI types are regenerated with no diff.
9. **Testing:** unit (validators, exception mapping, redaction); integration (400/404/409/429/500 shapes, security headers present, antiforgery rejection); architecture tests.
10. **Security:** no stack traces; redaction; request body size limits; CSP verified by test.
11. **DoD:** common DoD.

### R0-P4 — Authentication (ASP.NET Core Identity)

1. **Objective:** teachers can create and securely use accounts.
2. **Business functionality (M01):** registration with email and password, email confirmation, login/logout, password reset, protection against unauthorized access, account status.
3. **User flows:**
   - Register → account created (unconfirmed) → confirmation email (dev mailbox) → link → confirmed.
   - Login → cookie → `GET /api/auth/me`.
   - Forgot password → email → reset with token → security stamp rotated (all sessions end) → login.
   - Logout → cookie removed, stamp unchanged (only this session).
4. **Scope:**
   - `AppUser : IdentityUser<Guid>` (+ `Status`, `Locale`, `TimeZone`, `CreatedAt`); `AddIdentityCore<AppUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<ClassHubDbContext>().AddSignInManager().AddDefaultTokenProviders()`.
   - Identity options: password policy + common-password validator, lockout, `RequireUniqueEmail`.
   - Cookie authentication configuration (B.5); API 401/403 events; `SecurityStampValidatorOptions.ValidationInterval = 1 min`; status check in `OnValidatePrincipal`.
   - Data Protection: `PersistKeysToDbContext` (+ DPAPI protection locally), application name `ClassHub`.
   - Endpoints `/api/auth`: `register`, `confirm-email`, `resend-confirmation`, `login`, `logout`, `me`, `forgot-password`, `reset-password`, `antiforgery`; generic responses to avoid enumeration.
   - Outbox table + `BackgroundService` dispatcher + `IEmailSender` (dev mailbox) + localized templates (resx mk/en) for confirmation and reset.
   - Rate limits on all auth endpoints; antiforgery validation on authenticated unsafe endpoints.
   - Account events emitted through an audit interface (persisted from R0-P5).
5. **Exclusions:** workspace provisioning (P5), UI (P7), MFA (R1-P9), external logins, student invitations (R2-P3), Resend.
6. **Dependencies:** R0-P3.
7. **Business rules:** email unique per account; suspended/inactive accounts cannot authenticate and lose sessions within the validation interval; unconfirmed accounts can log in but cannot publish a site or receive leads (D-10b, enforced from R1).
8. **Acceptance criteria:**
   - Register, confirm, log in and log out work.
   - A duplicate email is rejected without revealing existence in reset/resend flows.
   - Expired or reused tokens are rejected.
   - Lockout after repeated failures.
   - A suspended user is rejected and an existing cookie stops working within 1 minute.
   - Cookie flags correct.
9. **Testing:**
   - Unit: password validator, email normalization.
   - Integration: all endpoints, token reuse/expiry, lockout, rate limits, stamp rotation, cookie attributes, antiforgery required, generic responses, no plaintext secrets stored (DB assertions on the Identity tables), outbox → dev mailbox.
10. **Security:** Identity hashing; tokens via Data Protection; no enumeration; session fixation prevented (new cookie on sign-in); auth events logged without secrets.
11. **DoD:** common DoD.

### R0-P5 — Workspaces, Roles & Tenant Isolation

1. **Objective:** every teacher gets an isolated workspace, and the isolation machinery exists for all later modules.
2. **Business functionality (M01, M15 roles):** one workspace per teacher; no access to other workspaces; Platform Owner/Admin roles; audited critical changes.
3. **User flows:** register → workspace + Owner membership created in the same transaction → `GET /api/me/workspaces` → `GET/PATCH /api/w/{workspaceId}` (name, timezone, currency, locale).
4. **Scope:**
   - Entities `Workspace`, `WorkspaceMember` (role `Owner`; enum ready for academy roles), `AuditLogEntry`; Identity roles `PlatformOwner`, `PlatformAdmin` seeded by migration.
   - Registration extended in a single transaction.
   - `WorkspacePermissionRequirement`/handler, `ws:<permission>` policy provider, role → permission map, `ITenantContext`.
   - `ITenantOwned` + **named query filter convention** (`"Tenant"`, `"Archived"`), `TenantSaveChangesInterceptor`, composite-FK helper with a reference implementation (`WorkspaceMember`).
   - **Bypass source-scan test** (C.7 layer 6).
   - **Tenant isolation kit** (`TwoTenantsFixture`, `AssertCrossTenantDenied(...)`).
   - Audit service + append-only `AuditLog` (no update/delete code paths).
   - CLI: `dotnet run --project src/ClassHub.Api -- grant-platform-owner <email>` (local only, audited, refuses if an owner exists unless `--force-add`).
   - Workspace settings endpoint.
5. **Exclusions:** admin endpoints/UI (R1-P9), plans (R1-P10), students (R2), RLS.
6. **Dependencies:** R0-P4.
7. **Business rules:** a teacher never accesses another workspace; platform roles grant no workspace data; only the Platform Owner grants admin roles (the CLI bootstraps the first owner).
8. **Acceptance criteria:**
   - Every registered teacher has exactly one workspace with Owner membership; a failure rolls back the user too.
   - Teacher B gets 404 on teacher A's workspace endpoints.
   - A platform admin without membership gets 404.
   - A tenant-owned query without a tenant context returns nothing.
   - The interceptor rejects a cross-tenant write.
   - The composite FK rejects a cross-workspace reference at DB level.
   - Audit entries exist for role grants and settings changes.
9. **Testing:** unit (permission map, handler decisions, interceptor rules); integration (provisioning atomicity, isolation kit, fail-closed filter, DB-level composite FK, audit); architecture (bypass scan, metadata).
10. **Security:** tenant only from verified membership; 404 for cross-tenant; audit append-only; `docs/SECURITY.md` tenancy section.
11. **DoD:** common DoD.

### R0-P6 — Web Foundation & Localization

1. **Objective:** a bilingual, responsive SPA shell connected to the API.
2. **Business functionality (M17 foundation, M16 responsive):** mk/en interface, language choice, localized dates/times, responsive navigation.
3. **User flows:** visitor opens `/login` → switches language → layouts render on phone and desktop.
4. **Scope:**
   - React Router data router; layouts for auth, teacher (`/app`), student (`/student`), admin (`/admin`) as empty shells.
   - Tailwind v4 + shadcn/ui theme tokens; accessible base components.
   - i18next with `mk` default and `en`, language switcher (cookie; account preference after login), `scripts/check-i18n` parity check.
   - TanStack Query provider; `openapi-fetch` client with credentials and XSRF header; ProblemDetails `code` → translated message mapping; form error mapping.
   - `Intl` formatting helpers.
   - Vite: dev proxy `/api` → 5080; production build to `src/ClassHub.Api/wwwroot/dist` with manifest; multi-entry config prepared for islands (empty).
   - ASP.NET: static files + SPA fallback for SPA prefixes (C.5); 404/500 pages.
5. **Exclusions:** auth screens (P7), business screens, Razor public pages (R1-P4), PWA (R3-P7).
6. **Dependencies:** R0-P3 (OpenAPI types, error codes).
7. **Business rules:** UI language independent of teaching-content language; all critical system messages translated.
8. **Acceptance criteria:** shells render in mk and en; parity check passes; usable at 360 px; the SPA served by ASP.NET from `wwwroot/dist` loads correctly on deep links (`/app/anything`).
9. **Testing:** Vitest (formatting helpers, error mapping, layout navigation); integration test for SPA fallback routing vs `/api` 404s.
10. **Security:** no secrets in the bundle; CSP compatible with the build (no inline scripts); `credentials` only to the same origin.
11. **DoD:** common DoD.

### R0-P7 — Auth UI & First End-to-End Flow

1. **Objective:** the whole Release 0 journey works in a browser.
2. **Business functionality (M01):** registration, confirmation, login, logout, reset, access to own workspace.
3. **User flows:** register → open dev mailbox → confirm → login → teacher dashboard shows the workspace name → logout; forgot password → reset → login; a second teacher opening the first teacher's workspace URL sees "not found".
4. **Scope:**
   - Screens with RHF + Zod; route guards (UX only; the API enforces access); session-aware header; account language preference.
   - `/dev/mailbox` page (Development and `E2E` environments only).
   - Playwright config: global setup starts the SQL container script, builds the SPA, starts the API in `E2E` (migrates the empty DB); projects Desktop Chrome + Pixel/iPhone emulation.
   - CI `e2e` job (artifacts: report and traces on failure).
5. **Exclusions:** onboarding checklist/profile (R1-P1), admin UI.
6. **Dependencies:** R0-P5, R0-P6.
7. **Business rules:** as R0-P4; unconfirmed users see a banner and cannot use publish features (later phases).
8. **Acceptance criteria:** Playwright passes the flows above in mk and en on desktop and mobile viewports; the `e2e` job is green and required.
9. **Testing:** Playwright critical flows; Vitest for form components.
10. **Security:** dev mailbox unreachable in other environments (integration test); tokens not logged; correct `autocomplete` attributes.
11. **DoD:** common DoD.

### R0-P8 — Foundation Hardening & Release 0 Closeout

1. **Objective:** confirm Release 0 is a safe base.
2. **Business functionality:** none new.
3. **User flows:** n/a.
4. **Scope:**
   - Whole-repository Codex security review (custom prompt with the AGENTS.md checklist) and fixes.
   - Complete `docs/ARCHITECTURE.md`, `DEVELOPMENT.md`, `DATABASE.md`, `TESTING.md`, `SECURITY.md` (threat model: assets, actors, trust boundaries).
   - Verify `PROGRESS.md`/`RECOVERY.md`, the ruleset, secret scanning and Dependabot.
   - Fresh-clone setup test following `DEVELOPMENT.md` only.
5. **Exclusions:** new features.
6. **Dependencies:** R0-P7.
7. **Business rules:** n/a.
8. **Acceptance criteria:** Release 0 DoD demonstrated; fresh clone → running app following the docs; no open critical/high findings; dependency audits clean at high/critical.
9. **Testing:** full CI; recorded fresh-setup run.
10. **Security:** threat model written; ruleset blocks direct pushes (verified with a dry-run push to a protected ref being rejected).
11. **DoD:** common DoD; Release 0 marked complete in `PROGRESS.md`.

---

# F. Release 1–3 Plans

Format per phase: **Objective · Flow · Scope · Exclusions · Rules · Acceptance · Tests · Security**. The common DoD (E) always applies. Test layers:
- **Unit:** xUnit, no DB.
- **Integration:** xUnit + WebApplicationFactory + Testcontainers.
- **Arch:** architecture tests.
- **Web:** Vitest.
- **E2E:** Playwright.

## Release 1 — Teacher Presence

**Release DoD:** a teacher can publish an indexable public website, present services, offer a diagnostic test and receive inquiries; the Platform Owner can manage users, plans and public content.

### R1-P1 — Teacher Profile & Onboarding — `feature/r1-p1-teacher-profile`
- **Objective:** the teacher presents themselves professionally.
- **Flow:** first login → onboarding checklist (Complete profile → Choose subjects → Create first offer → Prepare public page → Publish) → profile form → photo upload → steps update automatically.
- **Scope:**
  - Profile fields: first/last name, photo, short bio, subjects, teaching languages, education levels, mode online/in-person/hybrid, country, timezone, currency.
  - `Catalog` module with seeded subjects/categories/levels (mk/en names).
  - `IFileStorage` + `LocalDiskFileStorage`; image upload re-encoded with SkiaSharp (size/dimension limits).
  - Onboarding state derived from data, not ticked manually.
- **Exclusions:** public rendering (P4), catalog admin (P11), materials (R2-P8).
- **Rules:** a teacher edits only their own profile; currency/timezone changes audited.
- **Acceptance:** teacher completes the profile, uploads a photo, and sees checklist progress.
- **Tests:**
  - Unit: validators.
  - Integration: profile CRUD, photo type/size/magic-byte rejection, isolation.
  - E2E: complete profile on mobile.
- **Security:** EXIF stripped; photo served through a controlled endpoint (public only once the site is published).

### R1-P2 — Offers & Programs — `feature/r1-p2-offers`
- **Flow:** Offers → create (single lesson / package / group program) → subject, duration, price, currency, mode, seats, description → activate.
- **Scope:** offers CRUD, active flag, display order, "show on public site".
- **Exclusions:** package assignment/consumption (R2-P10), group enrollment (R2).
- **Rules:**
  - Price ≥ 0 (DB check constraint too).
  - An inactive offer cannot receive new requests.
  - Group capacity > 0; package lesson count ≥ 1.
  - Price changes are never retroactive (snapshots in R2-P10).
- **Tests:** unit (rules); integration (CRUD, isolation, DB check constraint).

### R1-P3 — Public Site Model & Editor — `feature/r1-p3-site-editor`
- **Flow:** Website → choose template → edit section texts/photos → colors → show/hide → preview.
- **Scope:**
  - One site per workspace; unique slug (format, reserved list, change handling).
  - 2–3 templates.
  - Sections: Hero, About Me, Subjects, Teaching Experience, Offers & Pricing (from offers), Testimonials (teacher-entered, D-17), FAQ, Contact.
  - Curated accessible color palettes; site language (D-15); draft vs published content.
  - **Editor in the SPA; preview = Razor page in an iframe with a signed, short-lived preview token** (minimal Razor preview page; full templates in P4).
- **Exclusions:** publishing and public route (P4), custom domains, free-form page builder.
- **Rules:** unique slug; only own site; no student data source exists in the editor.
- **Tests:**
  - Unit: slug rules, section schema.
  - Integration: slug uniqueness under concurrent claims, preview token expiry/scope, isolation.
  - Web: editor components.
- **Security:** structured text only (no raw HTML), Razor encoding, image rules as R1-P1, preview `noindex`.

### R1-P4 — Public Site Rendering, SEO & Islands — `feature/r1-p4-public-site`
- **Flow:** teacher publishes → `/{slug}` is live → visitor opens it on a phone → unpublish → 404.
- **Scope:**
  - Razor Pages and template partials; `PublicSiteQueries` (allow-listed projection).
  - Publish/unpublish (audited).
  - SEO: title, description, canonical, OG/Twitter, JSON-LD; `/sitemap.xml`, `/robots.txt`.
  - OutputCache with tag eviction on publish/edit.
  - Page-view counter (D-32).
  - Tailwind public CSS entry; **Vite island infrastructure + tag helper** (with a trivial island for verification).
  - Marketing landing page `/`.
- **Exclusions:** forms (P5), tests (P7).
- **Rules:** unpublished pages are not public; suspended owners' sites hidden; moderation-hidden content not rendered (hook for P11).
- **Acceptance:**
  - Published site renders complete HTML with meta and JSON-LD.
  - Unpublished → 404 with `noindex`.
  - Only published fields are exposed.
  - Works on phone and desktop.
- **Tests:**
  - Integration: HTML contains the expected meta/JSON-LD; projection allow-list snapshot; sitemap content; cache eviction.
  - E2E: publish → anonymous mobile visit.
- **Security:** strict CSP for public pages; no authenticated data in cached responses (cache varies by nothing user-specific; previews never cached).

### R1-P5 — Leads Management — `feature/r1-p5-leads`
- **Flow:** visitor fills in the contact or "request a lesson" island (optional offer/subject) → confirmation → teacher gets an email and an inbox item → status New → Contacted → Converted/Closed, notes, search/filter, archive.
- **Scope:**
  - `POST /api/public/sites/{slug}/leads` (anonymous, rate-limited).
  - Lead types `message` / `lesson_request` (no time slot until R2-P6, D-13).
  - Consent and privacy notice text (versioned).
  - Spam protection: honeypot, minimum fill time, IP/site rate limits, length limits (D-19).
  - Inbox, statuses, notes, filters; teacher email through the outbox.
- **Exclusions:** conversion to student (R2-P1, D-12), booking slots (R2-P6).
- **Rules:** a lead belongs to one workspace; inactive offers are rejected; close/archive allowed; unconfirmed-email teachers do not receive public leads (D-10b, form hidden).
- **Tests:**
  - Unit: status transitions.
  - Integration: validation, spam rules, rate limit, isolation, inactive offer.
  - E2E: site → contact → teacher inbox.
- **Security:** lead data never echoed publicly; teacher email contains minimal data; minors notice (D-18).

### R1-P6 — Diagnostic Test Builder — `feature/r1-p6-diagnostic-builder`
- **Flow:** Tests → create (title, subject, description, difficulty) → MCQ questions → correct option(s) → points → publish/deactivate.
- **Scope:** tests, questions, options, ordering; publish validation; editing rules once attempts exist (D-38); Free-plan count limit hook (enforced in R1-P10).
- **Rules:** unpublished tests are not public; at least one correct option; points ≥ 0.
- **Tests:** unit (validation, scoring function); integration (CRUD, isolation, publish validation).
- **Security:** correct answers never appear in any public projection (arch test on public DTOs).

### R1-P7 — Public Test Taking & Results — `feature/r1-p7-diagnostic-public`
- **Flow:** visitor opens `/{slug}/tests/{testSlug}` (Razor, indexable intro) → island runs the test → submit → score → optional contact + consent → lead (`diagnostic`) linked to the attempt → request a lesson.
- **Scope:** public questions endpoint (no answers); attempt submission with **server-side scoring**; result view; teacher view (attempt counts, shared results per consent D-18, linked leads); abuse limits.
- **Rules:** score only from defined correct answers; transparent data collection; anonymous attempts store no personal data.
- **Tests:**
  - Unit: scoring edge cases.
  - Integration: answers never leaked, tampered submissions rejected, rate limits, isolation.
  - E2E: mobile test → contact → teacher sees the lead.
- **Security:** unguessable attempt IDs; consent stored with timestamp and text version.

### R1-P8 — Basic Teacher Dashboard — `feature/r1-p8-teacher-dashboard`
- **Scope:** new leads, leads by status, test attempts, page visits per period, active offers, onboarding progress, conversion funnel (full student conversion after R2-P1).
- **Rules:** own metrics only.
- **Tests:** integration (counts vs fixtures, isolation).

### R1-P9 — Platform Admin Foundation — `feature/r1-p9-admin-foundation`
- **Flow:** owner logs in → enrolls the authenticator (TOTP) → admin console → overview (users, active teachers, new registrations) → search user → suspend/reactivate (reason required) → audit viewer; owner grants/revokes Platform Admin (step-up MFA).
- **Scope:** Identity authenticator setup/verify, recovery codes, `amr=mfa` policies; `/api/admin/*` (metadata and aggregates only); suspension (stamp rotation, site hidden); admin role management (owner only); audit viewer with filters; admin SPA area; shorter admin cookie lifetime.
- **Exclusions:** plans (P10), moderation/catalog (P11), impersonation, private content access.
- **Rules:** only the owner assigns critical roles; admin actions traceable; no private student materials; MFA step-up for critical operations.
- **Tests:**
  - Integration: MFA required, step-up, owner vs admin rights, suspension effects, audit, admin denied on workspace routes.
  - E2E: admin TOTP login (test secret) → suspend user.
- **Security:** authenticator keys protected by Identity/Data Protection; MFA attempts rate-limited.

### R1-P10 — SaaS Plans & Entitlements — `feature/r1-p10-plans-entitlements`
- **Flow:** new teacher on Free → hits a limit → sees an upgrade message → owner/admin activates Pro for N months (records the payment, D-30) → limits rise → on expiry, back to Free limits without data loss.
- **Scope:** `Plan`, `PlanLimit` (active students, published tests, activities, storage MB, analytics level), `WorkspaceSubscription` (+ history); `IEntitlementService`; manual activation/extension (audited); expiry `BackgroundService` job; **provisional limit values in seed/config** (D-29).
- **Exclusions:** automatic billing, Academy plan.
- **Rules:** every workspace has an active plan; limits applied consistently; expiry never deletes data; over-limit = read/use existing, block new creation (D-29b).
- **Tests:** unit (entitlements, periods); integration (**parallel creation near a limit cannot exceed it**, expiry job, audit, teacher cannot self-upgrade).
- **Security:** plan definitions owner-only; activation/extension admin or owner (D-39).

### R1-P11 — Moderation & Reference Data Admin — `feature/r1-p11-moderation-catalog`
- **Flow:** visitor clicks "Report" (island) → reason → admin queue → hide site/section/test with reason → teacher notified → resolve.
- **Scope:** report endpoint (rate-limited, honeypot); queue; hide/unhide honored by public queries and the cache; catalog admin CRUD (deactivate, never delete).
- **Tests:** integration (report limits, hidden content not rendered, cache eviction, audit).
- **Security:** reporter identity never shown to the teacher.

### R1-P12 — Release 1 Hardening — `chore/r1-p12-hardening`
- E2E: register → profile → offer → publish → visitor lead; test → lead; admin suspends → site hidden.
- Isolation sweep over all R1 endpoints; SEO checks (HTML validity, meta, sitemap, robots, Lighthouse SEO on a sample site).
- Codex release-level review; mobile pass; docs; `PROGRESS.md` marks Release 1 complete.

## Release 2 — Teaching Operations

**Release DoD:** a teacher can manage a student from first inquiry through scheduled and completed lessons, assignments and recorded payments; students see only their own information.

### R2-P1 — Students — `feature/r2-p1-students`
- **Flow:** add a student manually or convert a lead → dedupe check by email within the workspace → link or create → profile (contacts, subjects, private notes, active/archived).
- **Scope:** students CRUD, archive/restore, private notes, guardian contact fields (D-18), idempotent lead conversion, Free active-student limit.
- **Rules:** a student can exist without a login; archiving keeps history; no duplicates on conversion; own students only.
- **Tests:** unit (dedupe); integration (double-submit conversion concurrency, limit, isolation, notes never in student-facing DTOs).
- **Security:** student data treated as sensitive in logging policies.

### R2-P2 — Groups — `feature/r2-p2-groups`
- **Scope:** groups (name, subject), members (many-to-many), archive; composite tenant FKs.
- **Rules:** archiving and removing members keep the lesson history.
- **Tests:** integration (a cross-tenant student cannot be added, rejected by the DB).

### R2-P3 — Student Accounts & Invitations — `feature/r2-p3-student-accounts`
- **Flow:** Invite student → email → student sets a password or links an existing account → student portal shell.
- **Scope:** invitation tokens (hashed, expiring, single use); `Students.UserId` (filtered unique per workspace); one user linked to students in several workspaces (D-16); `Student.Self` policy; student settings (language, notification preferences placeholder).
- **Rules:** no other students' data; the teacher never sees credentials; minors safeguards (D-18).
- **Tests:** integration (invitation flows, reuse, linking, student blocked from teacher endpoints, student A vs B); E2E (invite → accept → portal).

### R2-P4 — Lessons Core & Calendar — `feature/r2-p4-lessons-calendar`
- **Flow:** Calendar (day/week/month) → create lesson (student or group, subject, topic, time, online link or location) → overlap rejected → lesson shown.
- **Scope:**
  - `Lesson`, `LessonParticipant`.
  - Statuses Scheduled → Completed / Cancelled (D-22); cancellation reason.
  - Responsive calendar; time zones.
  - **Overlap prevention:** transaction + `sp_getapplock('schedule:<teacherUserId>')` (via `Database.ExecuteSqlInterpolatedAsync` in an allow-listed class) + overlap query.
- **Exclusions:** recurrence (P5), booking (P6), attendance (P7), packages (P10), reminders (P11).
- **Rules:** no overlapping confirmed lessons per teacher; cancelled ≠ held; only the authorized teacher can modify.
- **Tests:** unit (state machine; interval math, touching intervals allowed); integration (**parallel `HttpClient` requests creating overlapping lessons → exactly one succeeds**, DST boundary, isolation).

### R2-P5 — Recurring Lessons — `feature/r2-p5-recurring-lessons`
- **Scope:** series (weekly/bi-weekly; end by date or count); occurrences materialized for a 12-week horizon, extended by a job; edit scopes this / this-and-following / all-future (D-24); conflicts reported per occurrence.
- **Rules:** completed/cancelled occurrences are never changed by series edits.
- **Tests:** unit (expansion incl. DST, edit scopes); integration (atomic series edits, conflicts, horizon job).

### R2-P6 — Availability & Booking — `feature/r2-p6-booking`
- **Flow:** teacher sets working hours and bookable durations → visitor opens `/{slug}/book` (Razor + booking island), picks an offer and a slot, submits a request with consent → teacher confirms (lesson created after a conflict check) or declines → visitor notified; teacher can reschedule or cancel.
- **Scope:** working hours, time off, slot computation (minus confirmed lessons), booking requests, confirm/decline/reschedule/cancel, link to lead/student.
- **Rules:** pending requests do not block slots (D-23); confirmation re-checks conflicts; time zones shown explicitly; inactive offers not bookable.
- **Tests:** unit (slot computation); integration (concurrent confirmations, spam limits, isolation); E2E (mobile booking → teacher confirms).

### R2-P7 — Attendance, Notes & History — `feature/r2-p7-attendance`
- **Scope:** idempotent "complete lesson", attendance per participant (Present/Absent/Excused), notes (private + optional shared summary, D-40), history per student/group; domain event for Finance.
- **Rules:** a completed lesson is never recorded again as a new held lesson.
- **Tests:** integration (double completion under concurrency → one transition, one event).

### R2-P8 — Materials — `feature/r2-p8-materials`
- **Flow:** upload (PDF/image/document) with title, description, subject → assign to a student or group → student opens or downloads in the portal.
- **Scope:** materials, assignments, authorized download endpoint, type/size allow-list, per-plan storage quota, filters by subject.
- **Rules:** private by default (public publishing out of MVP unless approved); only assigned students; files checked and limited.
- **Tests:** integration (unassigned → 404, group assignment, quota, disallowed type, filename sanitization); E2E (upload → assign → student download).
- **Security:** malware scanning decided with hosting (D-20); never rendered inline for untrusted types.

### R2-P9 — Homework — `feature/r2-p9-homework`
- **Flow:** create homework (title, description, due date, optional lesson) → assign → student submits text/file → teacher comments → Reviewed.
- **Scope:** homework, per-student assignment statuses Assigned → Submitted → Reviewed, submissions with files, resubmission rules (D-41).
- **Tests:** unit (transitions); integration (cross-student access, late rules, file limits).

### R2-P10 — Packages & Payment Tracking — `feature/r2-p10-finance`
- **Flow:** assign a package (from an offer or custom) → price and lesson count snapshotted → lesson completed with the student Present → one lesson consumed → full or partial payment recorded → balance and remaining lessons shown → corrections via adjustment entries.
- **Scope:** student packages (snapshot), **append-only consumption ledger** (unique per active `(LessonId, StudentId)`), reversal entries, payments (manual, partial), outstanding balance, history, adjustments with reason, period report per currency.
- **Rules:**
  - A lesson never decreases a package twice; corrections are traceable.
  - No negative amounts except explicit corrections.
  - Price-list changes never alter existing packages.
  - Cancelled lessons never consume automatically.
  - Payments are made outside ClassHub.
  - Consumption policy per D-22/D-25; currencies are never summed together.
- **Tests:** unit (balance math, consumption policy); integration (**concurrent completion → single consumption**, reversal, partial payments, currency separation, isolation, audit).
- **Security:** `ws:finance.manage`; ledger append-only.

### R2-P11 — Notifications — `feature/r2-p11-notifications`
- **Scope:** localized templates (invitation, new lead, lesson confirmed/cancelled/rescheduled, reminder, new homework); reminder scheduling by a `BackgroundService`; dedupe keys; cancellation removes pending reminders; rescheduling replaces them; preferences for optional types.
- **Rules:** no duplicate sends; no reminders for cancelled lessons; recipients resolved at send time.
- **Tests:** unit (dedupe keys, zone-aware times); integration (cancel → suppressed, reschedule → one new reminder, preferences, two pollers claim without double send).

### R2-P12 — Dashboards — `feature/r2-p12-dashboards`
- **Scope:** student dashboard (next lesson, materials, homework, past lessons, own results); teacher additions (upcoming lessons, active students, unpaid amounts, remaining package lessons, monthly income per currency, conversions).
- **Rules:** own data only; teacher income kept separate from ClassHub SaaS revenue.
- **Tests:** integration (numbers vs fixtures, isolation, student view).

### R2-P13 — Release 2 Hardening — `chore/r2-p13-hardening`
- E2E: lead → convert → invite → booking → confirm → complete → package decrement → payment → student sees lesson and homework.
- Concurrency and isolation sweeps; Codex release review; mobile; docs.

## Release 3 — Teaching Toolbox

**Release DoD:** a teacher can discover tools, create teaching activities, save them and reuse them.

### R3-P1 — Toolbox Catalog — `feature/r3-p1-toolbox-catalog`
- **Scope:** admin CRUD for tools (mk/en name, description, subject, category, built-in/external, official URL, active, featured, license note); teacher catalog with search/filter; external tools (GeoGebra, Desmos, PhET, MolView, others approved) open via official links in a new tab.
- **Rules:** only properly licensed tools embedded; official links only; admin can deactivate; no grading of external activities.
- **Tests:** integration (inactive hidden, admin-only edits, audit); E2E (search → open external tool).
- **Security:** HTTPS URL allow-list; `rel="noopener noreferrer"`.

### R3-P2 — Activity Library — `feature/r3-p2-activity-library`
- **Scope:** activities (title, subject, tool type, JSON document with schema version), list/search, open, edit, duplicate, soft delete, plan limits.
- **Tests:** integration (isolation, limit concurrency, document size and schema validation).

### R3-P3 — Drawing Board — `feature/r3-p3-drawing-board`
- **Scope:** Excalidraw in a lazy-loaded SPA route; drawing, text, shapes, arrows, diagrams; save/reopen scene JSON; self-hosted fonts/assets; embedded images disabled (D-42).
- **Tests:** Web (serialization round-trip); E2E (draw → save → reopen, desktop + tablet).
- **Security:** server-side JSON size/schema limits; no external asset loading (CSP).

### R3-P4 — Geometry Board: Constructions — `feature/r3-p4-geometry-constructions`
- **Scope:** JSXGraph board; points, lines, segments, angles, triangles, circles, coordinate system; own versioned construction format (D-33); undo; save/reopen.
- **Tests:** Web (format ↔ board mapping); E2E (triangle → save → reopen).

### R3-P5 — Geometry Board: Functions & Graphs — `feature/r3-p5-geometry-functions`
- **Scope:** function input with a **safe expression parser** (no `eval`), plotting, multiple functions, persistence.
- **Tests:** Web (parser incl. malicious input); E2E (plot → save → reopen).

### R3-P6 — Usage Analytics — `feature/r3-p6-usage-analytics`
- **Scope:** aggregated counters (tool opens, activities created/reused) per workspace and platform-wide; no per-student tracking.
- **Tests:** integration (aggregation, isolation, admin aggregates only).

### R3-P7 — PWA & Mobile Improvements — `feature/r3-p7-pwa`
- **Scope:** vite-plugin-pwa limited to SPA scopes (`/app`, `/student`); manifest (mk/en), static-asset caching only; offline fallback page stating that data may be outdated; install prompt; full mobile audit; "best on tablet/desktop" notice for complex tools. Public Razor pages are not cached by the service worker.
- **Rules:** offline never shows stale data as current; no native apps.
- **Tests:** E2E (offline fallback, manifest validity); optional Lighthouse PWA check.

### R3-P8 — Release 3 Hardening — `chore/r3-p8-hardening`
- E2E toolbox flows; Codex release review; docs; Release 3 complete in `PROGRESS.md`.

---

# G. MVP Beta, V1 and V2

## MVP Beta — testing with real teachers

| Phase | Scope | Considerations |
|---|---|---|
| B-P1 Regression & E2E | Complete critical-flow suite across roles/devices; add WebKit/Firefox projects | Keep the suite small and stable |
| B-P2 Security review | OWASP ASVS L2 checklist, full authorization matrix (every endpoint × role), dependency/secrets review, upload review, least-privilege runtime DB identity, optional external pentest | Decide on SQL Server RLS defense in depth |
| B-P3 Tenant isolation testing | Automated cross-tenant ID fuzzing on all endpoints; DB FK audit | 100% endpoint coverage |
| B-P4 Performance | Indexes, query plans, N+1, pagination caps, OutputCache tuning, Lighthouse on public pages | Express limits |
| B-P5 Mobile & UX | Real Android/iOS browsers; pilot feedback fixes | Scope changes need approval |
| B-P6 Release readiness | **Blockers J.4:** hosting/domain, production identity, backups, monitoring, Resend domain, legal texts, data export/deletion | Must be decided before this phase |
| B-P7 Pilot & fixes | Pilot teachers; `fix/*` branches through the same workflow | |

## V1 — Advanced Teaching (high level, not in MVP)

- **AI Teaching Assistant:** server-side provider calls, data minimization (no student personal data by default), per-plan usage limits, Macedonian language quality evaluation.
- **Learning Journey:** a progress model built on lessons, homework and results.
- **Parent Portal:** guardian links to students, consent flows for minors; fits the relationship-based access model.
- **Interactive assigned activities:** assign Activity Library items; student viewer; scoring for built-in tools only.
- **Calendar integrations:** ASP.NET Core external providers (Google/Microsoft OAuth), ICS feeds.
- **Push notifications:** Web Push via the PWA service worker.
- **Online payments:** provider availability in North Macedonia to be investigated; reuse the R2-P10 ledger.

## V2 — Academy & Courses (high level, not in MVP)

- **Academy roles:** Owner, Manager, Teacher as new workspace roles and permission mappings; per-teacher visibility rules inside a workspace.
- **Multiple teachers:** conflict checks are already per teacher.
- **Course Builder / recorded courses:** video hosting provider, storage costs.
- **Memberships, marketplace, marketing automation, referrals:** require online payments, payouts and a tax/legal review.

---

# H. Testing and CI Strategy

## H.1 Test layers

| Layer | Tool | Project / folder | Database |
|---|---|---|---|
| Backend unit | xUnit v3 + Shouldly | `tests/ClassHub.UnitTests` | none |
| Architecture | xUnit v3 + NetArchTest + source scan + endpoint metadata | `tests/ClassHub.ArchitectureTests` | none |
| Backend integration | xUnit v3 + `WebApplicationFactory<Program>` + Testcontainers.MsSql + Respawn | `tests/ClassHub.IntegrationTests` | disposable SQL Server 2022 container |
| Frontend unit/component | Vitest + Testing Library + jsdom | `web/src/**/*.test.ts(x)` | none |
| E2E | Playwright (Chromium desktop + mobile emulation; more browsers in Beta) | `web/e2e` | disposable SQL container (script) |
| Static | `dotnet build -warnaserror` (analyzers), oxlint, `tsc -b` | — | — |

**What each layer covers:**
- **Unit:** business rules, calculations (scoring, balances, slots, recurrence), validators, permission decisions, consumption policy, overlap math, state machines.
- **Integration:** EF/SQL Server behavior (constraints, filtered indexes, collation, RCSI, rowversion), endpoints, Identity flows, authorization, tenant isolation kit, transactions, **concurrency** (parallel `HttpClient` requests: overlapping lessons, double consumption, capacity, plan limits).
- **E2E (critical flows only):** registration/login; onboarding; publishing a site; sending a lead; taking a diagnostic test; creating a student; booking a lesson; completing a lesson with consumption; student viewing homework; admin suspension with MFA.

## H.2 GitHub Actions workflows

All workflows: `permissions: contents: read` (more only where required), actions **pinned by commit SHA**, concurrency groups that cancel superseded PR runs, NuGet/npm caches.

| Job (required check) | Steps |
|---|---|
| `backend` | `actions/setup-dotnet` (global.json) → `dotnet restore --locked-mode` → `dotnet build -c Release -warnaserror` → unit tests → architecture tests |
| `integration` | build → integration tests (Testcontainers on the runner's Docker; SQL image pinned to a CU tag) → upload test results |
| `migrations` | `dotnet tool restore` → `dotnet ef migrations has-pending-model-changes` → idempotent script → start empty SQL Server 2022 container (generated, masked `sa` password) → `database update` → re-apply idempotent script (no-op) → upload script |
| `frontend` | `npm ci` → oxlint → i18n parity → Vitest → `tsc -b` + `vite build` → **OpenAPI drift**: generate the OpenAPI document from the backend build, regenerate `schema.d.ts`, `git diff --exit-code` |
| `e2e` | build SPA into `wwwroot/dist` → start SQL container → run API (`E2E` env) → Playwright → upload report/traces on failure |
| `security` | gitleaks (pinned version, checksum verified, full history) → `dotnet list package --vulnerable --include-transitive` (fail on high/critical) → `npm audit --omit=dev --audit-level=high` → dependency-review action (PRs) |
| `codeql` | CodeQL for `csharp` and `javascript-typescript` (PRs + weekly) |
| `pr-gate` | On `pull_request` (opened/edited/synchronize): PR body must contain the Codex section (I.4) with `Codex review: completed`, a reviewed commit SHA belonging to the PR, and `Unresolved critical/high: 0`; branch name must match `feature/|fix/|chore/` |

Also `dependabot.yml`: weekly grouped updates for `nuget`, `npm`, `github-actions`. Dependabot PRs follow the same gates (Codex review is recorded by the autonomous workflow when the PR is picked up).

---

# I. GitHub and Codex Workflow (autonomous)

## I.1 One-time repository configuration

- **R0-P0:** public repo `Andrej994x/classhub`; squash merge only; auto-merge allowed; delete head branches; secret scanning + push protection; Dependabot alerts/updates; private vulnerability reporting.
- **R0-P1** (after the first CI run reports the check names, and **before** the first auto-merge): a **ruleset on `main`** created via `gh api repos/Andrej994x/classhub/rulesets`:
  - pull request required (0 approvals: there is a single owner; the gates replace human approval, as authorized);
  - required status checks: `backend`, `integration`, `migrations`, `frontend`, `security`, `codeql`, `pr-gate` (and `e2e` from R0-P7);
  - require branches to be up to date; linear history; block force pushes and deletion; **no bypass actors**.

## I.2 Per-phase autonomous loop

```powershell
# 1. Start from fresh main
git switch main; git pull --ff-only
git switch -c feature/r1-p2-offers
# record in docs/PROGRESS.md: phase, branch, step "1-branch", short phase plan (scope, exclusions, tests)

# 2. Implement only the phase scope, with tests

# 3. Local verification
dotnet build ClassHub.slnx -c Release -warnaserror
dotnet test ClassHub.slnx -c Release            # unit + architecture + integration (Docker running)
cd web; npm ci; npm run lint; npm run i18n:check; npm test; npm run build; cd ..
cd web; npx playwright test; cd ..              # when the phase touches a critical flow

# 4. Commit (Conventional Commits)
git add -A; git commit -m "feat(offers): offers and programs (R1-P2)"

# 5. Independent Codex review against main
git fetch origin
codex review --base origin/main "Review guidelines in AGENTS.md. Phase R1-P2: focus on business rules, tenant isolation, authorization, transactions, concurrency, tests."
#   raw output saved to .codex-review/<phase>-<n>.md (git-ignored)

# 6. Fix findings, re-run step 3; re-run Codex after significant changes (any change to auth, tenancy, finance,
#    concurrency, migrations, or > ~30% of the diff)

# 7. Push and open the PR with the Codex section (I.4)
git push -u origin feature/r1-p2-offers
gh pr create --base main --title "feat(offers): offers & programs (R1-P2)" --body-file .codex-review/pr-body.md

# 8. Enable automatic squash merge (merges only when every required check passes)
gh pr merge --squash --auto --delete-branch

# 9. If a check fails: fix on the branch, push (repeat steps 3, 5-6 as needed); max 3 fix cycles per gate
# 10. After merge: git switch main; git pull --ff-only; record merge SHA in docs/PROGRESS.md (first commit of next phase)
```

## I.3 Stop conditions (the loop pauses and reports to the user)

- A required gate still fails after 3 fix cycles, or a failure looks environmental (runner, registry, GitHub outage).
- Codex reports a critical/high finding that needs a business decision, or a finding is rejected but touches security/tenancy (the rejection is explained and the user is asked).
- Codex cannot run (auth, quota, network) or Docker is unavailable. **Gates are never skipped**, and a review is never reported as passed without real output.
- A blocking decision is reached (J.4), or the business requirements are ambiguous for the phase.
- An action outside the approved envelope is needed: any SQL Server configuration change; a destructive operation on the local `ClassHubDb` (dropping data, resetting the DB); new accounts, secrets or paid services; GitHub settings beyond I.1; installing system software.

**Never:** commit directly to `main` (except the R0-P0 bootstrap), force-push `main`, use `--admin` or bypass rules, disable or weaken a gate or test guard to get a merge, or merge with unresolved critical/high findings.

## I.4 PR body template (also `.github/pull_request_template.md`)

```
## Phase
R1-P2 — Offers & Programs · branch feature/r1-p2-offers

## Scope / Out of scope
...

## Business rules covered
...

## Tests run
| Check | Result |
| dotnet build -warnaserror | pass |
| unit / architecture / integration | pass (N tests) |
| web lint / i18n / vitest / build | pass |
| playwright | pass / n.a. |

## Migrations
...

## Codex review
<!-- codex-review -->
Codex review: completed
Reviewed commit: <sha>
| # | Severity | Finding | Resolution |
Unresolved critical/high: 0

## Security considerations / Remaining risks
...
```

---

# J. Decisions, Risks and Blockers

## J.1 Confirmed decisions (user)

- **Stack:**
  - Backend: ASP.NET Core Web API on .NET 10 LTS (C#), modular monolith, REST.
  - Auth: ASP.NET Core Identity, cookie sessions, policy-based authorization.
  - Data: SQL Server 2022 Express, EF Core, Microsoft.EntityFrameworkCore.SqlServer, EF Core migrations.
  - Frontend: React + TypeScript + Vite + React Router; Tailwind + shadcn/ui; React Hook Form + Zod; TanStack Query.
  - Tests and review: xUnit, Vitest, Playwright, GitHub Actions, Codex CLI.
- **Local:** `C:\dev\ClassHub`, the local `SQLEXPRESS` instance (`<machine>\SQLEXPRESS`), `ClassHubDb`, Windows Authentication; no Mixed Mode or SQL logins without a demonstrated need and approval.
- **GitHub:** public `Andrej994x/classhub`; branch and PR per phase; **Implement → Tests → Codex Review → Fixes → GitHub Actions → automatic squash merge → next phase**; auto-merge only after all gates pass.
- **This revision's choices:**
  - D-43: Razor Pages public sites + React islands.
  - D-44: Testcontainers for integration tests.
  - D-45: autonomous execution.
  - Recommended defaults below accepted; only J.4 items block.
- **Scope:** all business functionality, releases and phase IDs preserved; V1/V2 not implemented during MVP.

## J.2 Decision register (defaults are approved unless listed in J.4)

| ID | Decision | Resolution |
|---|---|---|
| D-01 | Authentication | **Confirmed:** ASP.NET Core Identity, custom account endpoints, cookies |
| D-02 | Local DB connection | **Confirmed:** Windows auth over Shared Memory (`.\SQLEXPRESS`); no Mixed Mode/SQL logins/TCP change |
| D-03 | Node.js | Use installed 22.17 (meets Vite 8); move to Node 24 LTS before Beta |
| D-04 | TypeScript | Pin ~6.0; re-evaluate 7.x later |
| D-05 | Frontend package manager | npm with `package-lock.json`, single package `web/` |
| D-06 | Test frameworks | **Confirmed:** xUnit v3, Vitest, Playwright |
| D-07 | Git identity | Repo-local GitHub noreply email |
| D-08 | Repository visibility/license | **Public** (confirmed); no open-source license ("All rights reserved" notice in README) |
| D-09 | Bootstrap commit | One initial commit on `main` in R0-P0; everything else through PRs |
| D-10 | Email in development | Dev mailbox; Resend when an account/domain exists |
| D-10b | Unconfirmed email | Can log in and set up; cannot publish site or receive public leads until confirmed |
| D-11 | M14 placement | Plans & entitlements in R1-P10 |
| D-12 | Lead → student conversion | In R2-P1 |
| D-13 | "Request booking" in R1 | Lead type `lesson_request`; slot booking in R2-P6 |
| D-14 | Public URLs | `/{slug}`, `/{slug}/tests/{testSlug}`, `/{slug}/book`; reserved list; no custom domains in MVP; production domain with hosting |
| D-15 | Public site language | One language per site (mk or en) |
| D-16 | One student, several teachers | Allowed; portal groups data per teacher |
| D-17 | Testimonials | Teacher-entered, shown as provided by the teacher |
| D-18 | Minors & consent | Provisional notice + configurable age threshold + guardian contact fields; **final legal wording blocks Beta** |
| D-19 | Spam protection | Honeypot + timing + rate limits; CAPTCHA with hosting |
| D-20 | Upload malware scanning | Type/size/magic-byte checks; scanning decided with hosting |
| D-21 | Currency | Workspace default currency; offers may differ; never summed across currencies |
| D-22 | Lesson statuses & consumption | Scheduled/Completed/Cancelled; attendance Present/Absent/Excused; only Present consumes automatically; teacher may record a manual consumption adjustment (e.g., no-show) with reason |
| D-23 | Booking holds | Pending requests do not block slots; conflicts checked on confirm |
| D-24 | Recurrence edits | This / this-and-following / all-future; 12-week horizon |
| D-25 | Group lessons & packages | Consumption per Present participant with an active package; others recorded as unpaid |
| D-26 | Notification channels | Email only in MVP |
| D-27 | Data export/deletion/retention | **Blocks Beta**; designed as a phase before B-P6 |
| D-28 | Hosting, domain, production DB identity | **Blocks Beta** |
| D-29 | Free/Pro limit values | Provisional config values (Free: 10 active students, 3 published tests, 10 activities, 500 MB; Pro: 150 students, 50 tests, 500 activities, 10 GB); **final values block Beta** |
| D-29b | Over-limit after downgrade | Existing data stays usable; new creation blocked |
| D-30 | SaaS revenue | Manual payment record on Pro activation |
| D-31 | Admin access | No impersonation or private-data access in MVP |
| D-32 | Page visits | Server-side counters; no cookies; no IP storage |
| D-33 | Geometry persistence | Own versioned JSON construction format |
| D-34 | PWA | vite-plugin-pwa, SPA scope only |
| D-35 | DB collation | `Macedonian_FYROM_100_CI_AS_SC` via `UseCollation` |
| D-36 | Session lifetimes | 30-day sliding (teachers/students); 12 hours (platform admins) |
| D-37 | Tenant routing | `/api/w/{workspaceId}` |
| D-38 | Tests edited after attempts | Block destructive edits; "duplicate as new version" |
| D-39 | Subscriptions admin rights | Admin activates/extends Pro; only Owner changes plan definitions/limits |
| D-40 | Lesson notes | Private notes + optional shared summary |
| D-41 | Homework resubmission | Allowed until Reviewed; late submissions flagged |
| D-42 | Images in drawings | Disabled in MVP |
| D-43 | Public site rendering | **Confirmed:** Razor Pages + React islands |
| D-44 | Integration test DB | **Confirmed:** Testcontainers (Docker required) |
| D-45 | Execution mode | **Confirmed:** autonomous with automatic squash merge after all gates |
| D-46 | Backend project layout | `ClassHub.Api` (host) + `ClassHub.Core` (modules) + 3 test projects |
| D-47 | API style | Minimal APIs with route groups per module |
| D-48 | Logging | Serilog with redaction |
| D-49 | Image processing | SkiaSharp (MIT) |
| D-50 | Applying migrations | Explicit `dotnet ef database update` locally; automatic only in `Testing`/`E2E` containers |
| D-51 | Security stamp validation | 1-minute interval + status check |
| D-52 | Data Protection keys | Stored in the database; DPAPI-protected locally |
| D-53 | Codex gate evidence | PR body section validated by the `pr-gate` check |
| D-54 | Autonomous retry budget | Max 3 fix cycles per failing gate, then stop |

## J.3 Gaps in the business document (handled, not silently changed)

1. M14 not assigned to a release → D-11.
2. Lead conversion before Students exist → D-12.
3. Booking request in R1 → D-13.
4. Email needed before M12 → minimal outbox in R0-P4; full notifications in R2-P11.
5. Public publishing of materials undefined → out of MVP unless approved.
6. "Results allowed to be shared" (M04) → consent per D-18.
7. Data subject rights (export, deletion, retention) not specified → D-27.
8. Minors' protection without rules → D-18.
9. Beta needs hosting while hosting is postponed → D-28.
10. Free/Pro limits without numbers → D-29.
11. Number of website templates undefined → 2–3 in R1-P3.
12. Definitions of "page visits" and "conversions" → D-32 and R1-P8.

## J.4 Blockers (the autonomous loop stops before the affected phase)

| Blocker | Affects | Until resolved |
|---|---|---|
| Microsoft.Data.SqlClient cannot connect over Shared Memory (conditional) | R0-P2 | Stop; ask whether to enable TCP/IP (Windows auth still; no Mixed Mode) |
| Docker Desktop not running | Every phase from R0-P2 | Stop; ask the user to start Docker |
| Codex CLI unavailable (auth/quota/network) | Every phase | Stop; report |
| D-28 hosting, domain, production DB identity | B-P6 | Releases 0–3 continue |
| D-18 final legal wording for minors/consent | B-P6 (public launch) | Provisional texts used |
| D-29 final Free/Pro values | B-P6 | Provisional config values used |
| D-27 data export/deletion design | Before B-P6 | — |
| Resend account + sending domain | B-P6 | Dev mailbox used |

## J.5 Technical risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Tenant isolation bug | Medium | Critical | Nine layers (C.7), composite FKs, fail-closed filters, bypass scan, isolation kit, Codex focus, Beta fuzzing |
| Autonomous auto-merge lands a defect | Medium | High | Required gates incl. integration, migrations, E2E, security and `pr-gate`; Codex review; small phases; stop conditions; `git revert` recovery (RECOVERY.md) |
| Razor + SPA dual rendering (two UI technologies) | Medium | Medium | Razor only for public pages; shared Tailwind tokens; islands reuse SPA components; one preview renderer |
| Developer runs as `sysadmin` locally | Certain | Low (local) | Accepted for dev; least-privilege identity at hosting; B-P2 check |
| Docker dependency for tests | Medium | Medium | Documented prerequisite; clear guard failures; never fall back to local DB |
| Shared Memory unsupported by Microsoft.Data.SqlClient | Low | Low | Verified at R0-P2 start; TCP fallback needs only approval |
| EF query filter misuse (`IgnoreQueryFilters`, raw SQL) | Medium | High | Allow-list source scan; review guidelines |
| Race conditions (bookings, packages, limits) | Medium | High | `sp_getapplock` + unique constraints + parallel tests |
| Public repo exposes commercial code and security details | Certain | Medium | Confirmed public; secret scanning + push protection; no sensitive data; threat model assumes code is public |
| Express unsuitable for production scale | Medium (later) | Medium | Standard T-SQL only; engine chosen with hosting |
| Library license changes | Low | Medium | Avoid commercial-license packages (B.2); Dependabot PRs reviewed |
| Windows dev vs Linux CI | Medium | Low | `.gitattributes`, case-correct paths, CI is the source of truth |
| Solo-developer capacity (~45 phases) | High | Medium | Strict scope, small phases, autonomous execution |

---

# K. Recommended First Phase

## R0-P1 — Repository Foundation & CI

**Branch:** `chore/r0-p1-repository-foundation`

**Preconditions:** R0-P0 complete (repo created and configured, bootstrap commit on `main`, Docker and Codex available).

**Objective:** a clean, documented, automatically verified repository that every later phase builds on, with the auto-merge gates in place.

**Business functionality:** none directly (enabler for M01–M17).

**User flows (developer):** clone → `dotnet tool restore` → `dotnet build` → `dotnet test` → `cd web && npm ci && npm run dev` → API `http://localhost:5080/health` → open PR → CI runs → auto-merge.

**Scope:**
1. Root: `ClassHub.slnx`, `global.json` (10.0.302, `latestFeature`), `Directory.Build.props` (net10.0, nullable, implicit usings, `TreatWarningsAsErrors`, `AnalysisLevel latest`), `Directory.Packages.props` (central versions, `RestorePackagesWithLockFile`), `dotnet-tools.json` (dotnet-ef 10.0.14), `.editorconfig`, `.gitattributes`.
2. `src/ClassHub.Api`: minimal host with `GET /health` → `{ "status": "ok" }` (anonymous, explicit metadata); Serilog console bootstrap; no database.
3. `src/ClassHub.Core`: empty module skeleton (`SharedKernel`, `Infrastructure`, `Modules/`) with the `IModule` interface.
4. `tests/ClassHub.UnitTests` (one test), `tests/ClassHub.ArchitectureTests` (first rule: `Core` does not reference `Api`), `tests/ClassHub.IntegrationTests` (one `WebApplicationFactory` test for `/health`, no DB).
5. `web/`: Vite + React + TypeScript + React Router + Tailwind v4 placeholder page; Vitest + Testing Library (one component test); oxlint; `i18n` folder with mk/en and the parity script; `package-lock.json`.
6. GitHub:
   - workflows `ci.yml` (`backend`, `integration`, `frontend` jobs at their initial size), `security.yml` (gitleaks pinned + checksum, vulnerable packages, npm audit, dependency review), `codeql.yml`, `pr-gate.yml`;
   - `dependabot.yml`; PR template (I.4); actions pinned by SHA.
7. Docs:
   - `README.md` (product summary, quick start, "All rights reserved");
   - `CLAUDE.md` (autonomous workflow, scope discipline, commands, stop conditions);
   - `AGENTS.md` (shared rules + **Codex review guidelines**);
   - `docs/DEVELOPMENT.md` (prerequisites, setup, ports, commands, branching, workflow);
   - `docs/ARCHITECTURE.md` (initial, from section C);
   - `docs/ROADMAP.md` (this plan); `docs/PROGRESS.md`; `docs/RECOVERY.md` (Appendix 2).
8. Verify the Codex command (`codex review --base origin/main` with a custom prompt) and record it in `docs/DEVELOPMENT.md`.
9. After the PR's first CI run: create the **`main` ruleset** with the reported check names (I.1), then `gh pr merge --squash --auto --delete-branch`.

**Exclusions:** EF Core, database, migrations, Identity, tenancy, i18n beyond the parity setup, shadcn/ui, layouts, Playwright, Razor public pages, any business module, any deployment.

**Dependencies:** R0-P0.

**Business rules:** none. Repository rules: no secrets, no database files, no personal data.

**Acceptance criteria:**
- `dotnet restore --locked-mode`, `dotnet build -c Release -warnaserror` and `dotnet test` pass locally and in CI.
- `npm ci`, `npm run lint`, `npm run i18n:check`, `npm test` and `npm run build` pass in `web/` locally and in CI.
- `GET http://localhost:5080/health` → `{ "status": "ok" }`.
- Security workflows (gitleaks, CodeQL, dependency review, audits) run and pass.
- `pr-gate` passes with a real Codex review section, and fails on a PR body without one (verified once).
- The ruleset is active with the required checks, and the PR is **merged automatically** (squash) only after all checks pass.
- A new developer can set up from `docs/DEVELOPMENT.md` alone.
- `docs/PROGRESS.md` shows R0-P1 complete with its merge SHA (recorded at the start of R0-P2).

**Tests:** unit placeholder, architecture rule, `/health` integration test, web component test, i18n parity script.

**Security considerations:** `.gitignore` covers env files, DB files, uploads, `wwwroot/dist`, `.codex-review/`; gitleaks over full history; least-privilege workflow permissions; SHA-pinned actions; no secrets needed by CI.

**Definition of Done:** common DoD (E) + ruleset active + automatic squash merge completed + local `main` synced.

---

# Appendix 1 — Local SQL Server: no changes required

- **Verified (2026-11-04):** Windows Authentication to the local `SQLEXPRESS` instance (`<machine>\SQLEXPRESS`) works over **Shared Memory** (and Named Pipes). The account the developer's Windows domain account is `sysadmin` with `CREATE ANY DATABASE`. TCP/IP is disabled and is **not needed**.
- **Do not change:** authentication mode (stays Windows-only), protocols, SQL Browser, logins.
- **Development connection string** (`src/ClassHub.Api/appsettings.Development.json`, no secrets):
  ```json
  "ConnectionStrings": {
    "ClassHub": "Server=.\\SQLEXPRESS;Database=ClassHubDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
  }
  ```
  Override per machine with the environment variable `ConnectionStrings__ClassHub` or `dotnet user-secrets`.
- **Creating the database (R0-P2, implementation only):**
  ```powershell
  dotnet tool restore
  dotnet ef database update --project src/ClassHub.Core --startup-project src/ClassHub.Api
  ```
  EF creates `ClassHubDb` with `Macedonian_FYROM_100_CI_AS_SC`, and a migration enables `READ_COMMITTED_SNAPSHOT`. Only the new database is affected.
- **Verification query** (recorded in `docs/DATABASE.md`):
  ```sql
  SELECT DB_NAME() AS db, SUSER_SNAME() AS login, net_transport, auth_scheme
  FROM sys.dm_exec_connections WHERE session_id = @@SPID;
  ```
- **Conditional fallback (only with user approval):** if Microsoft.Data.SqlClient cannot use Shared Memory, enable TCP/IP for `SQLEXPRESS` on a static local port and connect with `Server=localhost,<port>;Integrated Security=True;…`. Mixed Mode and SQL logins are still not required.
- **Automated tests never use the local server:** integration tests use Testcontainers; E2E and CI use disposable Linux containers with generated `sa` passwords.

---

# Appendix 2 — Documentation plan

| File | Purpose | Created |
|---|---|---|
| `README.md` | Product summary, quick start, license notice, links | R0-P0 stub, R0-P1 |
| `CLAUDE.md` | Claude Code rules: autonomous loop, scope discipline, commands, stop conditions, database safety; references AGENTS.md | R0-P1 |
| `AGENTS.md` | Shared rules for all agents + **Codex review guidelines** (business rules, tenancy, authorization, data integrity, transactions, concurrency, migrations, tests, secrets) | R0-P1 |
| `docs/ARCHITECTURE.md` | Structure, modules, tenancy layers, auth, routing, SEO approach, cross-cutting concerns | R0-P1 → R0-P8 |
| `docs/DEVELOPMENT.md` | Prerequisites, setup, ports, scripts, branching, autonomous workflow, Codex commands | R0-P1 |
| `docs/DATABASE.md` | Windows-auth connection, conventions (B.3), migrations workflow, test databases | R0-P2 |
| `docs/TESTING.md` | Layers, guards, isolation kit, concurrency tests, E2E setup, CI jobs | R0-P2 → R0-P7 |
| `docs/SECURITY.md` | Threat model, Identity/cookie design, tenancy, data classification, logging rules, vulnerability reporting | R0-P5 → R0-P8 |
| `docs/ROADMAP.md` | This approved plan (kept in sync when the plan changes) | R0-P0 |
| **`docs/PROGRESS.md`** | Live status: current phase, branch, workflow step (1–10), last action, next action; phase table (status, PR link, merge SHA, Codex summary, notes); open blockers; decisions taken during execution | R0-P0, updated in every phase |
| **`docs/RECOVERY.md`** | How to resume after interruption (`PROGRESS.md` → `git status` → `gh pr list` → `gh pr checks`); failed CI; re-running Codex; reverting a merged PR (`git revert` through a `fix/*` PR, never force push); local migration rollback (`dotnet ef database update <PreviousMigration>`; dropping local data only with approval); Docker/Testcontainers and Codex outages | R0-P1 |

**Rule:** each fact lives in one file and other files link to it. Docs change in the same PR as the code they describe. `PROGRESS.md` is updated at the start (branch, plan) and end (results) of every phase. The merge SHA is recorded in the next phase's first commit.

---

# Appendix 3 — Module → release traceability

| Module | R0 | R1 | R2 | R3 | Beta |
|---|---|---|---|---|---|
| M01 Accounts & Workspace | P4, P5, P7 | P1 (profile, onboarding) | | | |
| M02 Public Website | | P3 (editor), P4 (Razor rendering, SEO, islands) | | | P4 (performance) |
| M03 Leads | | P5 | P1 (conversion) | | |
| M04 Diagnostic Tests | | P6, P7 | | | |
| M05 Offers | | P2 | | | |
| M06 Students & Groups | | | P1, P2, P3 | | |
| M07 Scheduling & Booking | | P5 (lesson request) | P4, P5, P6 | | |
| M08 Lessons | | | P4, P7 | | |
| M09 Teaching Toolbox | | | | P1–P5 | |
| M10 Materials & Homework | | | P8, P9 | | |
| M11 Packages & Payments | | | P10 | | |
| M12 Notifications | P4 (outbox, auth emails) | P5 (lead email) | P11 | | P6 (Resend) |
| M13 Dashboards | | P8 | P12 | P6 | |
| M14 SaaS Plans | | P10 | student/storage limits | activity limits | P6 (final values) |
| M15 Administration | P5 (roles, audit, CLI) | P9, P10, P11 | | P1 (toolbox admin), P6 | |
| M16 Mobile & PWA | P6 (responsive SPA) | P4 (mobile-first Razor pages), all UI phases | all UI phases | P7 (PWA, SPA scope) | P5 |
| M17 Localization | P6 (i18next, resx) | all phases (mk/en) | all phases | all phases | |
