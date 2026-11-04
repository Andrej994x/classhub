# AGENTS.md — shared rules for AI agents (Claude Code, Codex) and contributors

## Sources of truth

1. [docs/PRODUCT_SCOPE.md](docs/PRODUCT_SCOPE.md): business requirements (modules M01–M17, business rules, acceptance criteria).
2. [docs/ROADMAP.md](docs/ROADMAP.md): approved architecture, phases, decisions (D-xx) and workflow.
3. [docs/PROGRESS.md](docs/PROGRESS.md): what is implemented right now.

If code and documents disagree, the documents win, unless `PROGRESS.md` records an approved deviation.

## Repository rules

- Stack: ASP.NET Core on .NET 10 (`src/`), EF Core + SQL Server, React + TypeScript + Vite (`web/`). Do not introduce other frameworks or infrastructure (no Redis, brokers, MediatR, AutoMapper, FluentAssertions).
- One phase = one branch (`feature/*`, `fix/*`, `chore/*`) = one PR, squash-merged automatically after all required checks pass. Never push to `main` directly.
- Implement only the current phase's scope. No future-release features, no unrelated refactoring.
- **Never commit secrets**, connection strings with passwords, database files/backups, uploads, or real personal data.
- Integration tests use only the disposable Testcontainers SQL Server. Never point tests at the local `SQLEXPRESS`, `ClassHubDb`, or any shared server, and never weaken test guards.
- Schema changes only through EF Core migrations (from R0-P2). Migrations are never applied on startup outside the `Testing`/`E2E` environments.
- All user-facing text exists in Macedonian (`mk`, default) and English (`en`).

## Commands

| Task | Command |
|---|---|
| Restore tools | `dotnet tool restore` |
| Build backend | `dotnet build ClassHub.slnx -c Release -warnaserror` |
| All backend tests | `dotnet test --solution ClassHub.slnx -c Release` (integration tests need Docker from R0-P2) |
| One test project | `dotnet test --project tests/ClassHub.UnitTests` |
| Run API | `dotnet run --project src/ClassHub.Api` → http://localhost:5080 |
| Web install / dev | `cd web && npm ci && npm run dev` → http://localhost:5173 |
| Web checks | `cd web && npm run lint && npm run i18n:check && npm test && npm run build` |

## Codex review guidelines

Codex reviews every branch with `codex review --base origin/main`. Report findings with a severity (critical / high / medium / low), the file and line, and a concrete failure scenario. Focus on:

1. **Business correctness:** behavior matches `docs/PRODUCT_SCOPE.md` and the phase in `docs/ROADMAP.md` (rules, statuses, limits, edge cases). Flag invented rules and missing required behavior.
2. **Multi-tenant isolation:** every tenant-owned entity is filtered by workspace; no route, query, raw SQL, `IgnoreQueryFilters`, join or DTO can expose or modify another workspace's data; cross-tenant access returns 404.
3. **Authentication & authorization:** every endpoint has explicit `AllowAnonymous` or a named policy; permission checks match the role; platform admins get no private student data; MFA where required; cookies `HttpOnly`/`Secure`/`SameSite`; antiforgery on unsafe cookie-authenticated requests.
4. **Public vs private data:** public pages and endpoints expose only allow-listed, published fields; no student or lead data in public responses, logs, or emails to the wrong recipient.
5. **Data integrity & transactions:** invariants enforced by the database where possible (constraints, unique indexes, composite tenant FKs); multi-step writes are atomic; no lost updates.
6. **Concurrency:** booking overlaps, package consumption, capacity and plan limits cannot be violated by parallel requests.
7. **Migrations:** reversible, no data loss, model and migration in sync, SQL Server specifics (NULLs in unique indexes, cascade paths, collation).
8. **Security:** injection, XSS (Razor and React), file uploads, open redirects, enumeration, rate limits, secrets in code/logs, unsafe deserialization.
9. **Tests:** missing or weak tests for the rules above; tests that mock away the database where integration tests are required.
10. **Regressions & architecture:** module boundaries (`Contracts` only), conventions in `docs/ROADMAP.md`, frontend/backend contract consistency.

Ignore pure style preferences unless they hide a bug.
