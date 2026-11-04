# Recovery guide

How to resume or repair the autonomous workflow after an interruption or failure.

## 1. Resume after an interruption

1. Read [PROGRESS.md](PROGRESS.md): current phase, branch, workflow step, next action, blockers.
2. Check the local state:
   ```powershell
   git status --short --branch
   git log --oneline -5
   gh pr list --state open
   gh pr checks <number>          # if a PR is open
   ```
3. Continue from the recorded workflow step:
   - Uncommitted work on the phase branch → run the checks, commit, continue with the Codex review.
   - PR open, checks running → wait; check failing → fix on the branch.
   - PR merged → `git switch main; git pull --ff-only`, record the merge SHA, start the next phase.

## 2. A required check fails

- Open the run: `gh run list --branch <branch>`, `gh run view <id> --log-failed`.
- Reproduce locally with the same command (see DEVELOPMENT.md), fix on the branch, push. Auto-merge stays enabled.
- After 3 failed fix cycles for the same gate: stop, record the blocker in PROGRESS.md, ask the user.
- If GitHub itself is degraded (githubstatus.com), wait and re-run: `gh run rerun <id> --failed`.

## 3. Codex review problems

- `codex login status` must say logged in. Re-run `codex review --base origin/main` after `git fetch origin`.
- If Codex cannot produce a review (auth, quota, network): **do not** fill the PR gate section; record the blocker and stop. A PR without a real review cannot pass `pr-gate`.

## 4. A merged change is wrong

- Never force-push `main` or rewrite history.
- Revert through a PR: `git switch main; git pull --ff-only; git switch -c fix/revert-<phase>; git revert <merge-sha>`, then the normal workflow (checks, Codex review, PR, auto-merge).

## 5. Local database (`ClassHubDb` on `.\SQLEXPRESS`)

- Roll back the last migration locally: `dotnet ef database update <PreviousMigrationName> --project src/ClassHub.Core --startup-project src/ClassHub.Api`.
- Dropping or recreating `ClassHubDb` deletes local data. Only do it with the user's approval.
- Automated tests never use this database.

## 6. Docker / Testcontainers

- `docker info` must answer within a few seconds. If it hangs: quit Docker Desktop (tray → Quit), or stop its processes, then start Docker Desktop again. On 2026-11-04 this fixed a hung engine (see PROGRESS.md).
- Leftover test containers are removed automatically by Testcontainers' resource reaper (Ryuk). Manual cleanup: `docker ps -a --filter label=org.testcontainers=true`.

## 7. Branch protection

- Inspect: `gh api repos/Andrej994x/classhub/rulesets`. The `main` ruleset must be `active` with the required checks listed in DEVELOPMENT.md and no bypass actors.
- Never disable the ruleset to get a merge through.
