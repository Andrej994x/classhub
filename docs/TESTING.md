# Testing

| Layer | Project / folder | Tools | Needs |
|---|---|---|---|
| Backend unit | `tests/ClassHub.UnitTests` | xUnit v3, Shouldly | nothing |
| Architecture | `tests/ClassHub.ArchitectureTests` | xUnit v3, NetArchTest | nothing |
| Backend integration | `tests/ClassHub.IntegrationTests` | xUnit v3, `WebApplicationFactory<Program>`, Testcontainers.MsSql, Respawn | **Docker** (Linux containers) |
| Frontend | `web/src/**/*.test.ts(x)` | Vitest, Testing Library, jsdom | Node |
| E2E | `web/e2e` | Playwright (Chromium: Desktop Chrome + Pixel 7) | Docker, Node, .NET SDK |

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

## End-to-end tests (Playwright, R0-P7)

```powershell
cd web; npx playwright install chromium   # once
npm run e2e                                # builds the SPA and the API, then runs all projects
```

`e2e/global-setup.ts` starts the whole stack and returns its teardown:

1. A disposable SQL Server 2022 container (same image as the integration tests) with a generated `sa` password, published on a random **loopback** port.
2. The production SPA build (`npm run build` → `wwwroot/dist`) and a Release build of the API (skipped with `E2E_SKIP_BUILD=1`, as CI builds them in earlier steps).
3. The API (`dotnet ClassHub.Api.dll`) in the **E2E** environment on `http://127.0.0.1:5180` (`E2E_PORT`), with `App:PublicBaseUrl` pointing at itself so email links open the SPA. At startup it migrates the database, but `E2EDatabase.AssertDisposable` refuses anything except a `ClassHubE2E_*` database on a loopback TCP endpoint with SQL authentication: the local `ClassHubDb` (Windows authentication, `.\SQLEXPRESS`) can never be used.

Flows (each in **mk and en**, on **desktop and mobile**): register → dev mailbox → confirm → sign in → the dashboard shows the workspace → sign out; forgot password → reset link → the old password fails, the new one works; a second teacher opening the first teacher's workspace URL sees "not found". Texts come from the app's locale files, so wording changes do not break tests. CI job `e2e` uploads the HTML report and traces on failure.

## Platform admin & MFA tests (R1-P9)

- Integration: `MfaTests` (enrollment with a wrong and a right code, 202 + no session after the password, the second step, recovery codes once and audited, regeneration with step-up, replacing the authenticator only with a current code or a recovery code, lockout after 5 wrong codes, step-up refused during a lockout on every path and reset by a right code, second step needs the first, exactly one kind of code, keys and codes encrypted at rest, suspended account cannot finish); `PlatformAdminTests` (401/403 for anonymous/teachers, `MFA_REQUIRED` for password-only staff, staff cookie never persistent and `amr` kept across stamp validations, overview numbers, search/detail return metadata only, suspension ends sessions/blocks sign-in/hides the site/is audited with reason and actor, owner vs admin target rules, owner-only role grant/revoke with step-up, staff get 404 on workspace routes, audit filters and hidden workspace details). `PlatformStaffKit` grants roles like the CLI and computes TOTP codes (RFC 6238).
- Unit: `MfaUnitTests` (code normalization, key formatting, staff session limits).
- Vitest: `TwoFactorLogin.test.tsx` (code step, recovery code, expired sign-in), `AdminPages.test.tsx` (no access, enrollment → recovery codes → overview, sign in again, suspension with reason, owner role grant with code, admin without role management, audit filters).
- E2E: `admin.spec.ts` — a Platform Owner bootstrapped with the CLI (`grant-platform-owner` on the disposable database) enrolls the authenticator, signs in again with a TOTP code computed from the shown key, suspends a teacher (session ends, sign-in refused) and finds the audit entry.

## Dashboard tests (R1-P8)

- Integration: `TeacherDashboardTests` (zeros and a full period of days for a new teacher; a fixture with offers, leads in every status incl. archived and old ones, a diagnostic request, test attempts, page views today and 40 days ago → exact numbers for 30 and 90 days, funnel included; only 7/30/90 days; isolation).
- Vitest: `DashboardOverview.test.tsx` (tiles and links, funnel shares, requests by status, period switch requests the new period, daily visits as a table).

## Public test tests (R1-P7)

- Integration: `PublicTestTests` (no correct answers in the raw public JSON, `SelectSeveral`; drafts, inactive and unpublished sites hidden, cached page evicted; server-side scoring incl. partial and empty answers, random attempt ids, anonymous columns, attempt count; tampered submissions refused and not stored; **8 parallel attempts against a cap of 3**; contact with shared and unshared results, teacher views; one request per attempt and only from its own site; type/attempt rules; attempts lock destructive edits; indexable test page, link from the site page, sitemap; isolation of the results view).
- Vitest: `DiagnosticTest.test.tsx` (choices sent without cookies, score, request with the result and opt-in sharing, unanswered hint, busy test, unavailable test, text parity); results card and the lead's test result.
- Playwright `diagnostic.spec.ts`: phone visitor opens the test from the site, answers, sees the score, sends a request sharing the result → the teacher sees it on the lead and in the test's results.

## Diagnostic test builder tests (R1-P6)

