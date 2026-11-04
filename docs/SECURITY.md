# Security

How ClassHub protects accounts, tenants and student data **as implemented**. The target design is [ROADMAP.md](ROADMAP.md) B.5 and C.6–C.8; the threat model is completed in R0-P8.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting on this repository. Do not open a public issue.

## Authentication (R0-P4)

ASP.NET Core Identity with cookie sessions. See [ARCHITECTURE.md](ARCHITECTURE.md#accounts-and-sessions-r0-p4): PBKDF2 hashes, lockout, no account enumeration, security-stamp revalidation (60 s), Data Protection keys in the database, tokens only in URL fragments, antiforgery tokens bound to the signed-in user.

## Authorization: deny by default

- Fallback policy: authenticated user. It also applies to requests that match no endpoint.
- Every endpoint declares `AllowAnonymous` or a **named** policy (`EndpointAuthorizationMetadataTests`).
- Policies: `Authenticated`; workspace policies `ws:<permission>` (for example `ws:workspace.read`). Permissions are code constants (`WorkspacePermissions`), mapped from workspace roles (`WorkspaceRolePermissions`; today `Owner` has all workspace permissions). Unknown permission names fail when the policy is resolved.
- Platform roles (`PlatformOwner`, `PlatformAdmin`) are Identity roles. **They grant no workspace access**: a platform admin without membership gets 404 on workspace routes (tested). The first Platform Owner is created only with the local CLI `dotnet run --project src/ClassHub.Api -- grant-platform-owner <email> [--force-add]` (audited, never exposed over HTTP). The owner check, the grant, the session revocation and the audit entry run in one transaction under an application lock (`sp_getapplock`), so concurrent bootstraps cannot both add an owner without `--force-add`.

## Tenant isolation (R0-P5)

A teacher's **workspace** is the tenant. Layers (each tested):

| # | Layer | Implementation | Test |
|---|---|---|---|
| 1 | Routing + policy | Workspace APIs live under `/api/w/{workspaceId:guid}`; `WorkspacePermissionHandler` checks an active membership and the role's permission | `WorkspaceTenancyTests` |
| 2 | Context | The handler binds the request to the verified workspace (`TenantContext.Set`, once; a request cannot switch workspaces). Never read from headers or bodies | `TenancyUnitTests` |
| 3 | Reads | EF Core 10 named query filter `Tenant` on every `ITenantOwned` entity: `WorkspaceId == CurrentTenantId`; **no tenant → no rows** | `TenancyProbeTests`, `WorkspaceTenancyTests` |
| 4 | Writes | `TenantSaveChangesInterceptor`: inserts are stamped with (or must match) the current workspace; updates/deletes must target it; `WorkspaceId` never changes; outside a workspace request new rows need an explicit WorkspaceId | `TenancyProbeTests` |
| 5 | Database | Composite tenant FKs `(WorkspaceId, ParentId) → (WorkspaceId, Id)` via `HasTenantReference`; the database rejects cross-workspace links even if code is wrong | `TenancyProbeTests` |
| 6 | Bypass control | `IgnoreQueryFilters` and raw SQL only in allow-listed files (`*Queries.cs` in module `Persistence`, migrations, the outbox dispatcher, `Jobs/`) | `TenantBypassTests` |
| 7 | Output | Explicit response DTOs; entities are never serialized | review |
| 8 | Tests | `TenantIsolationKit.AssertCrossTenantDeniedAsync`: another teacher gets **404 `NOT_FOUND`** (never 403) and changes nothing; every new tenant resource adds these tests | `WorkspaceTenancyTests` |
| 9 | Audit | Append-only `AuditLog` (actor, action, target, workspace, before/after JSON, request id) for workspace creation, settings changes and platform role grants; no secrets or emails in details | `WorkspaceTenancyTests` |

Registration provisions the workspace and Owner membership **in the same transaction** as the account and its confirmation email (tested: a failing provisioning leaves no account and no email).

## Logging and errors

Secrets are redacted and emails masked in logs; error responses never contain exception details (R0-P3, see ARCHITECTURE.md).

## Accepted local risks

- Development runs as the developer's Windows account (sysadmin) against `.\SQLEXPRESS`; a least-privilege runtime identity comes with hosting (D-28).
- Codex reviews run locally; CI checks the generated, commit-bound evidence but cannot execute Codex itself.
