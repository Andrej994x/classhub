# Testing

| Layer | Project / folder | Tools | Needs |
|---|---|---|---|
| Backend unit | `tests/ClassHub.UnitTests` | xUnit v3, Shouldly | nothing |
| Architecture | `tests/ClassHub.ArchitectureTests` | xUnit v3, NetArchTest | nothing |
| Backend integration | `tests/ClassHub.IntegrationTests` | xUnit v3, `WebApplicationFactory<Program>`, Testcontainers.MsSql, Respawn | **Docker** (Linux containers) |
| Frontend | `web/src/**/*.test.ts(x)` | Vitest, Testing Library, jsdom | Node |
| E2E | `web/e2e` (from R0-P7) | Playwright | Docker, Node |

## Running

```powershell
dotnet test --solution ClassHub.slnx -c Release                 # everything (Docker must be running)
dotnet test --project tests/ClassHub.UnitTests                   # no Docker
dotnet test --project tests/ClassHub.IntegrationTests            # Docker
cd web; npm test
```

Tests run on Microsoft.Testing.Platform (`global.json`). The first integration run pulls the SQL Server image (~1.7 GB). A full run takes about 40 seconds afterwards.

## Frontend tests (R0-P6)

- `src/test/render.tsx` → `renderRoute(path, language?)` renders the real route tree in a memory router with a fresh QueryClient, in Macedonian unless a language is given. Tests query by role and the visible (translated) name.
- `src/api/client.test.ts` drives the API client against a fake `fetch`: antiforgery fetch-before-unsafe, token adoption after sign-in, one retry on `ANTIFORGERY_INVALID`, ProblemDetails → `ApiError`, network errors, cookies.
- `src/lib/format.test.ts`: DST-aware time zones, mk/en dates, money with currency, translated error codes.
- Backend side: `Platform/SpaHostingTests` (integration) checks the fallback only for SPA prefixes, cache headers, and that `/api` and health checks never return the SPA.

## Integration test database safety

Integration tests **never** use the local `ClassHubDb`, `.\SQLEXPRESS`, LocalDB, or any shared server. `ClassHubApiFixture` (one per test run, `[Collection(ApiCollection.Name)]`) does, in order:

1. `DockerEndpointGuard.AssertLocalEngine`: the Docker endpoint is `npipe://`, `unix://` or loopback TCP.
2. Starts `mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04` (override: `CLASSHUB_TEST_SQL_IMAGE`, must stay an `mcr.microsoft.com/mssql/server:*` image) with a random per-run `sa` password.
3. `DockerEndpointGuard.AssertLocalContainerEndpoint`: loopback host, random port (never 1433).
4. The server reports the container id as machine name and Linux as platform.
5. `TestDatabaseGuard.AssertIsolated`: data source must be exactly the container's `host,port`; database must be `ClassHubTest_<guid>`; Integrated Security, `.`, `(local)`, named instances, pipes, shared memory, LocalDB, `AttachDbFilename`, `ClassHubDb` and system databases are refused.
6. Builds the API host in the `Testing` environment (no `appsettings.Development.json`, no user-secrets), replaces the DbContext configuration with the container connection string, checks the guard again on the DbContext's real connection string, then applies the migrations.

If Docker is unavailable, the fixture fails with "Docker is required". The tests **never** fall back to another server. `Safety/TestDatabaseGuardTests` checks the guards without Docker; `Safety/DatabaseIsolationTests` checks from inside the host that it really uses the container database.

Helpers:
- `fixture.CreateClient()`: HTTP client for the in-memory API.
- `fixture.CreateDbContext()`: a context on the test database for arrange/assert steps.
- `fixture.CreateIsolatedDatabaseConnectionString()`: a fresh `ClassHubTest_*` database in the same container (for migration or convention tests).
- `fixture.ResetDatabaseAsync()`: Respawn reset (keeps migration history and seeded technical rows).

Tests create their own data with unique values and never depend on global row counts.

## What is covered (R0-P2)

| Area | Tests |
|---|---|
| Health | `/health`, `/health/live`, `/health/ready` return 200 without details |
| Database settings | collation `Macedonian_FYROM_100_CI_AS_SC`, `READ_COMMITTED_SNAPSHOT ON`, all migrations applied, no pending model changes |
| Migrations | apply → revert to empty → re-apply on a fresh database |
| Conventions | UTC round-trip (local → UTC), Cyrillic + supplementary characters, `datetime2(3)`, many NULLs in an optional unique column, duplicates rejected, no cascading delete |
| Safety | guards refuse every non-container target; host environment is `Testing`; the DbContext's database is the container's |

## What is covered (R0-P3)

Platform tests use `Platform/PlatformApiFactory`: the real pipeline in the `Testing` environment with an unreachable connection string (no Docker; no test calls a database endpoint). Probe endpoints under `/api/_probe` (test-only `IModule`) exercise the pipeline.

