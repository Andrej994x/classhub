using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Groups.Domain;
using ClassHub.Core.Modules.Groups.Persistence;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Groups.Application;

/// <summary>
/// The teacher's groups (M06, R2-P2). Members are students of the same workspace (composite tenant FK in the database);
/// removing a member and archiving a group keep the rows, so lesson history stays intact.
/// </summary>
internal sealed class GroupService(ClassHubDbContext db, GroupQueries queries, ITenantContext tenant, ICatalogLookup catalog, IStudentLookup students, TimeProvider clock)
{
    public async Task<IReadOnlyList<GroupListItem>> ListAsync(bool archived, Guid? studentId, CancellationToken cancellationToken)
    {
        var groups = queries.WithArchived().Where(g => archived ? g.ArchivedAtUtc != null : g.ArchivedAtUtc == null);
        if (studentId is { } id)
        {
            groups = groups.Where(g => db.Set<GroupMember>().Any(m => m.GroupId == g.Id && m.StudentId == id && m.RemovedAtUtc == null));
        }

        return await groups.OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Select(g => new GroupListItem(g.Id, g.Name, g.SubjectId, db.Set<GroupMember>().Count(m => m.GroupId == g.Id && m.RemovedAtUtc == null), g.ArchivedAtUtc != null, g.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<GroupResponse> GetAsync(Guid groupId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await LoadAsync(groupId, tracking: false, cancellationToken), cancellationToken);

    public async Task<GroupResponse> CreateAsync(SaveGroupRequest request, CancellationToken cancellationToken)
    {
        await ThrowIfUnknownSubjectAsync(request.SubjectId, null, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            Name = request.Name!.Trim(),
            SubjectId = request.SubjectId,
            Description = Clean(request.Description),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Set<Group>().Add(group);
        await SaveAsync(cancellationToken);
        return await GetAsync(group.Id, cancellationToken);
    }

    public async Task<GroupResponse> UpdateAsync(Guid groupId, SaveGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await LoadForChangeAsync(groupId, request.Version, cancellationToken);
        await ThrowIfUnknownSubjectAsync(request.SubjectId, group.SubjectId, cancellationToken);
        group.Name = request.Name!.Trim();
        group.SubjectId = request.SubjectId;
        group.Description = Clean(request.Description);
        Touch(group);
        await SaveAsync(cancellationToken);
        return await GetAsync(group.Id, cancellationToken);
    }

    public async Task<GroupResponse> SetArchivedAsync(Guid groupId, GroupVersionRequest request, bool archived, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockMembersAsync(groupId, cancellationToken);
        var group = await LoadForChangeAsync(groupId, request.Version, cancellationToken);
        if ((group.ArchivedAtUtc is not null) != archived)
        {
            group.ArchivedAtUtc = archived ? clock.GetUtcNow().UtcDateTime : null;
            Touch(group);
            await SaveAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(group.Id, cancellationToken);
    }

    /// <summary>Adds an active student of this workspace; adding a former member reopens the membership.</summary>
    public async Task<GroupResponse> AddMemberAsync(Guid groupId, AddGroupMemberRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockMembersAsync(groupId, cancellationToken);
        var group = await LoadAsync(groupId, tracking: true, cancellationToken);
        if (group.ArchivedAtUtc is not null)
        {
            throw new BusinessRuleException("Restore the group before adding students.", ErrorCodes.InvalidStatusTransition);
        }

        var studentId = request.StudentId!.Value;
        var student = (await students.FindAsync([studentId], cancellationToken)).GetValueOrDefault(studentId)
            ?? throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["studentId"] = ["Unknown student."] });
        if (student.Archived)
        {
            throw new BusinessRuleException("Restore the student before adding them to a group.", ErrorCodes.InvalidStatusTransition);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var member = await db.Set<GroupMember>().SingleOrDefaultAsync(m => m.GroupId == group.Id && m.StudentId == studentId, cancellationToken);
        if (member is { RemovedAtUtc: null })
        {
            return await GetAsync(group.Id, cancellationToken); // already a member: nothing changes
        }

        // New and reopened memberships both take a place (checked under the members lock).
        if (await db.Set<GroupMember>().CountAsync(m => m.GroupId == group.Id && m.RemovedAtUtc == null, cancellationToken) >= Group.MaxMembers)
        {
            throw new BusinessRuleException($"A group has at most {Group.MaxMembers} students.");
        }

        if (member is null)
        {
            db.Set<GroupMember>().Add(new GroupMember { WorkspaceId = group.WorkspaceId, GroupId = group.Id, StudentId = studentId, AddedAtUtc = now });
        }
        else
        {
            member.RemovedAtUtc = null;
            member.AddedAtUtc = now;
        }

        // Membership changes are their own idempotent operations: they do not move the version of the group's details,
        // so an open details editor is not invalidated by a member added elsewhere.
        group.UpdatedAtUtc = now;
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(group.Id, cancellationToken);
    }

    /// <summary>Ends the membership (the row stays for the lesson history).</summary>
    public async Task<GroupResponse> RemoveMemberAsync(Guid groupId, Guid studentId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockMembersAsync(groupId, cancellationToken);
        var group = await LoadAsync(groupId, tracking: true, cancellationToken);
        var member = await db.Set<GroupMember>().SingleOrDefaultAsync(m => m.GroupId == group.Id && m.StudentId == studentId && m.RemovedAtUtc == null, cancellationToken)
            ?? throw new NotFoundException();
        member.RemovedAtUtc = clock.GetUtcNow().UtcDateTime;
        group.UpdatedAtUtc = member.RemovedAtUtc.Value;
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(group.Id, cancellationToken);
    }

    private void Touch(Group group)
    {
        group.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        group.Version++;
    }

    private async Task ThrowIfUnknownSubjectAsync(Guid? wanted, Guid? saved, CancellationToken cancellationToken)
    {
        // A subject the ClassHub team deactivated stays valid on the group that already has it.
        if (wanted is { } id && id != saved && (await catalog.UnknownSubjectsAsync([id], cancellationToken)).Count > 0)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["subjectId"] = ["Unknown subject."] });
        }
    }

    private async Task<Group> LoadAsync(Guid groupId, bool tracking, CancellationToken cancellationToken) =>
        await queries.WithArchived(tracking).SingleOrDefaultAsync(g => g.Id == groupId, cancellationToken) ?? throw new NotFoundException();

    private async Task<Group> LoadForChangeAsync(Guid groupId, int? version, CancellationToken cancellationToken)
    {
        var group = await LoadAsync(groupId, tracking: true, cancellationToken);
        return group.Version == version
            ? group
            : throw new ConflictException("The group was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The group was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("The student was added at the same time. Reload the group.", ErrorCodes.ConcurrencyConflict);
        }
    }

    private async Task<GroupResponse> ToResponseAsync(Group group, CancellationToken cancellationToken)
    {
        var members = await db.Set<GroupMember>().AsNoTracking().Where(m => m.GroupId == group.Id && m.RemovedAtUtc == null).ToListAsync(cancellationToken);
        var names = await students.FindAsync([.. members.Select(m => m.StudentId)], cancellationToken);
        return new GroupResponse(group.Id, group.Name, group.SubjectId, group.Description, group.ArchivedAtUtc is not null,
            [.. members.Where(m => names.ContainsKey(m.StudentId))
                .Select(m => new GroupMemberResponse(m.StudentId, names[m.StudentId].FirstName, names[m.StudentId].LastName, names[m.StudentId].Archived, m.AddedAtUtc))
                .OrderBy(m => m.LastName ?? m.FirstName).ThenBy(m => m.FirstName)],
            group.CreatedAtUtc, group.UpdatedAtUtc, group.Version);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
