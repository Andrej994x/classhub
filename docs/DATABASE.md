# Database

ClassHub uses **SQL Server 2022 Express** through **EF Core 10** (`Microsoft.EntityFrameworkCore.SqlServer`). There is one database, one `ClassHubDbContext` (`src/ClassHub.Core/Infrastructure/Persistence`), and one migration history (`.../Persistence/Migrations`).

## Local connection (Windows authentication)

`src/ClassHub.Api/appsettings.Development.json` (no secret, safe to commit):

```
Server=.\SQLEXPRESS;Database=ClassHubDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True
```

- Override per machine with the environment variable `ConnectionStrings__ClassHub` or `dotnet user-secrets` (`ConnectionStrings:ClassHub`).
- No SQL Server configuration changes are needed: Mixed Mode stays off, TCP/IP stays off, no SQL logins.
- The API refuses to start without `ConnectionStrings:ClassHub` (outside Development there is no default).

**Verified on 2026-11-04 (R0-P2):** `dotnet ef database update` created `ClassHubDb` with Windows authentication. The running API connects as `EFCore/10.0.14` over **Shared memory** with `NTLM`:

```sql
SELECT DB_NAME() AS db, SUSER_SNAME() AS login, c.net_transport, c.auth_scheme, s.program_name
FROM sys.dm_exec_connections c JOIN sys.dm_exec_sessions s ON s.session_id = c.session_id
WHERE s.database_id = DB_ID('ClassHubDb');
```

Accepted local risk: in development the app runs under the developer's Windows account (sysadmin). A least-privilege runtime identity is defined together with hosting (ROADMAP D-28, B-P2).

## Migrations

```powershell
dotnet tool restore                                          # pinned dotnet-ef 10.0.14
$ef = '--project', 'src/ClassHub.Core', '--startup-project', 'src/ClassHub.Api'
dotnet tool run dotnet-ef migrations add <Name> @ef --output-dir Infrastructure/Persistence/Migrations
dotnet tool run dotnet-ef database update @ef                # apply to the local ClassHubDb
dotnet tool run dotnet-ef database update <Previous> @ef     # roll back locally
dotnet tool run dotnet-ef migrations script --idempotent @ef # review SQL
```

Rules:
- Migrations are **never applied on application startup** (D-50). Locally they are applied explicitly; automated tests migrate their own disposable databases.
- Every migration has a working `Down`. CI applies all migrations to an empty SQL Server, re-runs the idempotent script (must be a no-op), and fails if the model has changes without a migration. The integration tests revert all migrations and re-apply them.
- Generated migration files are excluded from style analyzers (`.editorconfig`, `generated_code = true`); review their SQL in the PR.
- Statements that cannot run in a transaction (for example `ALTER DATABASE`) use `migrationBuilder.Sql(..., suppressTransaction: true)`.

## Migration history (Release 0)

