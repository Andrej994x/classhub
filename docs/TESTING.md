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

## CI

See [DEVELOPMENT.md](DEVELOPMENT.md#required-checks-github-actions). `integration` runs these tests on the runner's Docker. `migrations` applies all migrations to an empty SQL Server 2022 container, re-runs the idempotent script, and checks collation and RCSI.
