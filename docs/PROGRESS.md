# ClassHub — Implementation Progress

This is the live status of the autonomous implementation loop ([ROADMAP.md](ROADMAP.md) section I). It is updated at the start and end of every phase. After an interruption, read this file first, then follow [RECOVERY.md](RECOVERY.md).

## Current state

| Field | Value |
|---|---|
| Release | R2 — Teaching operations (R0 and R1 complete) |
| Current phase | R2-P8 — Materials |
| Branch | `feature/r2-p8-materials` (from `main` @ `637aede`) |
| Workflow step | 5 — signed Codex review, then PR |
| Next action | R2-P8 PR; then R2-P9 (homework) |
| Open blockers | None. User actions (optional): direct-push probe (R0-P8); remove the now redundant required `pr-gate` check (see Decisions) |

## Phase status

Status values: `not started`, `in progress`, `in review` (PR open), `merged`, `blocked`.

| Phase | Name | Status | Branch | PR | Merge SHA | Codex review | CI | Notes |
|---|---|---|---|---|---|---|---|---|
| R0-P0 | Local & GitHub prerequisites | merged (direct bootstrap) | `main` | — | `53d3f15` | n/a (no code) | n/a | Repo created and configured |
| R0-P1 | Repository foundation & CI | merged | `chore/r0-p1-repository-foundation` | [#1](https://github.com/Andrej994x/classhub/pull/1) | `2e3c225` | 2 rounds: 1 × P2 (i18n check ignored a missing locale) fixed; round 2 clean | 8/8 green | Ruleset `main protection` created and verified |
| R0-P2 | EF Core & SQL Server foundation | merged | `chore/r0-p2-database-foundation` | [#4](https://github.com/Andrej994x/classhub/pull/4) | `63d189d` | 1 round: no actionable defects | 9/9 green | Windows-auth gate passed (Shared memory, NTLM); `migrations` added to the ruleset |
| R0-P3 | API platform baseline | merged | `chore/r0-p3-api-platform-baseline` | [#5](https://github.com/Andrej994x/classhub/pull/5) | `293ae76` | 5 rounds: 3 × P2 (forwarded headers fail-open, missing default rate limit, limiter after authorization) fixed; rounds 4–5 clean; CodeQL HttpOnly finding fixed | 9/9 green | Blocked once by an outdated CodeQL review thread (resolved after the fix) |
| R0-P4 | Authentication (Identity) | merged | `feature/r0-p4-authentication` | [#6](https://github.com/Andrej994x/classhub/pull/6) | `5203a72` | 3 rounds: 4 findings fixed (atomic registration, confirmation token check, reset enumeration, READCOMMITTEDLOCK), 1 P1 rejected with evidence | 9/9 green | |
| R0-P5 | Workspaces, roles & tenancy | merged | `feature/r0-p5-workspace-tenancy` | [#8](https://github.com/Andrej994x/classhub/pull/8) | `22f1158` | Pre-review + 4 rounds: 8 × P2 fixed (owner grant atomic + serialized, IANA time zones, ISO 4217 currencies, workspace backfill migration, OpenAPI route parameters, async raw-SQL detection, no-op audit); round 4 clean | 9/9 green | First PR checked by the corrected `pr-gate` (generated evidence of `6dabf9b`); migration applied to local `ClassHubDb` |
| R0-P6 | Web foundation & localization | merged | `feature/r0-p6-web-foundation` | [#9](https://github.com/Andrej994x/classhub/pull/9) | `eab9c19` | 3 rounds: 3 × P2 fixed (antiforgery fetch network errors, auth shell at `/login`, `<html lang>` at startup); round 3 clean | 9/9 green | |
| R0-P7 | Auth UI & first E2E | merged | `feature/r0-p7-auth-ui` | [#10](https://github.com/Andrej994x/classhub/pull/10) | `8d02b1f` | 7 rounds: 6 × P2 fixed (failed logout, 401 session handling, reset in a signed-in tab, late language answers, reconciling sessions with the API, identity-scoped cache); round 7 clean | 10/10 green incl. first `e2e` (12/12) | `e2e` added to the required checks |
| R0-P8 | Foundation hardening & closeout | merged | `chore/r0-p8-foundation-hardening` | [#11](https://github.com/Andrej994x/classhub/pull/11) | `7973981` | Whole-repo security review: 2 × P1 + 2 × P2 fixed; branch rounds: 1 × P1 (gate field binding), 1 × P2 (key recovery), CodeQL high (keygen race) fixed; final round clean | 10/10 green | Transition PR: kept the old `pull_request` trigger so a gate ran on it |

| R0 closeout | Trusted gate only, Release 0 complete | merged | `chore/r0-close-trusted-gate` | [#12](https://github.com/Andrej994x/classhub/pull/12) | `c2b5219` | 1 round clean | green | First PR checked only by the trusted gate: `Evidence signature valid for 602807f` |
| R1-P1 | Teacher profile & onboarding | merged | `feature/r1-p1-teacher-profile` | [#13](https://github.com/Andrej994x/classhub/pull/13) | `3bb6b95` | 11 rounds: 9 × P2 fixed (limits feedback, atomic settings, partial blobs, stale-form versioning, form lock, first-profile race, settings re-send ×2, malformed multipart); final clean | green after 1 CI fix (OS-independent storage keys) | Migration applied to local `ClassHubDb` |
| R1-P2 | Offers & programs | merged | `feature/r1-p2-offers` | [#14](https://github.com/Andrej994x/classhub/pull/14) | `465d9e4` | 3 rounds: 3 × P2 fixed (form version binding, NULL-proof CHECKs, stale version after reorder); final clean | green | Migration applied to local `ClassHubDb` |
| R1-P3 | Public site model & editor | merged | `feature/r1-p3-site-editor` | [#15](https://github.com/Andrej994x/classhub/pull/15) | `ebb075b` | 5 rounds: 7 × P2 fixed (Vite preview proxy, null sections, no-referrer kept, item errors, slug newline, preview photo, token redaction); final clean | green | Migration applied to local `ClassHubDb` |
| R1-P4 | Public site rendering, SEO & islands | merged | `feature/r1-p4-public-site` | [#16](https://github.com/Andrej994x/classhub/pull/16) | `f78ecc8` | 5 rounds: 2 × P2 fixed (unsaved edits on unpublish, query-string cache poisoning of `/`); final clean | green after 2 fix cycles (currency digits vs CLDR, sign-out race) | Migration applied to local `ClassHubDb` |
| R1-P5 | Leads management | merged | `feature/r1-p5-leads` | [#17](https://github.com/Andrej994x/classhub/pull/17) | `9a948f1` | 5 rounds: 5 × P2 fixed (atomic per-site quota, consent version change, localized server errors, retry after token renewal, debounced search vs filters); final clean | green | Migration applied to local `ClassHubDb` |
| R1-P6 | Diagnostic test builder | merged | `feature/r1-p6-diagnostic-builder` | [#18](https://github.com/Andrej994x/classhub/pull/18) | `97b4167` | 3 rounds: 3 × P2 fixed (published tests stay publishable, repeated ids vs D-38, form version for status changes); final clean | green | Migration applied to local `ClassHubDb` |
| R1-P7 | Public test taking & results | merged | `feature/r1-p7-diagnostic-public` | [#19](https://github.com/Andrej994x/classhub/pull/19) | `7c79580` | 6 rounds: 6 × P2 fixed (attempts vs. edits, Vite proxy, shown version, eviction after commit, rollback with data, attempt → lead FK); final clean | green after 1 CI fix (TEST_CHANGED translation) | Migrations applied to local `ClassHubDb` |
| R1-P8 | Basic teacher dashboard | merged | `feature/r1-p8-teacher-dashboard` | [#20](https://github.com/Andrej994x/classhub/pull/20) | `2878b06` | 1 round: no actionable defects | green | No migration |
| R1-P9 | Platform admin foundation | merged | `feature/r1-p9-admin-foundation` | [#21](https://github.com/Andrej994x/classhub/pull/21) | `c2e3ac4` | 4 rounds: 2 × P2 fixed (authenticator replacement needs the current factor; lockout enforced on step-up); rounds 3–4 clean | green after 1 CI fix (CodeQL regex escaping in the E2E spec) | No migration |
| R1-P10 | SaaS plans & entitlements | merged | `feature/r1-p10-plans-entitlements` | [#22](https://github.com/Andrej994x/classhub/pull/22) | `1f2a0f5` | 3 rounds: 2 × P2 fixed (null plan-limit list validation; expiry recorded before reactivation); round 3 clean | green | Migration applied to local `ClassHubDb` |
| R1-P11 | Moderation & reference data admin | merged | `feature/r1-p11-moderation-catalog` | [#23](https://github.com/Andrej994x/classhub/pull/23) | `5c90a22` | 3 rounds: 4 × P2 fixed (parallel section hides, deactivated references on edit, parallel report decisions, atomic report cap); round 3 clean | green | Migration applied to local `ClassHubDb` |
| R1-P12 | Release 1 hardening | merged | `chore/r1-p12-hardening` | [#24](https://github.com/Andrej994x/classhub/pull/24) | `3c11d1b` | 1 round clean; release-level review of all R1 changes clean | green | Release 1 complete |
| R2-P1 | Students | merged | `feature/r2-p1-students` | [#25](https://github.com/Andrej994x/classhub/pull/25) | `550667c` | 6 rounds: 5 × P2 fixed (source-lead FK, editor remount on refetch, stale cache on reopen, phone format, conversion of closed requests); final round clean | green | Migration applied to local `ClassHubDb` |
| R2-P2 | Groups | merged | `feature/r2-p2-groups` | [#26](https://github.com/Andrej994x/classhub/pull/26) | `0873148` | 4 rounds: 3 × P2 fixed (cap on re-added members, deactivated subject shown, paged member picker); round 4 clean | green | Migration applied to local `ClassHubDb` |
| R2-P3 | Student accounts & invitations | merged | `feature/r2-p3-student-accounts` | [#27](https://github.com/Andrej994x/classhub/pull/27) | `29dd0ff` | 1 round: no actionable defects | green | Migration applied to local `ClassHubDb` |
| R2-P4 | Lessons core & calendar | merged | `feature/r2-p4-lessons-calendar` | [#28](https://github.com/Andrej994x/classhub/pull/28) | `c01eae0` | 5 rounds: 5 × P2 fixed (time zone change grouping, held timer, picker label, repeated-hour instant, timer after reschedule); round 5 clean | green | Migration applied to local `ClassHubDb` |
| R2-P5 | Recurring lessons | merged | `feature/r2-p5-recurring-lessons` | [#29](https://github.com/Andrej994x/classhub/pull/29) | `153f1b5` | 7 rounds: 9 × P2 fixed (job starvation, scope fallback, started lessons, date-ended catch-up, one end condition, edit zone, next unstarted lesson, series-zone today, instant cutoff); round 7 clean | green | Migration applied to local `ClassHubDb` |
| R2-P6 | Availability & booking | merged | `feature/r2-p6-booking` | [#30](https://github.com/Andrej994x/classhub/pull/30) | `eecfd7b` | 4 rounds: 7 × P2 fixed (atomic confirmation, lesson-editor sync, null-safe settings, archived students, paged requests, real slot ends, bookable durations); round 4 clean | green | Migration applied to local `ClassHubDb` |
| R2-P7 | Attendance, notes & history | merged | `feature/r2-p7-attendance` | [#31](https://github.com/Andrej994x/classhub/pull/31) | `637aede` | 3 rounds: 3 × P2 fixed (backfill of earlier held lessons, attendance draft kept on notes save, cross-workspace history 404); round 3 clean | green | Migration applied to local `ClassHubDb` |
| R2-P8 | Materials | in progress | `feature/r2-p8-materials` | | | | | Plan: private materials with an extension + signature allow-list (20 MB), safe file names, plan storage quota under the plan lock, assignments to students or groups, authorized downloads (attachment, nosniff, sandbox CSP), student portal list and download |
| R2-P9 | Homework | not started | | | | | | |
| R2-P10 | Packages & payments | not started | | | | | | |
| R2-P11 | Notifications | not started | | | | | | |
| R2-P12 | Dashboards (teacher + student) | not started | | | | | | |
| R2-P13 | Release 2 hardening | not started | | | | | | |

Release 3 and Beta are listed in [ROADMAP.md](ROADMAP.md) section D. They are added to this table when Release 2 is complete.

## Release 1 — complete (2026-11-06, PR #24, `3c11d1b`)

Definition of Done (ROADMAP D, R1-P1…P12) demonstrated:
- A teacher builds a profile, offers and a public site, publishes it, and receives visitor requests and diagnostic-test requests; the whole journey runs through the UI in Playwright on desktop and phone (`release.spec.ts`, `leads.spec.ts`, `diagnostic.spec.ts`).
- Isolation: every workspace route denies other teachers (404) and anonymous callers (401), every admin route denies teachers (403) — discovered from the endpoint table (`IsolationSweepTests`), on top of the per-module isolation tests.
- Platform operations: MFA admin console, suspension hides the site at once (Playwright), plans with enforced limits, moderation with reports and hidden content, catalog admin; all audited.
- SEO: server-rendered pages with complete metadata, JSON-LD, sitemap and robots (Playwright checks); Lighthouse SEO 100 on a sample page.
- Mobile: no horizontal scroll at phone width on the teacher, public and admin pages (Playwright, Pixel 7).
- Release-level Codex review over all Release 1 changes (`codex review --base 7973981`, 276 files): no actionable defects. Every phase PR carried signed, commit-bound evidence of its final review.
- Remaining provisional items (block Beta, not Release 1): D-18 consent wording, D-19 CAPTCHA, D-27 export/deletion, D-28 hosting, D-29 final plan limits.

## Release 0 — complete (2026-11-04)

Definition of Done (ROADMAP E) demonstrated:
- A teacher registers, confirms the email, signs in and uses an isolated workspace in mk or en on desktop and mobile (Playwright 12/12 in CI).
- A second teacher cannot access the first teacher's workspace (integration isolation kit, Playwright "not found").
- All CI gates pass: backend, integration (Testcontainers), migrations, frontend, e2e, security, CodeQL, PR gate with signed, commit-bound Codex evidence.
- Every phase was merged through the ruleset (PRs #1, #4–#11; no bypass); setup, progress and recovery are documented (fresh-clone run in R0-P8).
- This closeout PR is the first one checked only by the trusted gate (`pull_request_target`, base-branch workflow, signature verification).

## Phase results

### R0-P1 — Repository foundation & CI (merged, PR #1, `2e3c225`)

- .NET 10 solution (Api host with `/health`, Core module library, unit/architecture/integration test projects on xUnit v3 + Microsoft.Testing.Platform), central package management with lock files.
- `web/`: React 19 + TS 6.0 + Vite 8 + React Router 8 + Tailwind v4, Vitest, oxlint, mk/en parity check.
- CI: `backend`, `integration`, `frontend`, `security` (gitleaks, vulnerable packages, npm audit, dependency review), `codeql (csharp)`, `codeql (javascript-typescript)`, `pr-gate`.
- Ruleset **main protection** (id 26526706): active on the default branch; PR required (squash only, review threads resolved); required checks = the 7 above from GitHub Actions (app 15368), strict (branch up to date); linear history; no deletion or force push; **no bypass actors** (`current_user_can_bypass: never`). Verified via `GET /repos/.../rules/branches/main`.
- Tests: 3 backend + 2 frontend, all green locally and in CI.

### R0-P3 — API platform baseline (merged, PR #5, `293ae76`)

- ProblemDetails with stable `code` + `traceId`; `ClassHubException` hierarchy; FluentValidation filter; Serilog request logging with redaction; security headers/CSP; HSTS outside dev; forwarded headers (off by default); named rate limits (validated options); cookie auth scheme (Identity application scheme, 401/403 without redirect); deny-by-default fallback policy + endpoint-metadata test; antiforgery for JSON (`GET /api/auth/antiforgery`, `X-XSRF-TOKEN`); module discovery under `/api`; module-boundary architecture tests; OpenAPI contract → TypeScript types with CI drift checks.
- Local tests: 105/105 backend green, web lint/i18n/tests/build green.
- Dependabot: TypeScript and `@types/node` majors ignored (D-04, Node 22 runtime). PRs #2 (TypeScript 7) and #3 (`@types/node` 26) stay **open but unmergeable** (they cannot pass `pr-gate`); closing them was blocked by the auto-mode permission check. **User action (optional):** close #2 and #3.

### R0-P2 — EF Core & SQL Server foundation (merged, PR #4, `63d189d`)

- **Gate passed:** `dotnet ef database update` created `ClassHubDb` on `.\SQLEXPRESS` via Windows authentication. The running API connects as `EFCore/10.0.14` over **Shared memory**, `NTLM`. Collation `Macedonian_FYROM_100_CI_AS_SC`, RCSI on, migrations `InitialTechnical` + `EnableReadCommittedSnapshot` applied. No SQL Server configuration was changed.
- Local tests: 49/49 green (unit 5, architecture 1, integration 43 on Testcontainers SQL Server 2022).
- CI `migrations` job sequence verified locally against a throwaway container.

### R1-P12 — Release 1 hardening (merged, PR #24, `3c11d1b`)

- Isolation sweep over every workspace and admin route, release-journey and SEO E2E, suspension hides the page, mobile pass, Lighthouse SEO 100, release-level Codex review clean; 593 backend / 160 Vitest / 28 Playwright tests.

### R1-P11 — Moderation & reference data admin (merged, PR #23, `5c90a22`)

- Report island and anonymous endpoint (rate limit, honeypot, atomic cap), staff queue with hide/dismiss, hidden content honored by every public query and the cache, teacher notices without reporter details, catalog admin (deactivate, never delete); 591 backend / 160 Vitest / 24 Playwright tests.

### R1-P10 — SaaS plans & entitlements (merged, PR #22, `1f2a0f5`)

- Free/Pro plans with the provisional D-29 limits, entitlement checks under a lock (published tests), manual Pro activation/extension with payment records, expiry job without data loss, teacher plan page, admin plans and subscription UI; 579 backend / 153 Vitest / 24 Playwright tests.

### R1-P9 — Platform admin foundation (merged, PR #21, `c2e3ac4`)

- TOTP MFA with recovery codes and two-step sign-in, `amr=mfa` staff policies with step-up, admin console (overview, users, suspension with reason, owner-only Platform Admin role, audit viewer), shorter staff sessions; 554 backend / 148 Vitest / 24 Playwright tests.

### R1-P8 — Basic teacher dashboard (merged, PR #20, `2878b06`)

- Dashboard endpoint from the modules' contracts (leads, tests, page visits, offers, onboarding, funnel) and the SPA overview with tiles, funnel, requests by status and a daily-visits chart; 520 backend / 137 Vitest / 22 Playwright tests.

### R1-P7 — Public test taking & results (merged, PR #19, `7c79580`)

- Public tests without answers, server-side scoring of the shown version, anonymous attempts with random ids, per-IP and atomic per-test limits, attempts serialized with structural edits (D-38), diagnostic requests with opt-in result sharing (D-18), Razor test page, sitemap, teacher results; 516 backend / 134 Vitest / 22 Playwright tests.

### R1-P6 — Diagnostic test builder (merged, PR #18, `97b4167`)

- Tests with questions, options, correct answers and points; whole-test saves with stable ids; publish readiness, deactivate, duplicate, archive; D-38 lock after attempts; server-side scoring; architecture test against answers in public projections; 502 backend / 127 Vitest / 20 Playwright tests.

### R1-P5 — Leads management (merged, PR #17, `9a948f1`)

- Public contact/lesson-request island with signed form tokens, honeypot, fill time, per-IP and atomic per-site limits, versioned consent (D-18); D-10b for publishing and leads; minimal teacher email; inbox with statuses, notes, archive, filters; 468 backend / 120 Vitest / 20 Playwright tests.

### R1-P4 — Public site rendering, SEO & islands (merged, PR #16, `f78ecc8`)

- Publish/unpublish, Razor landing and teacher sites with SEO/JSON-LD, sitemap/robots, output cache with eviction, D-32 page views, Vite islands (copy-link), strict public CSP; CI also surfaced and fixed a browser-dependent currency table and a sign-out race; 428 backend / 105 Vitest / 18 Playwright tests.

### R1-P3 — Public site model & editor (merged, PR #15, `ebb075b`)

- Site model (unique slug, templates, WCAG-tested palettes, language, structured sections, draft/published JSON), editor with live slug availability, signed 15-minute preview (Razor, noindex, no-referrer, token redacted from logs); 381 backend / 97 Vitest / 14 Playwright tests.

### R1-P2 — Offers & programs (merged, PR #14, `465d9e4`)

- Offers (single lesson, package, group program) with database CHECKs (price ≥ 0, lessons/seats per kind incl. NULLs), version, archive, reorder, `IOfferLookup` for leads; offers UI; 326 backend / 91 Vitest tests.

### R1-P1 — Teacher profile & onboarding (merged, PR #13, `3bb6b95`)

- Catalog (seeded subjects/categories/levels mk/en, languages, countries), teacher profile with optimistic concurrency, photo upload (magic bytes, limits, SkiaSharp re-encode without metadata, atomic swap), local file storage, derived onboarding checklist, profile UI; 297 backend / 70 Vitest / 14 Playwright tests.

### R0-P8 — Foundation hardening & closeout (merged, PR #11, `7973981`)

- **Whole-repository Codex security review** (custom prompt with the AGENTS.md checklist, `codex review -` on `main` @ `8d02b1f`; transcript `.codex-review/r0-p8-whole-repo-security.transcript.md`):
  - P1 — PR evidence was self-attested (could be typed without running Codex). **Owner decision (2026-11-04): signed local attestation.** Evidence is signed with an Ed25519 key that exists only on the review machine (`%USERPROFILE%\.classhub\`, fingerprint `8caddbdb…1f396`); the gate verifies it with the committed public key.
  - P1 — `pr-gate` ran the PR's own workflow definition. Now `pull_request_target` (base-branch definition, no PR code) publishing the required commit status `classhub/codex-evidence`.
  - P2 — registration timing revealed registered emails: equal hashing work on both paths (test).
  - P2 — `Email:Provider=DevMailbox` was possible in Production: startup now fails outside Development/Testing/E2E (tests).
- **Docs:** threat model in `SECURITY.md` (assets, actors, trust boundaries, STRIDE table, residual risks); ARCHITECTURE overview at the end of R0; DATABASE migration history; DEVELOPMENT required checks, evidence key; RECOVERY key rotation.
- **Repository verification (API, 2026-11-04):** ruleset `main protection` active, `current_user_can_bypass: never`, no bypass actors; rules deletion, non-fast-forward, linear history, PR (squash only, review threads resolved), 9 required checks incl. `e2e`; secret scanning + push protection on; Dependabot alerts + security updates on; private vulnerability reporting on; 0 open Dependabot, code scanning and secret scanning alerts. Every commit on `main` except the R0-P0 bootstrap came through a PR.
- **Dependency audits:** `dotnet list package --vulnerable --include-transitive`: none; `npm audit`: 0 vulnerabilities.
- **Fresh-clone setup test** (only the commands in `DEVELOPMENT.md`, clone of `main` @ `8d02b1f` in a new folder): tool restore, restore, `npm ci`, `dotnet ef database update` (no-op), Release build with warnings as errors, unit 73/73, architecture 14/14, web lint/i18n/Vitest 60/60/build: all green in 110 s; `dotnet run --project src/ClassHub.Api` served `/health` 200, `/health/ready` 200 (Windows authentication to `ClassHubDb`), `/app` and `/login` (built SPA), `/api/auth/me` 401 ProblemDetails.
- **Direct-push probe:** not run by the agent (the auto-mode permission check blocks pushes to `main`, correctly). The API shows the PR rule applies to everyone (`current_user_can_bypass: never`). **User action (optional):** `git commit --allow-empty -m probe` on a scratch branch, `git push origin HEAD:main` must fail with `GH013`, then delete the scratch branch.

### R0-P7 — Auth UI & first E2E (merged, PR #10, `8d02b1f`)

- Sign-in, registration, email confirmation, password reset and dev mailbox screens (RHF + Zod); session-aware shell, UX-only guards with same-site `returnTo`; account language saved on the account (`PATCH /api/auth/me`); sessions reconciled with the API after 401s, resets and account changes; guarded E2E environment.
- Playwright: 3 flows × mk/en × desktop/mobile = 12/12 locally and in CI (first `e2e` run); `e2e` added to the ruleset's required checks.

### R0-P6 — Web foundation & localization (merged, PR #9, `eab9c19`)

- React Router data router with teacher/student/admin/auth layouts (responsive shell, mobile drawer), shadcn-style primitives on Tailwind v4 tokens, i18next mk/en (remembered choice, `<html lang>`), `openapi-fetch` client (cookies, in-memory antiforgery token, ProblemDetails → translated messages), Intl helpers; production build served by the API from `wwwroot/dist` (SPA fallback only for its prefixes).
- Local tests: 234/234 backend, 30/30 Vitest; manual check of the API serving the built SPA.

### R0-P5 — Workspaces, roles & tenancy (merged, PR #8, `22f1158`)

- Workspaces + Owner membership provisioned at registration (and backfilled by the migration for existing accounts); `ws:` permission policies, non-members get 404; named query filters `Tenant`/`Archived`, save interceptor, composite tenant FKs, bypass allow-list test (sync and async raw SQL); audit log; seeded platform roles; CLI `grant-platform-owner` (transaction + `sp_getapplock`).
- Local tests: 221/221 backend. `dotnet ef database update` applied `AddWorkspacesTenancyAndAudit` to `ClassHubDb` (no accounts yet, so the backfill had nothing to do).
- Verification of the corrected PR gate in GitHub Actions: `PR gate passed: … Codex review of 6dabf9b (base 56a5523, codex-cli 0.164.0) recorded, final round P0=0 P1=0 P2=0 P3=0, rejected 0, unresolved 0.`

### PR-gate evidence fix (merged, PR #7, `56a5523`)

- **Root cause** (same failure on #1, #4, #5, #6): PRs were opened, and fixes pushed, while the body still said "Codex review: in progress"; evidence was assembled by hand and not tied to a commit (the gate accepted any PR commit); re-runs of `pr-gate` reuse the event payload, so a corrected body was never seen; no concurrency group.
- **Fix:** `scripts/codex-review.mjs` runs the real review on a clean tree and writes commit-bound evidence (`review.json`: commit, base, CLI version, transcript SHA-256; raw final message). `pr-gate` reads the body from the API, requires reviewed commit = PR head SHA, recounts severities from the raw output, requires 0 unresolved P0/P1 and a `### Rejected findings` section for rejected ones; per-PR concurrency. 14 node tests run in the `frontend` job.
- **Merged PRs #1, #4, #5, #6:** bodies regenerated from their real final-round transcripts (no new results invented); all pass the new gate script against the live API. #6 records its 1 rejected P1 with the evidence.
- **Incident 2026-11-04 ~15:51 UTC:** GitHub Actions "degraded performance"; all runs for PR #7 stayed queued (no runner assigned) for over 25 minutes, then completed: 8/8 green, auto-merged. The `pr-gate` re-runs on the corrected bodies of #1, #4, #5 and #6 all passed.

## R0-P0 prerequisite results (2026-11-04)

| Check | Result |
|---|---|
| .NET SDK | 10.0.302 ✅ |
| Node.js / npm | v22.17.0 / 10.9.2 ✅ (meets Vite 8; Node 24 LTS before Beta, D-03) |
| GitHub CLI | Logged in as `Andrej994x`, scopes `repo, workflow, read:org, gist` ✅ |
| Codex CLI | 0.164.0, logged in with ChatGPT ✅ |
| Docker | Engine hung at first; fixed by restarting Docker Desktop (see Blockers). Docker 25.0.3, API 1.44, Linux containers ✅ |
| Git identity (repo-local) | `Andrej Nestorov` / `103068803+Andrej994x@users.noreply.github.com` (D-07) ✅ |
| SQL Server | No changes, as planned (ROADMAP Appendix 1) ✅ |
| GitHub repository | `Andrej994x/classhub`, **public**, default branch `main` ✅ |
| Merge settings | Squash only (title = PR title, message = commit messages); auto-merge allowed; head branches deleted after merge ✅ |
| Security settings | Secret scanning, push protection, Dependabot alerts and security updates, private vulnerability reporting ✅ |

**Incident during R0-P0:** GitHub reported degraded performance on 2026-11-04. Repository creation returned GraphQL and HTTP 502 errors, but the repository had been created; the first push was rejected (`commit_refs`), the retry succeeded. All settings were read back and verified.

## Blockers

| Blocker | Affects | Action needed |
|---|---|---|
| ~~Docker engine not responding~~ **Resolved 2026-11-04** | R0-P2 onward | Docker Desktop restarted at the user's request; verified with a disposable SQL Server container |
| ~~Unresolved review thread on PR #5~~ **Resolved 2026-11-04** | Merge of R0-P3 | CodeQL posted "Cookie 'HttpOnly' attribute is not set" as a review comment. Fixed in `fd1526e` (no readable cookie; code scanning shows no open alerts); the thread is now outdated but unresolved, and the ruleset requires resolved threads. Resolving it from here was blocked by the auto-mode permission check. **User action:** resolve the thread on PR #5 (or allow `gh api graphql … resolveReviewThread`). |

## Decisions taken during execution

| Date | Decision | Reason |
|---|---|---|
| 2026-11-04 | The plan file moved to `docs/ROADMAP.md` | Single source (ROADMAP Appendix 2) |
| 2026-11-04 | `.claude/settings.local.json` is git-ignored | Machine-specific |
| 2026-11-04 | Machine name and Windows domain account redacted from the public ROADMAP | Public repository |
| 2026-11-04 | Tests run on Microsoft.Testing.Platform (no VSTest) | xunit.v3 4.x on the .NET 10 SDK requires it |
| 2026-11-04 | `codex review --base origin/main` without a custom prompt | codex-cli 0.164 rejects a prompt combined with `--base`; guidelines come from AGENTS.md |
| 2026-11-04 | PR gate evidence = Codex section + Codex's final review message in the PR body; full transcript stays in `.codex-review/` | GitHub PR body size limit; transcripts include local paths |
| 2026-11-04 | PR evidence is generated by `scripts/codex-review.mjs` and bound to the PR head commit (superseding hand-written sections) | Self-reported review summaries are not evidence (PR #7) |
| 2026-11-04 | Ruleset requires the status `classhub/codex-evidence` **in addition to** `pr-gate` (10 required checks). Removing `pr-gate` (planned, since the job name is spoofable) was blocked by the agent's permission check as a required-check removal; the owner may remove it. Both are produced only by the trusted `pull_request_target` workflow | Additive change only; no protection weakened |
| 2026-11-04 | Evidence is Ed25519-signed with a key held only on the review machine; `pr-gate` runs on `pull_request_target` and publishes the required status `classhub/codex-evidence` | Owner decision on the R0-P8 P1 findings (signed local attestation instead of a paid CI integration or mandatory approvals) |
| 2026-11-04 | App's CI auto-fix monitor not enabled (auto-mode permission check denied it) | PR status is read when needed; auto-merge runs on GitHub |
| 2026-11-04 | `System.Security.Cryptography.Pkcs` pinned to 10.0.14 (transitive) | Vulnerable 9.0.11 via Microsoft.Data.SqlClient 6.1.7 |
