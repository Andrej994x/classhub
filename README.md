# ClassHub

ClassHub is a SaaS platform for private teachers, tutors, professors and small education centers. A teacher can run their teaching business from one place: a public website, offers, leads, diagnostic tests, students and groups, scheduling, lessons, materials and homework, packages and payment tracking, and a teaching toolbox. The interface is in Macedonian and English.

> **Status:** Release 0 (Foundation) in progress. There is no application code yet. See [docs/PROGRESS.md](docs/PROGRESS.md).

## Stack

- **Backend:** ASP.NET Core Web API on .NET 10 LTS (C#), modular monolith, ASP.NET Core Identity (cookie sessions), policy-based authorization
- **Database:** SQL Server 2022 Express, EF Core migrations
- **Frontend:** React + TypeScript + Vite + React Router, Tailwind CSS + shadcn/ui, React Hook Form + Zod, TanStack Query; Razor Pages for public teacher sites (SEO)
- **Testing:** xUnit, Vitest, Playwright; GitHub Actions; independent Codex CLI review

## Documentation

- [docs/PRODUCT_SCOPE.md](docs/PRODUCT_SCOPE.md): business requirements (source of truth for functionality)
- [docs/ROADMAP.md](docs/ROADMAP.md): the approved master architecture and implementation plan
- [docs/PROGRESS.md](docs/PROGRESS.md): live implementation status
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): prerequisites, setup, commands, CI checks, workflow
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): architecture as implemented
- [docs/RECOVERY.md](docs/RECOVERY.md): resuming and repairing the workflow
- [AGENTS.md](AGENTS.md) / [CLAUDE.md](CLAUDE.md): rules for AI agents, including the Codex review guidelines

## Quick start

```powershell
dotnet tool restore
dotnet build ClassHub.slnx
dotnet test --solution ClassHub.slnx
dotnet run --project src/ClassHub.Api        # http://localhost:5080/health
cd web; npm ci; npm run dev                  # http://localhost:5173
```

## License

Copyright (c) 2026 Andrej Nestorov. **All rights reserved.**
This repository is public for transparency only. No license is granted to use, copy, modify or distribute the code.
