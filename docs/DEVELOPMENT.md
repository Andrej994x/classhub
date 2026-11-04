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

## Running locally

| Part | Command | URL |
|---|---|---|
| API + (later) Razor public pages | `dotnet run --project src/ClassHub.Api` | http://localhost:5080 (`/health`) |
| React SPA (dev server, proxies `/api` to 5080) | `cd web; npm run dev` | http://localhost:5173 |

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
| `integration` | `ci.yml` | integration tests (Testcontainers SQL Server from R0-P2) |
| `frontend` | `ci.yml` | `npm ci`, oxlint, i18n parity, Vitest, type-check + build |
| `security` | `security.yml` | gitleaks (full history), vulnerable NuGet packages, `npm audit`, dependency review |
| `codeql (csharp)`, `codeql (javascript-typescript)` | `codeql.yml` | CodeQL security-extended analysis |
| `pr-gate` | `pr-gate.yml` | branch name, Codex review section with raw output, reviewed commit in the PR, 0 unresolved critical/high |

## Independent Codex review

```powershell
git fetch origin
codex review --base origin/main *> .codex-review\<phase>-<n>.md
```

Verified with codex-cli 0.164.0: `--base` **cannot** be combined with a custom prompt (`error: the argument '--base <BRANCH>' cannot be used with '[PROMPT]'`). Codex reads the review guidelines from [AGENTS.md](../AGENTS.md) automatically. The full transcript stays in `.codex-review/` (git-ignored). Paste Codex's final review message (its findings, or the statement that there are none) into the PR body between the `codex-raw-output` markers (see the PR template).