| Migration | Phase | Content |
|---|---|---|
| `InitialTechnical` | R0-P2 | `SchemaInfo` technical table |
| `EnableReadCommittedSnapshot` | R0-P2 | RCSI on (`suppressTransaction`) |
| `AddIdentityAndEmailOutbox` | R0-P4 | Identity tables (unique `EmailIndex`), `DataProtectionKeys`, `EmailOutbox`, `DevMailbox` |
| `AddWorkspacesTenancyAndAudit` | R0-P5 | `Workspaces`, `WorkspaceMembers`, `AuditLog`, seeded platform roles, idempotent workspace backfill for existing accounts |
| `AddSites` | R1-P3 | `Sites` (unique workspace, unique slug with `Latin1_General_100_BIN2` collation and a shape CHECK, ISJSON checks for draft/published content) |
| `AddStudents` | R2-P1 | `Students` (alternate key `(WorkspaceId, Id)`, unique filtered `(WorkspaceId, Email)`, composite tenant FK `(WorkspaceId, SourceLeadId)` → `Leads`, first-name and lowercase-email CHECKs, index for the active list), `StudentSubjects` and `StudentNotes` (composite tenant FKs to `Students`); `Leads.StudentId` with a composite tenant FK to `Students` |
| `AddGroups` | R2-P2 | `Groups` (alternate key `(WorkspaceId, Id)`, name CHECK, optional subject FK) and `GroupMembers` (key `(GroupId, StudentId)`, `RemovedAtUtc` for soft removal, composite tenant FKs `(WorkspaceId, GroupId)` → `Groups` and `(WorkspaceId, StudentId)` → `Students`) |
| `AddStudentAccounts` | R2-P3 | `Students.UserId` (FK to `AspNetUsers`; filtered unique `(WorkspaceId, UserId)`), `StudentInvitations` (unique `TokenHash` `binary(32)`, lowercase-email CHECK, composite tenant FK to `Students`, FKs to the inviting and accepting users) |
| `AddLessons` | R2-P4 | `Lessons` (alternate key `(WorkspaceId, Id)`; CHECKs for time order, kind/group consistency, mode, status, title; indexes `(WorkspaceId, StartUtc)` and `(TeacherUserId, StartUtc, EndUtc)`; composite tenant FK to `Groups`, FKs to subject and teacher) and `LessonParticipants` (key `(LessonId, StudentId)`, composite tenant FKs to `Lessons` and `Students`) |
| `AddLessonSeries` | R2-P5 | `LessonSeries` (alternate key; CHECKs for kind/student/group, mode, interval 1–2, duration, count 1–200, title; composite tenant FKs to `Students`, `Groups` and the continuing series; FKs to subject and teacher), `LessonSeriesSkips` (key `(SeriesId, SeriesIndex)`, composite tenant FK), `Lessons.SeriesId`/`SeriesIndex` (unique per series, composite tenant FK) and `SeriesException` |
| `AddBooking` | R2-P6 | `BookingSettings` (key `WorkspaceId`; CHECKs for step, notice, horizon), `WorkingHours` (CHECKs weekday and end after start), `TimeOff` (CHECK end on or after start), `BookingRequests` (unique `LeadId` and filtered `LessonId`; CHECKs time, status, mode, lesson only when confirmed/cancelled; composite tenant FKs to `Leads`, `Offers`, `Lessons`) |
| `AddFinance` | R2-P10 | `StudentPackages` (alternate key; CHECKs lessons 1–500, price, title; composite tenant FKs to `Students` and `Offers`), `LessonLedger` (CHECK on the entry shape per kind; filtered unique `(LessonId, StudentId, Occurrence)` for lesson entries and `ReversesId`; composite tenant FKs to `Students`, `StudentPackages`, `Lessons` and itself), `Payments` (CHECKs method and "negative only as a linked reversal"; filtered unique `ReversesPaymentId`; composite tenant FKs), `BalanceAdjustments` (CHECKs non-zero amount and reason) |
| `AddHomework` | R2-P9 | `Homework` (alternate key `(WorkspaceId, Id)`; CHECKs title and due date/instant together; composite tenant FK to `Lessons`), `HomeworkAssignments` (unique `(HomeworkId, StudentId)`; CHECKs status 1–3, submitted time from Submitted on, review time exactly when Reviewed, file columns all or none; filtered unique `FileId` with a composite tenant FK to `StoredFiles`; composite tenant FKs to `Homework` and `Students`) |
| `AddMaterials` | R2-P8 | `Materials` (alternate key `(WorkspaceId, Id)`; CHECKs size > 0 and title; unique `FileId` with a composite tenant FK to `StoredFiles`; FK to subject; index `(WorkspaceId, ArchivedAtUtc, UpdatedAtUtc)`), `MaterialAssignments` (CHECK exactly one of student and group; filtered unique `(MaterialId, StudentId)` and `(MaterialId, GroupId)`; composite tenant FKs to `Materials`, `Students` and `Groups`) |
| `AddAttendanceAndLessonEvents` | R2-P7 | `LessonParticipants.Attendance` (CHECK 1–3), `Lessons.PrivateNote`/`SharedSummary`, `LessonEvents` (composite tenant FK to `Lessons`; filtered unique `(LessonId, Kind)` for `LessonCompleted`; index for unprocessed events); backfills participants of lessons held before it as Present (D-22 default, no events) |
| `AddModerationAndCatalogAdmin` | R1-P11 | `AbuseReports` (target/section/test CHECK, reason and status CHECKs, resolution CHECK, indexes by status/date and workspace/date); `Sites`: `HiddenAtUtc`, `HiddenReason`, `HiddenSectionsJson` (ISJSON and hidden-pair CHECKs); `DiagnosticTests`: `HiddenAtUtc`, `HiddenReason` (hidden-pair CHECK); `IsActive` on `SubjectCategories` and `EducationLevels` (existing rows active) |
| `AddPlansAndSubscriptions` | R1-P10 | `Plans` (unique code, one default plan via a filtered unique index, seeded Free/Pro) and `PlanLimits` (PK plan + key, `0..1 000 000` CHECK, seeded D-29 values); `WorkspaceSubscriptions` (unique per workspace, period CHECK); `SubscriptionEvents` (kind CHECK, payment-required-for-activation CHECK, amount and currency CHECKs), indexes per workspace and date |
| `AddTestAttemptLeadForeignKey` | R1-P7 | Composite tenant FK `TestAttempts (WorkspaceId, LeadId)` → `Leads` (the attempt and its request stay in one workspace) |
| `AddTestAttemptsAndDiagnosticLeads` | R1-P7 | `TestAttempts` (random ids, ISJSON answers, score CHECKs, shared-requires-lead CHECK, unique `LeadId`, composite tenant FK to the test); `Leads`: type 3 `Diagnostic`, `AttemptId` with a composite tenant FK to the attempt, a type/attempt CHECK and a unique filtered index (one request per attempt) |
| `AddDiagnosticTests` | R1-P6 | `DiagnosticTests` (status/difficulty/slug/attempt CHECKs, published-date rule, unique slug per workspace with binary collation), `TestQuestions` (points 0–100, text CHECKs) and `TestOptions`, chained by composite tenant FKs |
| `AddLeads` | R1-P5 | `Leads` (type/status/name/email CHECKs, composite tenant FK to `Offers` with a title snapshot, subject FK, consent version + time, indexes per workspace and date/status) and `LeadNotes` (composite tenant FK to `Leads`) |
| `AddSitePageViews` | R1-P4 | `SitePageViews` (PK site + UTC day, `Views ≥ 0` CHECK, composite tenant FK to `Sites`): the D-32 counter, no visitor data |
| `AddOffers` | R1-P2 | `Offers` with CHECK constraints (price ≥ 0, duration, package lessons, group seats, currency), alternate key `(WorkspaceId, Id)`, subject and workspace FKs |
| `AddCatalogProfilesAndFiles` | R1-P1 | `SubjectCategories`, `Subjects`, `EducationLevels` (seeded, unique codes), `StoredFiles`, `TeacherProfiles` (unique per workspace, country check), profile subject/level/language lists with composite tenant FKs |

