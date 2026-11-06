using System.Security.Claims;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Students.Domain;
using ClassHub.Core.Modules.Students.Persistence;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Students.Application;

/// <summary>
/// The teacher's student records (M06, R2-P1). Every creation (manual, conversion, restore) runs in a transaction
/// under the workspace's student-intake lock, so the email dedupe and the active-student limit (D-29) hold under
/// parallel requests; a unique index per workspace and email backs the dedupe in the database.
/// </summary>
internal sealed class StudentService(
    ClassHubDbContext db,
    StudentQueries queries,
    ITenantContext tenant,
    ICatalogLookup catalog,
    ILeadConversion leads,
    IEntitlementService entitlements,
    IHttpContextAccessor http,
    TimeProvider clock)
{
    public const int MaxPageSize = 50;

    public async Task<StudentListResponse> ListAsync(string? q, bool archived, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 10_000 || pageSize is < 1 or > MaxPageSize)
        {
            throw new BadRequestException($"The page must be 1 to 10000 and the page size 1 to {MaxPageSize}.");
        }

        var students = queries.WithArchived().Where(s => archived ? s.ArchivedAtUtc != null : s.ArchivedAtUtc == null);
        if (q?.Trim() is { Length: > 0 } term)
        {
            students = students.Where(s => s.FirstName.Contains(term) || (s.LastName != null && s.LastName.Contains(term)) ||
                (s.Email != null && s.Email.Contains(term)) || (s.Phone != null && s.Phone.Contains(term)) ||
                (s.GuardianName != null && s.GuardianName.Contains(term)));
        }

        var total = await students.CountAsync(cancellationToken);
        var rows = await students.OrderBy(s => s.LastName ?? s.FirstName).ThenBy(s => s.FirstName).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var subjects = (await db.Set<StudentSubject>().AsNoTracking().Where(x => ids.Contains(x.StudentId)).ToListAsync(cancellationToken)).ToLookup(x => x.StudentId, x => x.SubjectId);
        var active = await db.Set<Student>().CountAsync(cancellationToken);
        return new StudentListResponse(
            [.. rows.Select(s => new StudentListItem(s.Id, s.FirstName, s.LastName, s.Email, s.Phone, s.IsMinor, [.. subjects[s.Id]], s.ArchivedAtUtc is not null, s.UpdatedAtUtc))],
            total, page, pageSize, active);
    }

    public async Task<StudentResponse> GetAsync(Guid studentId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await LoadAsync(studentId, tracking: false, cancellationToken), cancellationToken);

    public async Task<StudentResponse> CreateAsync(SaveStudentRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockIntakeAsync(tenant.RequireWorkspaceId(), cancellationToken);
        var email = StudentNames.NormalizeEmail(request.Email);
        if (email is not null && await queries.WithArchived().AnyAsync(s => s.Email == email, cancellationToken))
        {
            throw EmailTaken();
        }

        await ThrowIfUnknownSubjectsAsync(request.SubjectIds ?? [], [], cancellationToken);
        await EnsureActiveLimitAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var student = new Student { Id = Guid.CreateVersion7(), WorkspaceId = tenant.RequireWorkspaceId(), FirstName = string.Empty, CreatedAtUtc = now };
        Apply(student, request, email, now);
        db.Set<Student>().Add(student);
        SetSubjects(student, request.SubjectIds ?? [], []);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(student.Id, cancellationToken);
    }

    public async Task<StudentResponse> UpdateAsync(Guid studentId, SaveStudentRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockIntakeAsync(tenant.RequireWorkspaceId(), cancellationToken);
        var student = await LoadForChangeAsync(studentId, request.Version, cancellationToken);
        var email = StudentNames.NormalizeEmail(request.Email);
        if (email is not null && email != student.Email && await queries.WithArchived().AnyAsync(s => s.Email == email && s.Id != student.Id, cancellationToken))
        {
            throw EmailTaken();
        }

        var current = await db.Set<StudentSubject>().Where(x => x.StudentId == student.Id).ToListAsync(cancellationToken);
        // Subjects the ClassHub team deactivated stay valid where the student already has them.
        await ThrowIfUnknownSubjectsAsync(request.SubjectIds ?? [], [.. current.Select(c => c.SubjectId)], cancellationToken);
        Apply(student, request, email, clock.GetUtcNow().UtcDateTime);
        student.Version++;
        SetSubjects(student, request.SubjectIds ?? [], current);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(student.Id, cancellationToken);
    }

    /// <summary>Archive keeps the student and the history; restore counts towards the active-student limit again.</summary>
    public async Task<StudentResponse> SetArchivedAsync(Guid studentId, StudentVersionRequest request, bool archived, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockIntakeAsync(tenant.RequireWorkspaceId(), cancellationToken);
        var student = await LoadForChangeAsync(studentId, request.Version, cancellationToken);
        if ((student.ArchivedAtUtc is not null) != archived)
        {
            if (!archived)
            {
                await EnsureActiveLimitAsync(cancellationToken);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            student.ArchivedAtUtc = archived ? now : null;
            student.UpdatedAtUtc = now;
            student.Version++;
            await SaveAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(student.Id, cancellationToken);
    }

    /// <summary>
    /// D-12: idempotent. A request already converted returns its student; otherwise it is linked to the student with the
    /// same email (archived ones included) or a new student is created from it. The request is marked Converted.
    /// </summary>
    public async Task<ConvertLeadResponse> ConvertAsync(ConvertLeadRequest request, CancellationToken cancellationToken)
    {
        Guid studentId;
        bool created;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await queries.LockIntakeAsync(tenant.RequireWorkspaceId(), cancellationToken);
            var lead = await leads.FindAsync(request.LeadId!.Value, cancellationToken) ?? throw new NotFoundException();
            if (lead.StudentId is { } converted)
            {
                return new ConvertLeadResponse(await GetAsync(converted, cancellationToken), Created: false);
            }

            var email = StudentNames.NormalizeEmail(lead.Email);
            var existing = email is null ? null : await queries.WithArchived().Where(s => s.Email == email).Select(s => (Guid?)s.Id).SingleOrDefaultAsync(cancellationToken);
            if (existing is { } id)
            {
                studentId = id;
                created = false;
            }
            else
            {
                await EnsureActiveLimitAsync(cancellationToken);
                var (first, last) = StudentNames.Split(lead.Name);
                var now = clock.GetUtcNow().UtcDateTime;
                var student = new Student
                {
                    Id = Guid.CreateVersion7(),
                    WorkspaceId = tenant.RequireWorkspaceId(),
                    FirstName = first.Length > 0 ? first : email ?? "?",
                    LastName = last,
                    Email = email,
                    Phone = string.IsNullOrWhiteSpace(lead.Phone) ? null : lead.Phone.Trim(),
                    SourceLeadId = lead.Id,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.Set<Student>().Add(student);
                if (lead.SubjectId is { } subjectId)
                {
                    db.Set<StudentSubject>().Add(new StudentSubject { WorkspaceId = student.WorkspaceId, StudentId = student.Id, SubjectId = subjectId });
                }

                studentId = student.Id;
                created = true;
            }

            await leads.MarkConvertedAsync(lead.Id, studentId, cancellationToken);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new ConvertLeadResponse(await GetAsync(studentId, cancellationToken), created);
    }

    public async Task<IReadOnlyList<StudentNoteResponse>> NotesAsync(Guid studentId, CancellationToken cancellationToken)
    {
        await LoadAsync(studentId, tracking: false, cancellationToken);
        return await db.Set<StudentNote>().AsNoTracking().Where(n => n.StudentId == studentId).OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Select(n => new StudentNoteResponse(n.Id, n.Text, n.CreatedAtUtc)).ToListAsync(cancellationToken);
    }

    public async Task<StudentNoteResponse> AddNoteAsync(Guid studentId, AddStudentNoteRequest request, CancellationToken cancellationToken)
    {
        var student = await LoadAsync(studentId, tracking: false, cancellationToken);
        var note = new StudentNote
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = student.WorkspaceId,
            StudentId = student.Id,
            Text = request.Text!.Trim(),
            AuthorUserId = Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var author) ? author : null,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.Set<StudentNote>().Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return new StudentNoteResponse(note.Id, note.Text, note.CreatedAtUtc);
    }

    private Task EnsureActiveLimitAsync(CancellationToken cancellationToken) =>
        entitlements.EnsureWithinLimitAsync(PlanLimits.ActiveStudents, ct => db.Set<Student>().CountAsync(ct), adding: 1, cancellationToken);

    private static void Apply(Student student, SaveStudentRequest request, string? email, DateTime nowUtc)
    {
        student.FirstName = request.FirstName!.Trim();
        student.LastName = Clean(request.LastName);
        student.Email = email;
        student.Phone = Clean(request.Phone);
        student.IsMinor = request.IsMinor;
        student.GuardianName = Clean(request.GuardianName);
        student.GuardianEmail = StudentNames.NormalizeEmail(request.GuardianEmail);
        student.GuardianPhone = Clean(request.GuardianPhone);
        student.UpdatedAtUtc = nowUtc;
    }

    private void SetSubjects(Student student, IReadOnlyCollection<Guid> wanted, IReadOnlyCollection<StudentSubject> current)
    {
        var target = wanted.Distinct().ToHashSet();
        db.Set<StudentSubject>().RemoveRange(current.Where(c => !target.Contains(c.SubjectId)));
        db.Set<StudentSubject>().AddRange(target.Except(current.Select(c => c.SubjectId))
            .Select(id => new StudentSubject { WorkspaceId = student.WorkspaceId, StudentId = student.Id, SubjectId = id }));
    }

    private async Task ThrowIfUnknownSubjectsAsync(IReadOnlyCollection<Guid> wanted, IReadOnlyCollection<Guid> saved, CancellationToken cancellationToken)
    {
        if ((await catalog.UnknownSubjectsAsync([.. wanted.Distinct().Except(saved)], cancellationToken)).Count > 0)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["subjectIds"] = ["Unknown subject."] });
        }
    }

    private async Task<Student> LoadAsync(Guid studentId, bool tracking, CancellationToken cancellationToken) =>
        await queries.WithArchived(tracking).SingleOrDefaultAsync(s => s.Id == studentId, cancellationToken) ?? throw new NotFoundException();

    private async Task<Student> LoadForChangeAsync(Guid studentId, int? version, CancellationToken cancellationToken)
    {
        var student = await LoadAsync(studentId, tracking: true, cancellationToken);
        return student.Version == version
            ? student
            : throw new ConflictException("The student was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The student was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw EmailTaken();
        }
    }

    private async Task<StudentResponse> ToResponseAsync(Student student, CancellationToken cancellationToken)
    {
        var subjects = await db.Set<StudentSubject>().AsNoTracking().Where(x => x.StudentId == student.Id).Select(x => x.SubjectId).ToListAsync(cancellationToken);
        return new StudentResponse(student.Id, student.FirstName, student.LastName, student.Email, student.Phone, student.IsMinor, student.GuardianName,
            student.GuardianEmail, student.GuardianPhone, subjects, student.SourceLeadId, student.ArchivedAtUtc is not null, student.CreatedAtUtc, student.UpdatedAtUtc,
            student.Version);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ConflictException EmailTaken() =>
        new("Another student in your records already uses this email.", ErrorCodes.StudentEmailTaken);
}
