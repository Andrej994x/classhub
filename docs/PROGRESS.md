# ClassHub — Implementation Progress

This is the live status of the autonomous implementation loop ([ROADMAP.md](ROADMAP.md) section I). It is updated at the start and end of every phase. After an interruption, read this file first, then follow [RECOVERY.md](RECOVERY.md).

## Current state

| Field | Value |
|---|---|
| Release | R1 — Teacher presence (R0 complete) |
| Current phase | R1-P1 — Teacher profile & onboarding |
| Branch | `feature/r1-p1-teacher-profile` (from `main` @ `c2b5219`) |
| Workflow step | 5 — signed Codex review, then PR |
| Next action | R1-P1 PR; then R1-P2 (offers & programs) |
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
| R1-P1 | Teacher profile & onboarding | in progress | `feature/r1-p1-teacher-profile` | | | | | |
| R1-P2 | Offers & programs | not started | | | | | | |
| R1-P3 | Public site model & editor | not started | | | | | | |
| R1-P4 | Public site rendering, SEO & islands | not started | | | | | | |
| R1-P5 | Leads management | not started | | | | | | |
| R1-P6 | Diagnostic test builder | not started | | | | | | |
| R1-P7 | Public test taking & results | not started | | | | | | |
| R1-P8 | Basic teacher dashboard | not started | | | | | | |
| R1-P9 | Platform admin foundation | not started | | | | | | |
| R1-P10 | SaaS plans & entitlements | not started | | | | | | |
| R1-P11 | Moderation & reference data admin | not started | | | | | | |
| R1-P12 | Release 1 hardening | not started | | | | | | |

Releases 2–3 and Beta are listed in [ROADMAP.md](ROADMAP.md) section D. They are added to this table when Release 1 is complete.

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
