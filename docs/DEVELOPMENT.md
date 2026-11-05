# Development guide

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.302 (pinned in `global.json`, `rollForward: latestFeature`) | `dotnet --version` |
| Node.js / npm | ≥ 22.12 (Node 24 LTS recommended before Beta) | `node -v` |
| Docker Desktop | Linux containers | Required for integration and E2E tests (from R0-P2). If `docker info` hangs, quit and restart Docker Desktop |
| SQL Server | 2022 Express, local instance `.\SQLEXPRESS`, Windows authentication | Used by the running app only, never by automated tests (from R0-P2) |
| Git, GitHub CLI | current | `gh auth status` |
| Codex CLI | 0.164+ | `codex login status`; used for the independent review |

## First-time setup

```powershell
git clone https://github.com/Andrej994x/classhub.git
cd classhub
dotnet tool restore
dotnet restore ClassHub.slnx
cd web; npm ci; cd ..
```

## Local database (once, then after each new migration)

```powershell
dotnet tool run dotnet-ef database update --project src/ClassHub.Core --startup-project src/ClassHub.Api
```

Creates or updates `ClassHubDb` on `.\SQLEXPRESS` with Windows authentication. Details: [DATABASE.md](DATABASE.md).

## Running locally

| Part | Command | URL |
|---|---|---|
| API + Razor public pages (landing `/`, sites `/{slug}`) | `dotnet run --project src/ClassHub.Api` | http://localhost:5080 (`/health`, `/health/live`, `/health/ready`) |
| React SPA (dev server, proxies `/api`, `/preview` and the public pages to 5080) | `cd web; npm run dev` | http://localhost:5173 |
| Public page styles and islands (built assets, rebuilt on change) | `cd web; npx vite build --watch` | used by the Razor pages on 5080 and 5173 |
| Built SPA served by the API (same origin as production) | `cd web; npm run build`, then run the API | http://localhost:5080/app |

The SPA areas are `/app` (teacher), `/student`, `/admin` and the auth pages. Language: Macedonian by default, switchable to English in the header (remembered per browser). New UI text goes into both `web/src/i18n/locales/mk.json` and `en.json` (`npm run i18n:check`).

## Email in development

Emails (account confirmation, password reset) go to the **dev mailbox**: the `DevMailbox` table in `ClassHubDb`, readable at `GET http://localhost:5080/api/dev/mailbox?to=<email>` (Development, Testing and E2E only), and as a page at `/dev/mailbox` (links in the messages are clickable). Links point to `App:PublicBaseUrl` (`http://localhost:5173` in Development). No email leaves the machine.

## Checks (run before every commit)

```powershell
dotnet build ClassHub.slnx -c Release -warnaserror
dotnet test --solution ClassHub.slnx -c Release
cd web; npm run lint; npm run i18n:check; npm test; npm run build; cd ..
```

Tests run on **Microsoft.Testing.Platform** (opted in via `global.json`). Use `dotnet test --solution ...` or `dotnet test --project ...`.

NuGet packages are versioned centrally in `Directory.Packages.props` with lock files (`packages.lock.json`). After changing a package, run `dotnet restore` and commit the updated lock files. CI restores with `--locked-mode`.

## Branching and workflow

- `main` is protected by a ruleset: pull request required, required checks, linear history, no force pushes, no bypass.
- Branches: `feature/<phase>-<name>`, `fix/<name>`, `chore/<phase>-<name>`. One phase = one PR = one squash commit.
- Workflow per phase: see [CLAUDE.md](../CLAUDE.md) and [ROADMAP.md](ROADMAP.md) section I.

## Required checks (GitHub Actions)

