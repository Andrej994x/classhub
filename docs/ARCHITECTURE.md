# Architecture

This document describes the architecture **as implemented**. The target design is in [ROADMAP.md](ROADMAP.md) sections B–C; this file is updated as phases land.

## Current state (R0-P1)

```
ClassHub.slnx
├─ src/ClassHub.Api      ASP.NET Core host: GET /health (anonymous), Serilog console logging
├─ src/ClassHub.Core     module library: IModule contract, CoreAssembly marker (no modules yet)
├─ tests/ClassHub.UnitTests           xUnit v3 + Shouldly
├─ tests/ClassHub.ArchitectureTests   NetArchTest: Core must not depend on the Api host
├─ tests/ClassHub.IntegrationTests    WebApplicationFactory<Program>: /health
└─ web/                  React 19 + TypeScript + Vite 8 + React Router 8 + Tailwind v4; Vitest; oxlint
```

## Principles (from ROADMAP)

- **Modular monolith:** `ClassHub.Api` is only the composition root. Business modules live in `ClassHub.Core/Modules/<Name>/` and implement `IModule`. A module may use another module only through its `Contracts` namespace (enforced by architecture tests from R0-P3).
- **Single origin:** `/api/*` (REST), SPA routes (`/app`, `/student`, `/admin`, auth pages) and Razor public pages (`/`, `/{slug}`, …) are all served by one ASP.NET Core app (from R0-P6 / R1-P4).
- **Tenant isolation:** each teacher's workspace is the tenant boundary; layered isolation per ROADMAP C.7 (from R0-P5).
- **Contracts:** the OpenAPI document is generated at build time and turned into TypeScript types for the SPA (from R0-P3).

## Toolchain decisions taken during implementation

| Topic | Decision | Reason |
|---|---|---|
| Test runner | Microsoft.Testing.Platform (opt-in in `global.json`); no `Microsoft.NET.Test.Sdk`/VSTest adapter | xunit.v3 4.x on the .NET 10 SDK no longer supports the VSTest target |
| Restore | Central package management + `packages.lock.json`, CI uses `--locked-mode` | Reproducible builds |
| Package source | `NuGet.config` clears sources and uses nuget.org only | No machine-specific feeds |