Since R1-P9 (no migration), the `AspNetUserTokens` values of Identity's own tokens `AuthenticatorKey` and `RecoveryCodes` are Data Protection payloads, not plain text (`ProtectedUserStore`).

## Database settings

| Setting | Value | Where |
|---|---|---|
| Collation | `Macedonian_FYROM_100_CI_AS_SC` (case-insensitive, accent-sensitive, supplementary characters) | `UseCollation` in the model; EF applies it when it creates the database |
| `READ_COMMITTED_SNAPSHOT` | `ON` | migration `EnableReadCommittedSnapshot` |
| Migration history | `__EFMigrationsHistory` | EF default |

## Modeling conventions (`ClassHubModelConventions`)

| Topic | Convention |
|---|---|
| Text | `nvarchar` with an explicit `HasMaxLength` for every string column; indexed strings ≤ 450 characters |
| Date/time | `DateTime` stored as UTC in `datetime2(3)` via `UtcDateTimeConverter`; values read back have `Kind = Utc`. Time zones (IANA) are stored separately where needed |
| Deletes | ClassHub foreign keys use `DeleteBehavior.NoAction` (SQL Server rejects multiple cascade paths; ClassHub archives instead of deleting). ASP.NET Core Identity tables keep their own behavior |
| Unique + NULL | EF adds `IS NOT NULL` filters to unique indexes on nullable columns, so many NULLs are allowed (verified by `ConventionProbeTests`) |
| Keys | `Guid` keys use EF's SQL Server sequential GUID generation (from R0-P4/P5); technical tables may use `int` identity |
| Money | `long` minor units + ISO 4217 `char(3)` currency (from R1-P2) |
| Concurrency | `rowversion` on editable aggregates; `sp_getapplock` + unique constraints for critical invariants (from R2) |
| Tenancy | every tenant-owned table has `WorkspaceId` and composite tenant foreign keys (from R0-P5) |
| Retries | no `EnableRetryOnFailure` (it conflicts with explicit transactions); reconsider with cloud hosting |

## Test databases

Automated tests **never** use the local server. Integration tests create `ClassHubTest_<guid>` databases inside a disposable Testcontainers SQL Server 2022 container (see [TESTING.md](TESTING.md)). CI's `migrations` job uses its own throwaway container.