| Check | Workflow | What it verifies |
|---|---|---|
| `backend` | `ci.yml` | locked restore, Release build with warnings as errors, unit + architecture tests |
| `integration` | `ci.yml` | integration tests on a Testcontainers SQL Server 2022 |
| `migrations` | `ci.yml` | no model changes without a migration; all migrations apply to an empty SQL Server 2022; idempotent script re-applies as a no-op; collation and RCSI verified |
| `frontend` | `ci.yml` | `npm ci`, oxlint, i18n parity, API types current with `openapi.json`, Vitest, type-check + build |
| `security` | `security.yml` | gitleaks (full history), vulnerable NuGet packages, `npm audit`, dependency review |
| `codeql (csharp)`, `codeql (javascript-typescript)` | `codeql.yml` | CodeQL security-extended analysis |
| `e2e` | `ci.yml` | Playwright against the API in the E2E environment with a disposable SQL Server container (desktop + mobile, mk + en); report and traces uploaded on failure |
| status `classhub/codex-evidence` (published by the `pr-gate` job) and the `pr-gate` job itself (still required; redundant because any workflow could reuse the job name, the status is the trustworthy check) | `pr-gate.yml` (`pull_request_target`, base-branch definition) | branch name; **signed, generated** Codex evidence for exactly the checked commit (full head SHA, base SHA, CLI version, transcript SHA-256, Ed25519 signature); severity counts recomputed from the raw output; unresolved P0/P1 = 0; rejected P0/P1 need written reasoning |

## Independent Codex review and PR evidence

The PR gate accepts only evidence generated by `scripts/codex-review.mjs` from a real, completed review of
the **current head commit**, **signed** with the evidence key. Never type or edit the evidence block by hand:
evidence without a valid signature, or edited after signing, fails the gate.

### The evidence key (once per machine that runs reviews)

```powershell
node scripts/codex-review.mjs keygen
```

Creates an Ed25519 key pair: the **private key** goes to `%USERPROFILE%\.classhub\codex-evidence-ed25519.pem` (outside the repository; override with `CLASSHUB_EVIDENCE_KEY`), the **public key** to `scripts/ci/codex-evidence-public.pem`, which is committed and is what the gate trusts. Keep the private key secret and back it up outside the repository (for example in a password manager). Never commit it (`*.pem` is git-ignored; only the public key is allowed). Replacing a lost key: [RECOVERY.md](RECOVERY.md#3-codex-review-problems).

Current key fingerprint (SHA-256 of the public key): `8caddbdbe876ba2c08b3144430331ce1bfc374552bc6b87091da36392ab1f396`.

### Per PR

```powershell
# 1. Commit everything first (the review covers HEAD; a dirty tree is refused).
node scripts/codex-review.mjs review
#    → runs `codex review --base origin/main`, saves .codex-review/<sha>.transcript.md and writes
#      .codex-review/<sha>.evidence.md (reviewed/base SHA, codex-cli version, transcript SHA-256,
#      final-round severity counts, raw final message, Ed25519 signature over all of them)

# 2a. New PR: put the evidence into the body, then push and open the PR.
node scripts/codex-review.mjs apply --body .codex-review\pr-body.md --evidence .codex-review\<sha>.evidence.md
git push -u origin <branch>
gh pr create --base main --body-file .codex-review\pr-body.md --title "<title>"

# 2b. Open PR after new commits: re-run step 1, push, then replace the evidence in the PR body.
git push
node scripts/codex-review.mjs apply --pr <number> --evidence .codex-review\<sha>.evidence.md
```

Rules:
- **Open a PR (and push to an open PR) only after the review of that exact commit has completed.** Every new commit needs a new review; the gate compares the evidence with the commit it is checking.
- Fix confirmed findings in new commits and review again. If a final-round P0/P1 finding is rejected, pass `--rejected <n>` and add a `### Rejected findings` section with the technical reasoning (the gate requires it).
- Keep the human-written `### Review history` table (all rounds, findings, resolutions) above the generated block.
- If Codex cannot complete a review, there is no evidence and the PR cannot merge. Never work around that.
- `pr-gate` runs on `pull_request_target`: the workflow, the gate script and the public key always come from the base branch, so a PR cannot weaken its own gate, and no pull-request code is checked out or executed. It reads the current PR body from the API, verifies the signature, and publishes the result as the commit status **`classhub/codex-evidence`** (required by the ruleset; fork workflows cannot create it). A per-PR concurrency group makes the newest run the deciding one.
- What the signature proves: the evidence was produced by the review script on a machine holding the private key, for exactly this head commit, with exactly this raw Codex output. It does not make that machine trustworthy: keep the key on the developer machine only.

Verified with codex-cli 0.164.0: `--base` cannot be combined with a custom prompt; Codex reads the review guidelines from [AGENTS.md](../AGENTS.md). Transcripts stay in `.codex-review/` (git-ignored, they contain local paths); the transcript SHA-256 in the PR lets the owner verify which local transcript a PR's evidence came from.

Tests for the generator and the gate: `node --test scripts/ci/pr-gate.test.mjs` (also run in CI).
