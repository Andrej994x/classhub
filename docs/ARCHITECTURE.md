# Architecture

This document describes the architecture **as implemented**. The target design is in [ROADMAP.md](ROADMAP.md) sections B–C; this file is updated as phases land.

## Current state (R0-P3)

```
ClassHub.slnx
├─ src/ClassHub.Api      ASP.NET Core host (composition root)
│  ├─ Program.cs         pipeline + /health, /health/live, /health/ready, OpenAPI (Development/Testing), Scalar (Development)
│  └─ Platform/          ClassHubExceptionHandler, SecurityHeadersMiddleware, SensitiveDataRedactionEnricher,
│                        RateLimitingSetup (+ RateLimitingOptions), SecuritySetup (cookie auth, policies, antiforgery,
│                        forwarded headers)
├─ src/ClassHub.Core     module library
│  ├─ SharedKernel/      Errors (ErrorCodes, ClassHubException + NotFound/Conflict/BusinessRule/ForbiddenOperation),
│  │                     Security (ClassHubPolicies, RateLimitPolicies)
│  ├─ Infrastructure/    Persistence (DbContext, conventions, migrations), Web (ValidationFilter, AntiforgeryHeaderFilter)
│  └─ Modules/           ModuleRegistration (discovery, /api group); Identity (GET /api/auth/antiforgery)
├─ tests/ClassHub.UnitTests           redaction, error codes, validation paths, UTC converter
├─ tests/ClassHub.ArchitectureTests   layering, module boundaries (Contracts only), domain purity, shared kernel
├─ tests/ClassHub.IntegrationTests    Testcontainers DB tests; Platform/ (no-DB host): pipeline, endpoint metadata, OpenAPI contract
└─ web/                  React 19 + TS 6.0 + Vite 8 + React Router 8 + Tailwind v4; Vitest; oxlint; src/api (OpenAPI → types)
```

Persistence details: [DATABASE.md](DATABASE.md). Test infrastructure: [TESTING.md](TESTING.md).

## Request pipeline (order)

1. Forwarded headers (only if `ForwardedHeaders:Enabled`). Startup **fails** when it is enabled without at least one valid `KnownProxies` address, so forwarded IP/scheme can never be accepted from arbitrary peers.
2. `UseExceptionHandler` → `ClassHubExceptionHandler`: `ClassHubException` → its status, `code` and safe message; `DbUpdateConcurrencyException` → 409 `CONCURRENCY_CONFLICT`; `BadHttpRequestException` → its 4xx; anything else → 500 `INTERNAL_ERROR` with a generic title (details only in the log).
3. `UseStatusCodePages` → empty 4xx/5xx responses (401, 403, unknown routes, malformed JSON) become ProblemDetails. Every ProblemDetails has `code` (default from `ErrorCodes.ForStatus`) and `traceId`.
4. HSTS outside Development/Testing/E2E.
5. `SecurityHeadersMiddleware`: `nosniff`, `Referrer-Policy`, `Cross-Origin-Opener-Policy`, `Permissions-Policy`; CSP `default-src 'none'` + `X-Frame-Options: DENY` for `/api`, `/health`, `/openapi`; same-origin CSP + `SAMEORIGIN` for pages.
6. Serilog request logging (path without query string) with `SensitiveDataRedactionEnricher` (secrets → `***`, emails masked, nested values included).
7. Routing → authentication (cookie `.ClassHub.Auth`, Identity application scheme; 401/403 instead of redirects) → authorization → rate limiter.

## Authorization model (deny by default)

- `FallbackPolicy = RequireAuthenticatedUser`. It also applies to requests that match no endpoint, so anonymous callers get 401 for unknown `/api` routes (no route probing).
- **Every endpoint must declare** `.AllowAnonymous()` or `.RequireAuthorization(<named ClassHub policy>)`; `EndpointAuthorizationMetadataTests` fails otherwise. Policies live in `ClassHubPolicies` (more are added with workspaces in R0-P5).

## API conventions

