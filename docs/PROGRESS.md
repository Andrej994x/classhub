# ClassHub — Implementation Progress

This is the live status of the autonomous implementation loop ([ROADMAP.md](ROADMAP.md) section I). It is updated at the start and end of every phase. After an interruption, read this file first, then follow `docs/RECOVERY.md` (created in R0-P1).

## Current state

| Field | Value |
|---|---|
| Release | R0 — Foundation |
| Current phase | R0-P1 — Repository foundation & CI |
| Branch | `chore/r0-p1-repository-foundation` (created from `main` @ `53d3f15`) |
| Workflow step | 1 — branch created; R0-P0 result recorded (this commit) |
| Next action | R0-P1 step 2–3: scaffold solution, web app, CI, docs (ROADMAP section K) |
| Open blockers | Docker engine not responding (see Blockers); blocks R0-P2, not R0-P1 |

## Phase status

Status values: `not started`, `in progress`, `in review` (PR open), `merged`, `blocked`.

| Phase | Name | Status | Branch | PR | Merge SHA | Codex review | Notes |
|---|---|---|---|---|---|---|---|
| R0-P0 | Local & GitHub prerequisites | merged (direct bootstrap) | `main` | — | `53d3f15` | n/a (no code) | Repo created and configured; see results below |
| R0-P1 | Repository foundation & CI | in progress | `chore/r0-p1-repository-foundation` | | | | |
| R0-P2 | EF Core & SQL Server foundation | not started | | | | | Needs Docker; first gate: Windows auth over Shared Memory |
| R0-P3 | API platform baseline | not started | | | | | |
| R0-P4 | Authentication (Identity) | not started | | | | | |
| R0-P5 | Workspaces, roles & tenancy | not started | | | | | |
| R0-P6 | Web foundation & localization | not started | | | | | |
| R0-P7 | Auth UI & first E2E | not started | | | | | |
| R0-P8 | Foundation hardening & closeout | not started | | | | | |

Releases 1–3 and Beta are listed in [ROADMAP.md](ROADMAP.md) section D. They are added to this table when Release 0 is complete.

## R0-P0 prerequisite results (2026-11-04)

| Check | Result |
|---|---|
| .NET SDK | 10.0.302 ✅ |
| Node.js / npm | v22.17.0 / 10.9.2 ✅ (meets Vite 8; Node 24 LTS before Beta, D-03) |
| GitHub CLI | Logged in as `Andrej994x`, scopes `repo, workflow, read:org, gist` ✅ |
| Codex CLI | 0.164.0, logged in with ChatGPT ✅ |
| Docker | Docker Desktop processes running, but `docker info` did not answer within 120 s ⚠️ |
| Git identity (repo-local) | `Andrej Nestorov` / `103068803+Andrej994x@users.noreply.github.com` (D-07) ✅ |
| SQL Server | No changes, as planned (ROADMAP Appendix 1) ✅ |
| GitHub repository | `Andrej994x/classhub`, **public**, default branch `main` ✅ |
| Bootstrap commit | `53d3f15` pushed to `origin/main` ✅ |
| Merge settings | Squash only (title = PR title, message = commit messages); merge commits and rebase disabled; auto-merge allowed; head branches deleted after merge; update-branch allowed ✅ |
| Security settings | Secret scanning ✅, push protection ✅, Dependabot alerts ✅, Dependabot security updates ✅, private vulnerability reporting ✅; wiki and projects disabled |
| Ruleset on `main` | Not yet: created in R0-P1 after the first CI run reports the check names (ROADMAP I.1) |

**Incident during R0-P0:** GitHub reported degraded performance (API, Git operations, Actions) on 2026-11-04. Repository creation returned GraphQL and HTTP 502 errors, but the repository had been created. The first push was rejected (`commit_refs`); the retry succeeded. All settings were read back and verified afterwards.

## Blockers

| Blocker | Affects | Action needed |
|---|---|---|
| Docker engine not responding (`docker info` hangs; Docker Desktop has been running since 2026-10-28) | R0-P2 and every later phase (integration tests via Testcontainers, E2E) | The user restarts Docker Desktop and confirms `docker info` works. The autonomous loop does not restart system software itself |

## Decisions taken during execution

| Date | Decision | Reason |
|---|---|---|
| 2026-11-04 | The plan file `CLASSHUB_IMPLEMENTATION_PLAN.md` moved to `docs/ROADMAP.md` | Keep a single source (ROADMAP Appendix 2) |
| 2026-11-04 | `.claude/settings.local.json` is git-ignored | Local Claude Code settings are machine-specific |
| 2026-11-04 | Machine name and Windows domain account redacted from the public ROADMAP (`<machine>\SQLEXPRESS`) | Public repository; local identifiers stay local |