- Unit: `DiagnosticTestRulesTests` (scoring: all correct, several correct options all-or-nothing, duplicates, unanswered/unknown, zero-point questions; publish readiness; request validation incl. null questions; slugs).
- Architecture: `PublicProjectionTests` (no correct answers or domain entities in any public projection).
- Integration: `DiagnosticTestTests` (create with generated and suffixed addresses, unique per workspace, ids kept on edit, stale version, foreign question/option ids refused, publish readiness errors, publish/deactivate/republish, **D-38** destructive edits refused and text fixes allowed after attempts, duplicate as editable draft, archive keeps the address, isolation kit on every endpoint, composite FK and points CHECK in the database).
- Vitest: `TestsPages.test.tsx` (list, create with questions/options/points, browser validation, ids kept, publish problems shown, publish after saving with the new version, structure locked with attempts and duplicate, archive with confirmation).

## Lead tests (R1-P5)

- Unit: `LeadRulesTests` (every status transition, form tokens: site-bound, tampered, expired, future, request validation, minimal localized email, consent texts).
- Integration: `LeadTests` (form contents: only public offers and own subjects, `no-store`; full intake → inbox + email **without visitor data**, nothing echoed; honeypot and too-fast submissions dropped; invalid/foreign/expired tokens; field validation incl. hidden/inactive/foreign offers and foreign subjects; per-site hourly limit → 429; D-10b for publishing and leads; unknown/unpublished sites; statuses with versions and invalid transitions; notes; archive/restore; filters, search with literal `%`, paging, new count; isolation kit on every endpoint; composite FK in the database).
- Vitest: `ContactForm.test.tsx` (payload incl. token, consent version and empty honeypot without cookies; browser validation; hidden when unavailable; expired form renewed; rate limit message; mk/en text parity), `LeadsPages.test.tsx` (list, filters in the URL, archived, search, empty state; detail, allowed transitions with the loaded version, notes, archive, conflict, not found).
- Playwright `leads.spec.ts`: published site → anonymous phone visitor sends a lesson request → teacher email → inbox → detail → status and note. E2E teachers are confirmed through the dev mailbox (`teacherViaApi`).

## Public site tests (R1-P4)

- Unit: `PublicSiteRulesTests` (SEO title/description/fallbacks, JSON-LD content and escaping, currency minor units and price formatting, mk/en text parity, palette CSS = `SitePalettes`, slug route constraint, page-view visitor filter, robots/sitemap output, **render-model allow-list snapshot**, Vite manifest resolution and missing build); `SiteRulesTests` also checks contrast on the page background.
- Integration: `PublicSiteTests` (404 + noindex for unknown/unpublished, complete HTML with meta tags and parsed JSON-LD, only public offers and visible sections, encoded XSS payloads incl. `</script>`, draft vs published and unpublish, audit entries, **cache hit proven by a change behind the app's back, then eviction by profile and offer changes**, signed-in requests bypass the cache, query strings share an entry, suspended owner hidden and back after reactivation, sitemap/robots, page views only for visitors and from cached hits, concurrent first views counted, public photo rules, publish validation and isolation, onboarding step, slug change moves the page, landing page in both languages, read-only public tenant binding); `SpaHostingTests` (`/` is the Razor landing page with the built stylesheet).
- Vitest: publish card (publish, address, views, version follow-up, unpublish confirmation, disabled before the first save, slug-move warning), `copy-link` island and `mountIsland`, fixed minor-unit table identical to `Currencies.MinorDigits` (never the browser's CLDR data), `/` hands over to the server.
- Playwright `public-site.spec.ts`: publish → anonymous **phone** visit (Pixel 7: heading, title, canonical, island, no horizontal scroll, no console/CSP errors) → unpublish → 404 with noindex; landing page links to registration.

## Site tests (R1-P3)

- Unit: `SiteRulesTests` (slug rules and reserved words, name-based suggestion with transliteration, WCAG AA contrast of every palette, section schema, normalization).
- Integration: `SiteTests` (suggestions, draft save and version conflicts, slug uniqueness, **concurrent claims of one slug with exactly one winner**, settings and section validation, preview: encoded HTML, noindex, hidden sections, invalid/tampered/expired tokens, isolation kit, onboarding step, every SPA prefix reserved).
- Vitest: `SiteEditorPage.test.tsx` (suggestions, save with order/visibility/items, sandboxed preview, live availability, browser validation, conflicts).

## Offer tests (R1-P2)

- Unit: `OfferRulesTests` (kind-specific fields, price, duration, mode, currency).
- Integration: `OfferTests` (all three kinds, default currency, ordering, validation, **CHECK constraints via raw SQL** bypassing the API, version conflicts, archive/inactive → not bookable through `IOfferLookup`, reorder, isolation kit, onboarding "first offer").
- Vitest: `OffersPage.test.tsx` (money parsing, list, create package, browser validation, edit with version and conflict, reorder, archive).

## Teacher profile tests (R1-P1)

- Unit: `ImageProcessorTests` (magic bytes, limits, disguised files, EXIF/comment removal and orientation with real JPEGs that carry injected metadata, built in memory by `TestImages`).
- Integration: `TeacherProfileTests` (catalog, profile CRUD and validation, photo storage and cleanup, upload rules, antiforgery, isolation kit, derived onboarding), `LocalDiskFileStorageTests` (traversal, no overwrite). Uploaded files go to a per-run temporary folder (`ClassHubApiFactory.FileRoot`); seeded catalog tables survive the Respawn reset.
- Playwright: `e2e/profile.spec.ts` completes the profile with a photo (a PNG generated in memory) on desktop and mobile and checks the checklist.

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