- Modules implement `IModule` and map relative routes; `MapClassHubModules` mounts them under `/api`. Modules are discovered from `ClassHub.Core` (and the host may register more `IModule` services, which tests use for probe endpoints).
- Request validation: FluentValidation validators (auto-registered from Core) + `.WithValidation<TRequest>()` → 400 `VALIDATION_FAILED` with camelCase field paths (`items[0].displayName`).
- CSRF: cookie-authenticated unsafe endpoints use `.RequireAntiforgeryHeader()`; the SPA gets tokens from `GET /api/auth/antiforgery` (`XSRF-TOKEN` readable cookie + HttpOnly `.ClassHub.Antiforgery`) and sends `X-XSRF-TOKEN`. Tokens are bound to the user: re-issue after sign-in/sign-out.
- Rate limiting: named fixed-window policies per client IP (`auth`, `public-form`, `public-read`), limits in `RateLimiting:*` (validated on start), 429 `RATE_LIMITED` + `Retry-After`. In-memory, no Redis.
- JSON request body limit: 1 MB (Kestrel); upload endpoints set their own limits later.

## API contract → TypeScript

`OpenApiContractTests` requires `web/src/api/openapi.json` to equal the API's `/openapi/v1.json` (server URLs removed). `npm run api:types` generates `web/src/api/schema.d.ts` from it; CI fails if either is stale. Update flow after an API change:

```powershell
$env:CLASSHUB_UPDATE_OPENAPI='1'; dotnet test --project tests/ClassHub.IntegrationTests -- --filter-class "*OpenApiContractTests"; Remove-Item Env:CLASSHUB_UPDATE_OPENAPI
cd web; npm run api:types
```

## Principles (from ROADMAP)

- **Modular monolith:** `ClassHub.Api` is only the composition root. Business modules live in `ClassHub.Core/Modules/<Name>/` and implement `IModule`. A module may use another module only through its `Contracts` namespace (enforced by architecture tests from R0-P3).
- **Single origin:** `/api/*` (REST), SPA routes (`/app`, `/student`, `/admin`, auth pages) and Razor public pages (`/`, `/{slug}`, …) are all served by one ASP.NET Core app (from R0-P6 / R1-P4).
- **Tenant isolation:** each teacher's workspace is the tenant boundary; layered isolation per ROADMAP C.7 (from R0-P5).
- **Contracts:** the OpenAPI document is generated at build time and turned into TypeScript types for the SPA (from R0-P3).

## Toolchain decisions taken during implementation

| Topic | Decision | Reason |
|---|---|---|
| Test runner | Microsoft.Testing.Platform (opt-in in `global.json`); no `Microsoft.NET.Test.Sdk`/VSTest adapter | xunit.v3 4.x on the .NET 10 SDK no longer supports the VSTest target |
| Restore | Central package management + `packages.lock.json`, CI uses `--locked-mode` | Reproducible builds |
| Package source | `NuGet.config` clears sources and uses nuget.org only | No machine-specific feeds |
| Security pins | `CentralPackageTransitivePinningEnabled`; `System.Security.Cryptography.Pkcs` pinned to 10.0.14 | Microsoft.Data.SqlClient 6.1.7 brings Pkcs 9.0.11 (GHSA-846f-f764-76mx high, GHSA-2fmv-rv2c-59x7 moderate); NuGet audit warnings are build errors |
| Database collation | Applied by EF on database creation (`UseCollation`), no migration step | EF does not emit `AlterDatabase` for the initial model; verified by tests and the CI `migrations` job |
| Migrations | Excluded from style analyzers via `.editorconfig` (`generated_code = true`) | Generated code |
| Unknown routes | Anonymous → 401 (fallback policy), authenticated → 404 | Deny-by-default includes unmatched requests |
| openapi-typescript + TS 6 | npm `overrides` lets only `openapi-typescript` use the project's TypeScript 6.0 (its peer range is `^5.x`) | No release supports TS 6 yet; narrower than `--legacy-peer-deps`. Generation and type-check verified |
| Dev-only audit findings | 3 moderate advisories in `openapi-typescript` → `@redocly/openapi-core` → `minimatch` (GHSA-g3hv-75wg-m249) accepted | Build-time tool, input is our own committed JSON, not shipped; the only fix is a breaking downgrade. Production `npm audit` is clean; re-check on each Dependabot update |
| Dependabot | TypeScript and `@types/node` major updates ignored | D-04 (TS 6.0) and the Node 22 runtime |