| Area | Tests |
|---|---|
| ProblemDetails | expected errors keep status, `code`, safe title, `traceId`; unexpected errors are a generic 500 without exception text or stack; malformed JSON → 400 `BAD_REQUEST` |
| Validation | invalid body → 400 `VALIDATION_FAILED` with camelCase paths (`items[0].displayName`); valid body reaches the handler |
| Authorization | protected, undeclared (fallback) and unknown routes → 401 problem for anonymous callers, no redirect; **every real endpoint declares AllowAnonymous or a named policy** (`EndpointAuthorizationMetadataTests`) |
| Rate limiting | the 4th request over a limit of 3 → 429 `RATE_LIMITED` + `Retry-After`; invalid limits stop the host at startup |
| Antiforgery | unsafe request without token or with a forged token → 400 `ANTIFORGERY_INVALID`; cookie + header → accepted; safe methods pass; token cookies have the right flags |
| Security headers | API: deny-all CSP, `DENY`, `nosniff`, referrer and COOP; pages: same-origin CSP, `SAMEORIGIN` |
| Contract | `OpenApiContractTests`: committed `openapi.json` equals the API document |
| Logging (unit) | secrets redacted (top-level, nested, dictionaries), emails masked, `CancellationToken` untouched |
| Architecture | Core ↛ Api; modules use other modules only via `Contracts`; `Domain` ↛ ASP.NET/EF/Infrastructure; SharedKernel ↛ modules/infrastructure |

## What is covered (R0-P4)

`Identity/AuthFlowTests` (Testcontainers SQL Server, `ApiClient` with cookies + antiforgery header, `WaitForEmailAsync` reading the dev mailbox):

| Area | Tests |
|---|---|
| Registration | register → confirmation email (en and default mk) → confirm → sign in; existing email answers the same 202 and gets an "already registered" email; **6 parallel registrations of one email create exactly one account** (unique index); weak, common and email-based passwords rejected for new and known emails alike; invalid emails rejected |
| Sign-in | correct sign-in returns the user; wrong password and unknown email give identical 401s; 5 failures lock the account (even the right password then fails); cookie flags (HttpOnly, SameSite=Lax, path, persistent only with remember-me); antiforgery header required on auth POSTs |
| Sessions | `/me` requires a session; logout ends it (token issued at sign-in accepted); authenticated callers get 404 for unknown routes; **suspension ends existing sessions and blocks sign-in** (403 `ACCOUNT_SUSPENDED`) |
| Password reset | reset email → new password works, old password and **existing sessions stop working**, the link cannot be reused; unknown email → 202 and no email; forged reset/confirmation tokens → 400 `INVALID_TOKEN` |
| Storage | passwords stored only as Identity V3 hashes; delivered outbox rows keep no body |
| Dev mailbox | `/api/dev/mailbox` lists delivered messages in Testing |
| Unit | common-password rules, localized and HTML-encoded emails, outbox backoff, email normalization |

The Testing host validates security stamps on every request (`Identity:SecurityStampValidationIntervalSeconds=0`) and dispatches email every 100 ms.

## What is covered (R0-P5)

| Area | Tests |
|---|---|
| Provisioning | registration creates exactly one Owner workspace (localized name, MKD, Europe/Skopje) and an audit entry; a failing provisioning rolls back the account and its email |
| Settings | owner reads and updates name/time zone/currency/locale (audited with before/after); invalid IANA zones, currencies, locales and empty names are rejected |
| Isolation | another teacher gets 404 on GET and PATCH and changes nothing; a platform admin without membership gets 404; unknown and malformed ids 404; anonymous 401; tenant rows are invisible to a context without a tenant |
| Machinery (probe model) | filter shows only the current workspace and nothing without one; inserts stamped; foreign-workspace writes refused; WorkspaceId cannot change; orphan rows refused; **the database rejects cross-workspace references** (composite FK) |
| CLI | `grant-platform-owner`: unknown email → 1, first owner → 0, second owner needs `--force-add` (2 otherwise), audited, role change ends the old session, not reachable over HTTP; a failure before the audit entry grants nothing; a concurrent bootstrap waits for the lock and is refused once an owner exists |
| Migrations | `WorkspaceBackfillMigrationTests`: upgrading a populated R0-P4 database gives every existing account an owned workspace (account language and time zone), audited, once |
| Architecture | tenant-filter bypasses and raw SQL only in allow-listed files |
| Unit | role permissions, policy names, tenant context binding, unknown permissions |

`Infrastructure/TenantIsolationKit` provides `CreateTeacherAsync` (register, sign in, workspace id) and `AssertCrossTenantDeniedAsync` for every later phase.

## CI

See [DEVELOPMENT.md](DEVELOPMENT.md#required-checks-github-actions). `integration` runs these tests on the runner's Docker. `migrations` applies all migrations to an empty SQL Server 2022 container, re-runs the idempotent script, and checks collation and RCSI.
