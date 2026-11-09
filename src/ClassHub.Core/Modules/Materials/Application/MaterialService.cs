using System.Security.Cryptography;
using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Materials.Contracts;
using ClassHub.Core.Modules.Materials.Domain;
using ClassHub.Core.Modules.Materials.Persistence;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Materials.Application;

/// <summary>An opened material file: stream, type, safe name and whether browsers may show it inline.</summary>
internal sealed record MaterialFile(Stream Content, string ContentType, string FileName, bool Inline);

/// <summary>
/// Teaching materials (M10, R2-P8). Uploads are checked against the allow-list by extension and signature and counted
/// against the plan's storage (all of the workspace's stored files) under the plan lock. Files are private: the teacher
/// and the students they are assigned to, directly or through a group, can open them; everyone else gets 404.
/// </summary>
internal sealed class MaterialService(
    ClassHubDbContext db,
    MaterialQueries queries,
    ITenantContext tenant,
    IFileStorage files,
    IStorageQuota storage,
    ICatalogLookup catalog,
    IStudentLookup students,
    IGroupLookup groups,
    TimeProvider clock)
{
    public async Task<MaterialListResponse> ListAsync(string? q, Guid? subjectId, bool archived, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 10_000 || pageSize is < 1 or > 100)
        {
            throw new BadRequestException("The page must be 1 to 10000 and the page size 1 to 100.");
        }

        var materials = queries.WithArchived().Where(m => archived ? m.ArchivedAtUtc != null : m.ArchivedAtUtc == null);
        if (q?.Trim() is { Length: > 0 } term)
        {
            materials = materials.Where(m => m.Title.Contains(term) || (m.Description != null && m.Description.Contains(term)) || m.FileName.Contains(term));
        }

        if (subjectId is { } subject)
        {
            materials = materials.Where(m => m.SubjectId == subject);
        }

        var total = await materials.CountAsync(cancellationToken);
        var rows = await materials.OrderByDescending(m => m.UpdatedAtUtc).ThenBy(m => m.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MaterialListItem(m.Id, m.Title, m.SubjectId, m.FileName, m.ContentType, m.SizeBytes,
                db.Set<MaterialAssignment>().Count(a => a.MaterialId == m.Id), m.ArchivedAtUtc != null, m.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return new MaterialListResponse(rows, total, page, pageSize, await storage.UsedBytesAsync(cancellationToken));
    }

    public async Task<MaterialResponse> GetAsync(Guid materialId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await LoadAsync(materialId, tracking: false, cancellationToken), cancellationToken);

    /// <summary>
    /// Stores a new material. The blob is written first and removed again if anything after it fails; the quota check
    /// (all stored files of the workspace plus this one) and the rows commit together under the plan lock.
    /// </summary>
    public async Task<MaterialResponse> CreateAsync(string? title, string? description, Guid? subjectId, string? uploadedName, byte[] content, CancellationToken cancellationToken)
    {
        var cleanTitle = title?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (cleanTitle.Length is < 1 or > Material.TitleMaxLength)
        {
            errors["title"] = [$"Enter a title (up to {Material.TitleMaxLength} characters)."];
        }

        if (description?.Trim().Length > Material.DescriptionMaxLength)
        {
            errors["description"] = [$"Up to {Material.DescriptionMaxLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        if (content.Length is 0 or > MaterialFiles.MaxBytes)
        {
            throw new BadRequestException("The file must be at most 20 MB.", ErrorCodes.PayloadTooLarge);
        }

        var kind = MaterialFiles.Detect(uploadedName ?? string.Empty, content)
            ?? throw new BadRequestException($"This file type is not allowed. Allowed: {string.Join(", ", MaterialFiles.Extensions)}.", ErrorCodes.FileNotAllowed);
        await ThrowIfUnknownSubjectAsync(subjectId, null, cancellationToken);

        var workspaceId = tenant.RequireWorkspaceId();
        var now = clock.GetUtcNow().UtcDateTime;
        var fileId = Guid.CreateVersion7();
        var key = $"{workspaceId:N}/materials/{fileId:N}{kind.Extension}";
        var material = new Material
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            Title = cleanTitle,
            Description = Clean(description),
            SubjectId = subjectId,
            FileId = fileId,
            FileName = MaterialFiles.SafeName(uploadedName, kind.Extension),
            ContentType = kind.ContentType,
            SizeBytes = content.Length,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        await files.WriteAsync(key, content, cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // Everything the workspace stores plus this file, under the plan lock shared with every file writer.
            await storage.EnsureRoomAsync(content.Length, null, cancellationToken);
            db.Set<StoredFile>().Add(new StoredFile
            {
                Id = fileId,
                WorkspaceId = workspaceId,
                Purpose = MaterialFiles.Purpose,
                ContentType = kind.ContentType,
                SizeBytes = content.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
                StorageKey = key,
                CreatedAtUtc = now,
            });
            db.Set<Material>().Add(material);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        return await GetAsync(material.Id, cancellationToken);
    }

    public async Task<MaterialResponse> UpdateAsync(Guid materialId, UpdateMaterialRequest request, CancellationToken cancellationToken)
    {
        var material = await LoadForChangeAsync(materialId, request.Version, cancellationToken);
        await ThrowIfUnknownSubjectAsync(request.SubjectId, material.SubjectId, cancellationToken);
        material.Title = request.Title!.Trim();
        material.Description = Clean(request.Description);
        material.SubjectId = request.SubjectId;
        Touch(material);
        await SaveAsync(cancellationToken);
        return await GetAsync(material.Id, cancellationToken);
    }

    /// <summary>Archived materials leave the lists and the portal; nothing is deleted.</summary>
    public async Task<MaterialResponse> SetArchivedAsync(Guid materialId, MaterialVersionRequest request, bool archived, CancellationToken cancellationToken)
    {
        var material = await LoadForChangeAsync(materialId, request.Version, cancellationToken);
        if ((material.ArchivedAtUtc is not null) != archived)
        {
            material.ArchivedAtUtc = archived ? clock.GetUtcNow().UtcDateTime : null;
            Touch(material);
            await SaveAsync(cancellationToken);
        }

        return await GetAsync(material.Id, cancellationToken);
    }

    /// <summary>Deletes the material, its assignments and its file (frees the storage).</summary>
    public async Task DeleteAsync(Guid materialId, MaterialVersionRequest request, CancellationToken cancellationToken)
    {
        string key;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var material = await LoadForChangeAsync(materialId, request.Version, cancellationToken);
            var file = await db.Set<StoredFile>().SingleAsync(f => f.Id == material.FileId, cancellationToken);
            key = file.StorageKey;
            db.Set<MaterialAssignment>().RemoveRange(await db.Set<MaterialAssignment>().Where(a => a.MaterialId == material.Id).ToListAsync(cancellationToken));
            db.Set<Material>().Remove(material);
            await SaveAsync(cancellationToken);
            db.Set<StoredFile>().Remove(file);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // After the commit (best effort): an orphaned blob is harmless, a missing one is not.
        try
        {
            await files.DeleteAsync(key, CancellationToken.None);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Assigns to an active student or group of this workspace; assigning twice changes nothing.</summary>
    public async Task<MaterialResponse> AssignAsync(Guid materialId, AssignMaterialRequest request, CancellationToken cancellationToken)
    {
        var material = await LoadAsync(materialId, tracking: false, cancellationToken);
        if (material.ArchivedAtUtc is not null)
        {
            throw new BusinessRuleException("Restore the material before assigning it.", ErrorCodes.InvalidStatusTransition);
        }

        if (request.StudentId is { } studentId)
        {
            var student = (await students.FindAsync([studentId], cancellationToken)).GetValueOrDefault(studentId) ?? throw Invalid("studentId", "Unknown student.");
            if (student.Archived)
            {
                throw new BusinessRuleException("Restore the student before assigning materials.", ErrorCodes.InvalidStatusTransition);
            }

            if (await db.Set<MaterialAssignment>().AnyAsync(a => a.MaterialId == material.Id && a.StudentId == studentId, cancellationToken))
            {
                return await GetAsync(material.Id, cancellationToken);
            }
        }
        else
        {
            var groupId = request.GroupId!.Value;
            var group = (await groups.FindAsync([groupId], cancellationToken)).GetValueOrDefault(groupId) ?? throw Invalid("groupId", "Unknown group.");
            if (group.Archived)
            {
                throw new BusinessRuleException("Restore the group before assigning materials.", ErrorCodes.InvalidStatusTransition);
            }

            if (await db.Set<MaterialAssignment>().AnyAsync(a => a.MaterialId == material.Id && a.GroupId == groupId, cancellationToken))
            {
                return await GetAsync(material.Id, cancellationToken);
            }
        }

        db.Set<MaterialAssignment>().Add(new MaterialAssignment
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = material.WorkspaceId,
            MaterialId = material.Id,
            StudentId = request.StudentId,
            GroupId = request.StudentId is null ? request.GroupId : null,
            AssignedAtUtc = clock.GetUtcNow().UtcDateTime,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            // Assigned at the same moment elsewhere: the assignment exists, which is what was asked for.
            db.ChangeTracker.Clear();
        }

        return await GetAsync(material.Id, cancellationToken);
    }

    public async Task<MaterialResponse> UnassignAsync(Guid materialId, Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await db.Set<MaterialAssignment>().SingleOrDefaultAsync(a => a.Id == assignmentId && a.MaterialId == materialId, cancellationToken)
            ?? throw new NotFoundException();
        db.Set<MaterialAssignment>().Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(materialId, cancellationToken);
    }

    /// <summary>The teacher's download (archived materials included).</summary>
    public async Task<MaterialFile> OpenAsync(Guid materialId, CancellationToken cancellationToken) =>
        await OpenFileAsync(await LoadAsync(materialId, tracking: false, cancellationToken), cancellationToken);

    /// <summary>The student's materials (student:self): assigned to them or to one of their current groups, not archived.</summary>
    public async Task<IReadOnlyList<StudentMaterialResponse>> ListForStudentAsync(CancellationToken cancellationToken)
    {
        var (studentId, groupIds) = await StudentScopeAsync(cancellationToken);
        var assignments = await db.Set<MaterialAssignment>().AsNoTracking()
            .Where(a => a.StudentId == studentId || (a.GroupId != null && groupIds.Contains(a.GroupId.Value)))
            .Select(a => new { a.MaterialId, a.AssignedAtUtc })
            .ToListAsync(cancellationToken);
        var ids = assignments.Select(a => a.MaterialId).Distinct().ToList();
        var materials = await db.Set<Material>().AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(cancellationToken);
        return [.. materials
            .Select(m => new StudentMaterialResponse(m.Id, m.Title, m.Description, m.SubjectId, m.FileName, m.ContentType, m.SizeBytes,
                assignments.Where(a => a.MaterialId == m.Id).Min(a => a.AssignedAtUtc)))
            .OrderByDescending(m => m.AssignedAtUtc)];
    }

    /// <summary>The student's download: only an assigned, not archived material; anything else is not found.</summary>
    public async Task<MaterialFile> OpenForStudentAsync(Guid materialId, CancellationToken cancellationToken)
    {
        var (studentId, groupIds) = await StudentScopeAsync(cancellationToken);
        var material = await db.Set<Material>().AsNoTracking()
            .Where(m => m.Id == materialId && db.Set<MaterialAssignment>().Any(a => a.MaterialId == m.Id
                && (a.StudentId == studentId || (a.GroupId != null && groupIds.Contains(a.GroupId.Value)))))
            .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException();
        return await OpenFileAsync(material, cancellationToken);
    }

    private async Task<(Guid StudentId, List<Guid> GroupIds)> StudentScopeAsync(CancellationToken cancellationToken)
    {
        var studentId = tenant.StudentId ?? throw new InvalidOperationException("Not a student request.");
        return (studentId, [.. await groups.ActiveGroupsOfStudentAsync(studentId, cancellationToken)]);
    }

    private async Task<MaterialFile> OpenFileAsync(Material material, CancellationToken cancellationToken)
    {
        var key = await db.Set<StoredFile>().AsNoTracking().Where(f => f.Id == material.FileId).Select(f => f.StorageKey).SingleAsync(cancellationToken);
        var stream = await files.OpenReadAsync(key, cancellationToken) ?? throw new NotFoundException();
        // Only images may be shown in the browser; everything else is downloaded (never rendered inline).
        return new MaterialFile(stream, material.ContentType, material.FileName, material.ContentType.StartsWith("image/", StringComparison.Ordinal));
    }

    private async Task ThrowIfUnknownSubjectAsync(Guid? wanted, Guid? saved, CancellationToken cancellationToken)
    {
        if (wanted is { } id && id != saved && (await catalog.UnknownSubjectsAsync([id], cancellationToken)).Count > 0)
        {
            throw Invalid("subjectId", "Unknown subject.");
        }
    }

    private void Touch(Material material)
    {
        material.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        material.Version++;
    }

    private async Task<Material> LoadAsync(Guid materialId, bool tracking, CancellationToken cancellationToken) =>
        await queries.WithArchived(tracking).SingleOrDefaultAsync(m => m.Id == materialId, cancellationToken) ?? throw new NotFoundException();

    private async Task<Material> LoadForChangeAsync(Guid materialId, int? version, CancellationToken cancellationToken)
    {
        var material = await LoadAsync(materialId, tracking: true, cancellationToken);
        return material.Version == version ? material : throw Conflict();
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

    private async Task<MaterialResponse> ToResponseAsync(Material material, CancellationToken cancellationToken)
    {
        var assignments = await db.Set<MaterialAssignment>().AsNoTracking().Where(a => a.MaterialId == material.Id).OrderBy(a => a.AssignedAtUtc).ToListAsync(cancellationToken);
        var studentNames = await students.FindAsync([.. assignments.Where(a => a.StudentId is not null).Select(a => a.StudentId!.Value)], cancellationToken);
        var groupNames = await groups.FindAsync([.. assignments.Where(a => a.GroupId is not null).Select(a => a.GroupId!.Value)], cancellationToken);
        return new MaterialResponse(material.Id, material.Title, material.Description, material.SubjectId, material.FileName, material.ContentType, material.SizeBytes,
            material.ArchivedAtUtc is not null,
            [.. assignments.Select(a => new MaterialAssignmentResponse(a.Id, a.StudentId, a.GroupId,
                a.StudentId is { } s && studentNames.TryGetValue(s, out var student) ? string.Join(' ', new[] { student.FirstName, student.LastName }.Where(n => !string.IsNullOrEmpty(n)))
                : a.GroupId is { } g && groupNames.TryGetValue(g, out var group) ? group.Name : string.Empty,
                a.AssignedAtUtc))],
            material.CreatedAtUtc, material.UpdatedAtUtc, material.Version);
    }

    private static ConflictException Conflict() =>
        new("The material was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
