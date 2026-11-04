# CLAUDE.md

Read [AGENTS.md](AGENTS.md) first: shared rules, commands and the Codex review guidelines apply to Claude Code too.

## Autonomous workflow (docs/ROADMAP.md section I)

For each phase, in order:
1. `git switch main && git pull --ff-only`, then create the phase branch. Record phase, branch and a short plan in `docs/PROGRESS.md`.
2. Implement only the phase scope, with tests (unit, integration on Testcontainers SQL Server, Vitest, Playwright where the phase touches a critical flow).
3. Run all local checks (see AGENTS.md). Fix failures; never skip, weaken or delete tests to get green.
4. Commit (Conventional Commits, ending with the Co-Authored-By line).
5. Run `node scripts/codex-review.mjs review` (wraps `codex review --base origin/main` on the committed HEAD and writes generated evidence). Never type review evidence by hand.
6. Fix confirmed critical/high findings and medium findings that affect correctness, security or data integrity in new commits, re-run checks, and **review again**: evidence must always be for the current head commit.
7. Only after the final review completed: apply the evidence to the PR body (`node scripts/codex-review.mjs apply …`), push, open the PR, then `gh pr merge --squash --auto --delete-branch`. Never open a PR or push to an open PR with "review in progress" — see docs/DEVELOPMENT.md.
8. If a required check fails: fix on the branch (max 3 fix cycles per gate, then stop and report).
9. After the merge: sync `main`, record the merge SHA in `docs/PROGRESS.md` in the next phase's first commit, continue.

## Stop and ask the user when

- a gate still fails after 3 fix cycles, or Codex/Docker/GitHub is unavailable (never mark a gate passed without real output);
- a critical/high finding needs a business decision, or a blocking decision (ROADMAP J.4) is reached;
- an action is outside the approved envelope: SQL Server configuration changes, destructive operations on the local `ClassHubDb`, new accounts/secrets/paid services, GitHub settings beyond ROADMAP I.1, installing system software.

## Local safety

- The local database is `ClassHubDb` on `.\SQLEXPRESS` (Windows authentication). Only `dotnet ef database update` changes it; never drop or reset it without the user's approval.
- Never use `--admin` merges, force-push `main`, or bypass the ruleset.
