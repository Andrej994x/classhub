# ClassHub — Implementation Progress

This is the live status of the autonomous implementation loop ([ROADMAP.md](ROADMAP.md) section I). It is updated at the start and end of every phase. After an interruption, read this file first, then follow [RECOVERY.md](RECOVERY.md).

## Current state

| Field | Value |
|---|---|
| Release | R0 — Foundation |
| Current phase | R0-P2 — EF Core & SQL Server foundation |
| Branch | `chore/r0-p2-database-foundation` (from `main` @ `2e3c225`) |
| Workflow step | 5 — implementation and local checks done; Codex review next |
| Next action | Codex review → PR → add `migrations` to the ruleset → auto-merge → R0-P3 |
| Open blockers | None |

## Phase status

Status values: `not started`, `in progress`, `in review` (PR open), `merged`, `blocked`.

| Phase | Name | Status | Branch | PR | Merge SHA | Codex review | CI | Notes |
|---|---|---|---|---|---|---|---|---|
| R0-P0 | Local & GitHub prerequisites | merged (direct bootstrap) | `main` | — | `53d3f15` | n/a (no code) | n/a | Repo created and configured |
| R0-P1 | Repository foundation & CI | merged | `chore/r0-p1-repository-foundation` | [#1](https://github.com/Andrej994x/classhub/pull/1) | `2e3c225` | 2 rounds: 1 × P2 (i18n check ignored a missing locale) fixed; round 2 clean | 8/8 green | Ruleset `main protection` created and verified |
| R0-P2 | EF Core & SQL Server foundation | in progress | `chore/r0-p2-database-foundation` | | | | | Windows-auth gate passed (Shared memory, NTLM) |
| R0-P3 | API platform baseline | not started | | | | | | |
| R0-P4 | Authentication (Identity) | not started | | | | | | |
| R0-P5 | Workspaces, roles & tenancy | not started | | | | | | |
| R0-P6 | Web foundation & localization | not started | | | | | | |
| R0-P7 | Auth UI & first E2E | not started | | | | | | |
| R0-P8 | Foundation hardening & closeout | not started | | | | | | |

Releases 1–3 and Beta are listed in [ROADMAP.md](ROADMAP.md) section D. They are added to this table when Release 0 is complete.

## Phase results

### R0-P1 — Repository foundation & CI (merged, PR #1, `2e3c225`)

- .NET 10 solution (Api host with `/health`, Core module library, unit/architecture/integration test projects on xUnit v3 + Microsoft.Testing.Platform), central package management with lock files.
- `web/`: React 19 + TS 6.0 + Vite 8 + React Router 8 + Tailwind v4, Vitest, oxlint, mk/en parity check.
- CI: `backend`, `integration`, `frontend`, `security` (gitleaks, vulnerable packages, npm audit, dependency review), `codeql (csharp)`, `codeql (javascript-typescript)`, `pr-gate`.
- Ruleset **main protection** (id 26526706): active on the default branch; PR required (squash only, review threads resolved); required checks = the 7 above from GitHub Actions (app 15368), strict (branch up to date); linear history; no deletion or force push; **no bypass actors** (`current_user_can_bypass: never`). Verified via `GET /repos/.../rules/branches/main`.
- Tests: 3 backend + 2 frontend, all green locally and in CI.

### R0-P2 — EF Core & SQL Server foundation (in progress)

- **Gate passed:** `dotnet ef database update` created `ClassHubDb` on `.\SQLEXPRESS` via Windows authentication. The running API connects as `EFCore/10.0.14` over **Shared memory**, `NTLM`. Collation `Macedonian_FYROM_100_CI_AS_SC`, RCSI on, migrations `InitialTechnical` + `EnableReadCommittedSnapshot` applied. No SQL Server configuration was changed.
- Local tests: 49/49 green (unit 5, architecture 1, integration 43 on Testcontainers SQL Server 2022).
- CI `migrations` job sequence verified locally against a throwaway container.

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

## Decisions taken during execution

| Date | Decision | Reason |
|---|---|---|
| 2026-11-04 | The plan file moved to `docs/ROADMAP.md` | Single source (ROADMAP Appendix 2) |
| 2026-11-04 | `.claude/settings.local.json` is git-ignored | Machine-specific |
| 2026-11-04 | Machine name and Windows domain account redacted from the public ROADMAP | Public repository |
| 2026-11-04 | Tests run on Microsoft.Testing.Platform (no VSTest) | xunit.v3 4.x on the .NET 10 SDK requires it |
| 2026-11-04 | `codex review --base origin/main` without a custom prompt | codex-cli 0.164 rejects a prompt combined with `--base`; guidelines come from AGENTS.md |
| 2026-11-04 | PR gate evidence = Codex section + Codex's final review message in the PR body; full transcript stays in `.codex-review/` | GitHub PR body size limit; transcripts include local paths |
| 2026-11-04 | App's CI auto-fix monitor not enabled (auto-mode permission check denied it) | PR status is read when needed; auto-merge runs on GitHub |
| 2026-11-04 | `System.Security.Cryptography.Pkcs` pinned to 10.0.14 (transitive) | Vulnerable 9.0.11 via Microsoft.Data.SqlClient 6.1.7 |
