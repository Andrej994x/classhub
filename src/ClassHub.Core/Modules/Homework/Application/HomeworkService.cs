using System.Security.Cryptography;
using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Homework.Contracts;
using ClassHub.Core.Modules.Homework.Domain;
using ClassHub.Core.Modules.Homework.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Homework.Application;

/// <summary>
/// Homework (M10, R2-P9). The teacher creates homework and assigns it to students (directly or the current members of a
/// group); each student's copy goes Assigned → Submitted → Reviewed. Students answer with text and/or one file (the
/// document allow-list, counted against the workspace storage) and may resubmit until the review (D-41); answers after
/// the due day are flagged late. Students only ever see their own assignments; anything else is 404.
/// </summary>
internal sealed class HomeworkService(
    ClassHubDbContext db,
    HomeworkQueries queries,
    ITenantContext tenant,
    IFileStorage files,
    IStorageQuota storage,
    IStudentLookup students,
    IGroupLookup groups,
    ILessonLookup lessons,
    IWorkspaceSettings workspace,
    INotificationScheduler notifications,
    TimeProvider clock)
{
    public async Task<HomeworkListResponse> ListAsync(string? q, Guid? lessonId, bool archived, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 10_000 || pageSize is < 1 or > 100)
        {
            throw new BadRequestException("The page must be 1 to 10000 and the page size 1 to 100.");
        }

        var homework = queries.WithArchived().Where(h => archived ? h.ArchivedAtUtc != null : h.ArchivedAtUtc == null);
        if (q?.Trim() is { Length: > 0 } term)
        {
            homework = homework.Where(h => h.Title.Contains(term) || (h.Description != null && h.Description.Contains(term)));
        }

        if (lessonId is { } lesson)
        {
            homework = homework.Where(h => h.LessonId == lesson);
        }

        var assignments = db.Set<HomeworkAssignment>();
        var total = await homework.CountAsync(cancellationToken);
        var rows = await homework.OrderByDescending(h => h.UpdatedAtUtc).ThenBy(h => h.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(h => new HomeworkListItem(h.Id, h.Title, h.DueDate, h.LessonId,
                assignments.Count(a => a.HomeworkId == h.Id),
                assignments.Count(a => a.HomeworkId == h.Id && a.Status == HomeworkStatus.Submitted),
                assignments.Count(a => a.HomeworkId == h.Id && a.Status == HomeworkStatus.Reviewed),
                h.ArchivedAtUtc != null, h.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return new HomeworkListResponse(rows, total, page, pageSize);
    }

    public async Task<HomeworkResponse> GetAsync(Guid homeworkId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await LoadAsync(homeworkId, tracking: false, cancellationToken), cancellationToken);

    public async Task<HomeworkResponse> CreateAsync(SaveHomeworkRequest request, CancellationToken cancellationToken)
    {
        await ThrowIfUnknownLessonAsync(request.LessonId, null, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var homework = new HomeworkTask
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            Title = request.Title!.Trim(),
            CreatedAtUtc = now,
        };
        await ApplyAsync(homework, request, now, cancellationToken);
        db.Set<HomeworkTask>().Add(homework);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(homework.Id, cancellationToken);
    }

    public async Task<HomeworkResponse> UpdateAsync(Guid homeworkId, SaveHomeworkRequest request, CancellationToken cancellationToken)
    {
        var homework = await LoadForChangeAsync(homeworkId, request.Version, cancellationToken);
        await ThrowIfUnknownLessonAsync(request.LessonId, homework.LessonId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        await ApplyAsync(homework, request, now, cancellationToken);
        homework.Version++;
        await SaveAsync(cancellationToken);
        return await GetAsync(homework.Id, cancellationToken);
    }

    /// <summary>Archived homework leaves the lists and the portal (students can no longer submit); nothing is deleted.</summary>
    public async Task<HomeworkResponse> SetArchivedAsync(Guid homeworkId, HomeworkVersionRequest request, bool archived, CancellationToken cancellationToken)
    {
        var homework = await LoadForChangeAsync(homeworkId, request.Version, cancellationToken);
        if ((homework.ArchivedAtUtc is not null) != archived)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            homework.ArchivedAtUtc = archived ? now : null;
            homework.UpdatedAtUtc = now;
            homework.Version++;
            await SaveAsync(cancellationToken);
        }

        return await GetAsync(homework.Id, cancellationToken);
    }

    /// <summary>
    /// Assigns to the given active students and the current active members of a group; students who already have it keep
    /// their assignment (repeating changes nothing, also in parallel).
    /// </summary>
    public async Task<HomeworkResponse> AssignAsync(Guid homeworkId, AssignHomeworkRequest request, CancellationToken cancellationToken)
    {
        var homework = await LoadAsync(homeworkId, tracking: false, cancellationToken);
        if (homework.ArchivedAtUtc is not null)
        {
            throw new BusinessRuleException("Restore the homework before assigning it.", ErrorCodes.InvalidStatusTransition);
        }

        var wanted = new HashSet<Guid>(request.StudentIds ?? []);
        if (wanted.Count > 0)
        {
            var found = await students.FindAsync(wanted, cancellationToken);
            if (wanted.Any(id => !found.ContainsKey(id)))
            {
                throw Invalid("studentIds", "Unknown student.");
            }

            if (found.Values.Any(s => s.Archived))
            {
                throw new BusinessRuleException("Restore the student before assigning homework.", ErrorCodes.InvalidStatusTransition);
            }
        }

        if (request.GroupId is { } groupId)
        {
            var group = (await groups.FindAsync([groupId], cancellationToken)).GetValueOrDefault(groupId) ?? throw Invalid("groupId", "Unknown group.");
            if (group.Archived)
            {
                throw new BusinessRuleException("Restore the group before assigning homework.", ErrorCodes.InvalidStatusTransition);
            }

            wanted.UnionWith(group.ActiveStudentIds);
        }

        for (var attempt = 0; ; attempt++)
        {
            var existing = await db.Set<HomeworkAssignment>().AsNoTracking().Where(a => a.HomeworkId == homework.Id).Select(a => a.StudentId).ToListAsync(cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            var added = new List<HomeworkAssignmentRef>();
            foreach (var studentId in wanted.Except(existing))
            {
                var assignment = new HomeworkAssignment
                {
                    Id = Guid.CreateVersion7(),
                    WorkspaceId = homework.WorkspaceId,
                    HomeworkId = homework.Id,
                    StudentId = studentId,
                    AssignedAtUtc = now,
                };
                db.Set<HomeworkAssignment>().Add(assignment);
                added.Add(new HomeworkAssignmentRef(studentId, assignment.Id));
            }

            // The new students hear about it (R2-P11), in the same save as their assignment.
            await notifications.HomeworkAssignedAsync(homework.Id, added, cancellationToken);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex) && attempt < 3)
            {
                // Assigned at the same moment elsewhere: assign the rest again.
                db.ChangeTracker.Clear();
            }
        }

        return await GetAsync(homework.Id, cancellationToken);
    }

    /// <summary>Removes an assignment the student has not answered yet (an answer is kept: archive the homework instead).</summary>
    public async Task<HomeworkResponse> UnassignAsync(Guid homeworkId, Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await db.Set<HomeworkAssignment>().SingleOrDefaultAsync(a => a.Id == assignmentId && a.HomeworkId == homeworkId, cancellationToken)
            ?? throw new NotFoundException();
        if (assignment.Status != HomeworkStatus.Assigned)
        {
            throw new BusinessRuleException("The student already answered this homework; archive the homework instead.", ErrorCodes.InvalidStatusTransition);
        }

        db.Set<HomeworkAssignment>().Remove(assignment);
        await SaveAsync(cancellationToken);
        return await GetAsync(homeworkId, cancellationToken);
    }

    /// <summary>Reviews the answer the teacher looked at (its version): a resubmission in between is a conflict.</summary>
    public async Task<HomeworkResponse> ReviewAsync(Guid homeworkId, Guid assignmentId, ReviewHomeworkRequest request, CancellationToken cancellationToken)
    {
        var assignment = await db.Set<HomeworkAssignment>().SingleOrDefaultAsync(a => a.Id == assignmentId && a.HomeworkId == homeworkId, cancellationToken)
            ?? throw new NotFoundException();
        if (assignment.Version != request.Version)
        {
            throw Conflict();
        }

        HomeworkRules.Review(assignment, request.Comment, clock.GetUtcNow().UtcDateTime);
        assignment.Version++;
        await SaveAsync(cancellationToken);
        return await GetAsync(homeworkId, cancellationToken);
    }

    /// <summary>The teacher opens an answer's file.</summary>
    public async Task<PrivateFile> OpenAnswerAsync(Guid homeworkId, Guid assignmentId, CancellationToken cancellationToken)
    {
        await LoadAsync(homeworkId, tracking: false, cancellationToken);
        var assignment = await db.Set<HomeworkAssignment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == assignmentId && a.HomeworkId == homeworkId, cancellationToken)
            ?? throw new NotFoundException();
        return await OpenFileAsync(assignment, cancellationToken);
    }

    /// <summary>The student's homework (student:self): their own assignments of homework that is not archived.</summary>
    public async Task<IReadOnlyList<StudentHomeworkResponse>> ListForStudentAsync(CancellationToken cancellationToken)
    {
        var studentId = RequireStudent();
        var rows = await OwnAssignments(studentId).ToListAsync(cancellationToken);
        return [.. rows.OrderBy(r => r.Assignment.Status == HomeworkStatus.Reviewed).ThenBy(r => r.Homework.DueDate ?? DateOnly.MaxValue).ThenByDescending(r => r.Assignment.AssignedAtUtc)
            .Select(r => ToStudentResponse(r.Homework, r.Assignment))];
    }

    public async Task<StudentHomeworkResponse> GetForStudentAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var row = await OwnAssignments(RequireStudent()).SingleOrDefaultAsync(r => r.Assignment.Id == assignmentId, cancellationToken) ?? throw new NotFoundException();
        return ToStudentResponse(row.Homework, row.Assignment);
    }

    /// <summary>
    /// The student's answer: text and/or a file (a new file replaces the previous one; <paramref name="removeFile"/> drops
    /// it). Allowed until the review; the stored answer must not be empty. <paramref name="version"/> is the version the
    /// student's form showed: an answer saved or reviewed since then (another tab, the teacher) is a conflict (409).
    /// </summary>
    public async Task<StudentHomeworkResponse> SubmitAsync(Guid assignmentId, int? version, string? text, bool removeFile, string? uploadedName, byte[]? content, CancellationToken cancellationToken)
    {
        var studentId = RequireStudent();
        if (version is null)
        {
            throw Invalid("version", "Reload the page and hand in again.");
        }

        var answer = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (answer?.Length > HomeworkAssignment.AnswerMaxLength)
        {
            throw Invalid("text", $"Up to {HomeworkAssignment.AnswerMaxLength} characters.");
        }

        DocumentFileKind? kind = null;
        if (content is not null)
        {
            if (content.Length is 0 or > DocumentFiles.MaxBytes)
            {
                throw new BadRequestException("The file must be at most 20 MB.", ErrorCodes.PayloadTooLarge);
            }

            kind = DocumentFiles.Detect(uploadedName ?? string.Empty, content)
                ?? throw new BadRequestException($"This file type is not allowed. Allowed: {string.Join(", ", DocumentFiles.Extensions)}.", ErrorCodes.FileNotAllowed);
        }

        var row = await OwnAssignments(studentId).SingleOrDefaultAsync(r => r.Assignment.Id == assignmentId, cancellationToken) ?? throw new NotFoundException();
        HomeworkRules.EnsureOpen(row.Assignment); // before writing the file
        if (row.Assignment.Version != version)
        {
            throw Conflict();
        }

        var keepsFile = row.Assignment.FileId is not null && kind is null && !removeFile;
        if (answer is null && kind is null && !keepsFile)
        {
            throw Invalid("text", "Write an answer or attach a file.");
        }

        var workspaceId = tenant.RequireWorkspaceId();
        var now = clock.GetUtcNow().UtcDateTime;
        StoredFile? newFile = null;
        if (kind is not null)
        {
            var fileId = Guid.CreateVersion7();
            newFile = new StoredFile
            {
                Id = fileId,
                WorkspaceId = workspaceId,
                Purpose = HomeworkFiles.Purpose,
                ContentType = kind.ContentType,
                SizeBytes = content!.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
                StorageKey = $"{workspaceId:N}/homework/{fileId:N}{kind.Extension}",
                CreatedAtUtc = now,
            };
            await files.WriteAsync(newFile.StorageKey, content, cancellationToken);
        }

        string? oldKey = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var assignment = await db.Set<HomeworkAssignment>().SingleOrDefaultAsync(a => a.Id == assignmentId && a.StudentId == studentId, cancellationToken)
                ?? throw new NotFoundException();
            // Archived meanwhile (e.g. during the upload): no more answers. Held until the commit, so archiving waits.
            if (!await queries.LockActiveAsync(assignment.HomeworkId, workspaceId, cancellationToken))
            {
                throw new NotFoundException();
            }

            if (assignment.Version != version)
            {
                throw Conflict();
            }

            var replaced = newFile is not null || removeFile ? assignment.FileId : null;
            if (newFile is not null)
            {
                // The workspace's storage (shared plan lock); the replaced answer file is freed by this transaction.
                await storage.EnsureRoomAsync(newFile.SizeBytes, async ct => replaced is { } id
                    ? await db.Set<StoredFile>().AsNoTracking().Where(f => f.Id == id).Select(f => f.SizeBytes).SingleAsync(ct)
                    : 0, cancellationToken);
                db.Set<StoredFile>().Add(newFile);
            }

            HomeworkRules.Submit(assignment, now);
            assignment.AnswerText = answer;
            if (newFile is not null)
            {
                assignment.FileId = newFile.Id;
                assignment.FileName = DocumentFiles.SafeName(uploadedName, kind!.Extension);
                assignment.ContentType = newFile.ContentType;
                assignment.SizeBytes = newFile.SizeBytes;
            }
            else if (removeFile)
            {
                assignment.FileId = null;
                assignment.FileName = null;
                assignment.ContentType = null;
                assignment.SizeBytes = null;
            }

            assignment.Version++;
            await SaveAsync(cancellationToken);
            if (replaced is { } oldId)
            {
                var old = await db.Set<StoredFile>().SingleAsync(f => f.Id == oldId, cancellationToken);
                oldKey = old.StorageKey;
                db.Set<StoredFile>().Remove(old);
                await SaveAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (newFile is not null)
            {
                await files.DeleteAsync(newFile.StorageKey, CancellationToken.None);
            }

            throw;
        }

        if (oldKey is not null)
        {
            // After the commit (best effort): an orphaned blob is harmless, a missing one is not.
            try
            {
                await files.DeleteAsync(oldKey, CancellationToken.None);
            }
            catch (IOException)
            {
            }
        }

        return await GetForStudentAsync(assignmentId, cancellationToken);
    }

    /// <summary>The student opens their own answer's file.</summary>
    public async Task<PrivateFile> OpenOwnAnswerAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var row = await OwnAssignments(RequireStudent()).SingleOrDefaultAsync(r => r.Assignment.Id == assignmentId, cancellationToken) ?? throw new NotFoundException();
        return await OpenFileAsync(row.Assignment, cancellationToken);
    }

    private sealed class OwnRow
    {
        public required HomeworkTask Homework { get; init; }

        public required HomeworkAssignment Assignment { get; init; }
    }

    /// <summary>The student's own assignments; the archive filter hides archived homework.</summary>
    private IQueryable<OwnRow> OwnAssignments(Guid studentId) =>
        from a in db.Set<HomeworkAssignment>().AsNoTracking()
        join h in db.Set<HomeworkTask>().AsNoTracking() on a.HomeworkId equals h.Id
        where a.StudentId == studentId
        select new OwnRow { Homework = h, Assignment = a };

    private async Task<PrivateFile> OpenFileAsync(HomeworkAssignment assignment, CancellationToken cancellationToken)
    {
        if (assignment.FileId is not { } fileId)
        {
            throw new NotFoundException();
        }

        var key = await db.Set<StoredFile>().AsNoTracking().Where(f => f.Id == fileId).Select(f => f.StorageKey).SingleAsync(cancellationToken);
        var stream = await files.OpenReadAsync(key, cancellationToken) ?? throw new NotFoundException();
        return new PrivateFile(stream, assignment.ContentType!, assignment.FileName!, DocumentUploads.IsInline(assignment.ContentType!));
    }

    private async Task ApplyAsync(HomeworkTask homework, SaveHomeworkRequest request, DateTime now, CancellationToken cancellationToken)
    {
        homework.Title = request.Title!.Trim();
        homework.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        homework.LessonId = request.LessonId;
        if (request.DueDate != homework.DueDate || (request.DueDate is not null && homework.DueAtUtc is null))
        {
            homework.DueDate = request.DueDate;
            homework.DueAtUtc = request.DueDate is { } due
                ? HomeworkRules.DueAtUtc(due, TimeZoneInfo.FindSystemTimeZoneById(await workspace.GetTimeZoneAsync(cancellationToken)))
                : null;
        }

        homework.UpdatedAtUtc = now;
    }

    private async Task ThrowIfUnknownLessonAsync(Guid? wanted, Guid? saved, CancellationToken cancellationToken)
    {
        if (wanted is { } id && id != saved && !(await lessons.FindAsync([id], cancellationToken)).ContainsKey(id))
        {
            throw Invalid("lessonId", "Unknown lesson.");
        }
    }

    private Guid RequireStudent() => tenant.StudentId ?? throw new InvalidOperationException("Not a student request.");

    private async Task<HomeworkTask> LoadAsync(Guid homeworkId, bool tracking, CancellationToken cancellationToken) =>
        await queries.WithArchived(tracking).SingleOrDefaultAsync(h => h.Id == homeworkId, cancellationToken) ?? throw new NotFoundException();

    private async Task<HomeworkTask> LoadForChangeAsync(Guid homeworkId, int? version, CancellationToken cancellationToken)
    {
        var homework = await LoadAsync(homeworkId, tracking: true, cancellationToken);
        return homework.Version == version ? homework : throw Conflict();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict();
        }
    }

    private async Task<HomeworkResponse> ToResponseAsync(HomeworkTask homework, CancellationToken cancellationToken)
    {
        var assignments = await db.Set<HomeworkAssignment>().AsNoTracking().Where(a => a.HomeworkId == homework.Id).ToListAsync(cancellationToken);
        var names = await students.FindAsync([.. assignments.Select(a => a.StudentId)], cancellationToken);
        HomeworkLessonResponse? lesson = null;
        if (homework.LessonId is { } lessonId && (await lessons.FindAsync([lessonId], cancellationToken)).TryGetValue(lessonId, out var found))
        {
            lesson = new HomeworkLessonResponse(found.Id, found.Title, found.StartUtc, found.TimeZone);
        }

        string Name(Guid id) => names.TryGetValue(id, out var s) ? string.Join(' ', new[] { s.FirstName, s.LastName }.Where(n => !string.IsNullOrEmpty(n))) : string.Empty;
        return new HomeworkResponse(homework.Id, homework.Title, homework.Description, homework.DueDate, homework.DueAtUtc, lesson, homework.ArchivedAtUtc is not null,
            [.. assignments.OrderBy(a => Name(a.StudentId), StringComparer.CurrentCulture).Select(a => new HomeworkAssignmentResponse(
                a.Id, a.StudentId, Name(a.StudentId), names.TryGetValue(a.StudentId, out var s) && s.Archived, a.Status.ToString(), a.AssignedAtUtc, a.SubmittedAtUtc,
                HomeworkRules.IsLate(a.SubmittedAtUtc, homework.DueAtUtc), a.AnswerText, FileOf(a), a.ReviewedAtUtc, a.TeacherComment, a.Version))],
            homework.CreatedAtUtc, homework.UpdatedAtUtc, homework.Version);
    }

    private static StudentHomeworkResponse ToStudentResponse(HomeworkTask homework, HomeworkAssignment a) =>
        new(a.Id, homework.Title, homework.Description, homework.DueDate, a.Status.ToString(), a.AssignedAtUtc, a.SubmittedAtUtc,
            HomeworkRules.IsLate(a.SubmittedAtUtc, homework.DueAtUtc), a.AnswerText, FileOf(a), a.ReviewedAtUtc,
            a.Status == HomeworkStatus.Reviewed ? a.TeacherComment : null, a.Version);

    private static HomeworkFileResponse? FileOf(HomeworkAssignment a) =>
        a.FileId is null ? null : new HomeworkFileResponse(a.FileName!, a.ContentType!, a.SizeBytes ?? 0);

    private static ConflictException Conflict() =>
        new("The homework was changed elsewhere. Reload and try again.", ErrorCodes.ConcurrencyConflict);

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
