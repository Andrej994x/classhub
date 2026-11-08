using System.Security.Cryptography;
using System.Text;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Students.Domain;
using ClassHub.Core.Modules.Students.Persistence;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Students.Application;

/// <summary>
/// Student accounts (R2-P3): the teacher invites a student by email; the student accepts with a new account or links
/// an existing one (one account can be a student of several teachers, D-16). Every change to a student's account link
/// runs under the workspace's student-intake lock, so an invitation is used once and a student gets one account.
/// </summary>
internal sealed class StudentAccountService(
    ClassHubDbContext db,
    StudentQueries queries,
    StudentAccountQueries accounts,
    StudentService students,
    ITenantContext tenant,
    IInvitedAccounts identity,
    IWorkspaceNames workspaces,
    IEmailOutbox outbox,
    IAuditLog audit,
    IOptions<AppLinkOptions> links,
    TimeProvider clock)
{
    public async Task<StudentResponse> InviteAsync(Guid studentId, InviteStudentRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = tenant.RequireWorkspaceId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockIntakeAsync(workspaceId, cancellationToken);
        var student = await queries.WithArchived(tracking: true).SingleOrDefaultAsync(s => s.Id == studentId, cancellationToken) ?? throw new NotFoundException();
        if (student.ArchivedAtUtc is not null)
        {
            throw new BusinessRuleException("Restore the student before inviting them.", ErrorCodes.InvalidStatusTransition);
        }

        if (student.UserId is not null)
        {
            throw new ConflictException("The student already has an account.", ErrorCodes.StudentAlreadyLinked);
        }

        var email = StudentNames.NormalizeEmail(request.Email ?? student.Email)
            ?? throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["email"] = ["Enter the email to send the invitation to."] });

        var now = clock.GetUtcNow().UtcDateTime;
        var since = now - StudentInvitation.SendWindow;
        if (await db.Set<StudentInvitation>().CountAsync(i => i.StudentId == student.Id && i.CreatedAtUtc > since, cancellationToken) >= StudentInvitation.MaxPerWindow)
        {
            throw new TooManyRequestsException("Too many invitations for this student today. Try again tomorrow.");
        }

        await RevokePendingAsync(db, student.Id, now, cancellationToken);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(StudentInvitation.TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.Set<StudentInvitation>().Add(new StudentInvitation
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            StudentId = student.Id,
            Email = email,
            TokenHash = Hash(token),
            CreatedByUserId = tenant.UserId!.Value,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + StudentInvitation.ValidFor,
        });

        var workspace = (await workspaces.FindAsync([workspaceId], cancellationToken))[workspaceId];
        var link = links.Value.PublicBaseUrl.TrimEnd('/') + "/student/invitation#token=" + token;
        outbox.Enqueue(StudentEmails.Invitation(email, workspace.Locale, workspace.Name, student.FirstName, student.IsMinor, link));
        audit.Record("student.invited", "Student", student.Id.ToString(), workspaceId, actorUserId: tenant.UserId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await students.GetAsync(student.Id, cancellationToken);
    }

    public async Task<StudentResponse> RevokeInvitationAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var workspaceId = tenant.RequireWorkspaceId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockIntakeAsync(workspaceId, cancellationToken);
        var student = await queries.WithArchived().SingleOrDefaultAsync(s => s.Id == studentId, cancellationToken) ?? throw new NotFoundException();
        if (await RevokePendingAsync(db, student.Id, clock.GetUtcNow().UtcDateTime, cancellationToken) > 0)
        {
            audit.Record("student.invitation-revoked", "Student", student.Id.ToString(), workspaceId, actorUserId: tenant.UserId);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await students.GetAsync(student.Id, cancellationToken);
    }

    /// <summary>Ends the student's access with their account (the account itself stays; it may belong to other teachers' students).</summary>
    public async Task<StudentResponse> UnlinkAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var workspaceId = tenant.RequireWorkspaceId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockIntakeAsync(workspaceId, cancellationToken);
        var student = await queries.WithArchived(tracking: true).SingleOrDefaultAsync(s => s.Id == studentId, cancellationToken) ?? throw new NotFoundException();
        var now = clock.GetUtcNow().UtcDateTime;
        if (student.UserId is not null)
        {
            student.UserId = null;
            student.UpdatedAtUtc = now;
            audit.Record("student.account-unlinked", "Student", student.Id.ToString(), workspaceId, actorUserId: tenant.UserId);
        }

        await RevokePendingAsync(db, student.Id, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await students.GetAsync(student.Id, cancellationToken);
    }

    /// <summary>What the invitation page shows. Anything but a pending invitation of an active, unlinked student is INVALID_TOKEN.</summary>
    public async Task<InvitationPreviewResponse> PreviewAsync(InvitationTokenRequest request, CancellationToken cancellationToken)
    {
        var (invitation, student) = await LoadValidAsync(request.Token, tracking: false, cancellationToken);
        if (student.UserId is not null)
        {
            throw InvalidToken();
        }

        var workspace = (await workspaces.FindAsync([invitation.WorkspaceId], cancellationToken))[invitation.WorkspaceId];
        return new InvitationPreviewResponse(workspace.Name, student.FirstName, invitation.Email, student.IsMinor, invitation.ExpiresAtUtc);
    }

    /// <summary>Links the signed-in account to the invited student.</summary>
    public async Task<StudentTeacherResponse> AcceptAsync(Guid userId, AcceptInvitationRequest request, CancellationToken cancellationToken) =>
        (await AcceptCoreAsync(request.Token, request.GuardianConsent, userId, null, cancellationToken)).Response;

    /// <summary>Creates an account for the invited email, links it and signs it in.</summary>
    public async Task<StudentTeacherResponse> AcceptWithAccountAsync(HttpContext http, AcceptInvitationWithAccountRequest request, CancellationToken cancellationToken)
    {
        var linked = await AcceptCoreAsync(request.Token, request.GuardianConsent, null, (request.Password!, request.Locale), cancellationToken);
        await identity.SignInAsync(http, linked.UserId);
        return linked.Response;
    }

    private async Task<(Guid UserId, StudentTeacherResponse Response)> AcceptCoreAsync(
        string? token, bool guardianConsent, Guid? userId, (string Password, string? Locale)? newAccount, CancellationToken cancellationToken)
    {
        // A cheap check first, so garbage tokens never take a lock.
        var (found, _) = await LoadValidAsync(token, tracking: false, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockIntakeAsync(found.WorkspaceId, cancellationToken);
        var (invitation, student) = await LoadValidAsync(token, tracking: true, cancellationToken);
        if (student.UserId is not null)
        {
            throw new ConflictException("The student already has an account.", ErrorCodes.StudentAlreadyLinked);
        }

        if (student.IsMinor && !guardianConsent)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["guardianConsent"] = ["A parent or guardian must agree before a minor's account is created."],
            });
        }

        Guid accountId;
        if (newAccount is { } account)
        {
            accountId = await identity.CreateConfirmedAsync(invitation.Email, account.Password, account.Locale, cancellationToken);
        }
        else
        {
            accountId = userId!.Value;
            if (await accounts.IsLinkedInWorkspaceAsync(invitation.WorkspaceId, accountId, cancellationToken))
            {
                throw new ConflictException("Your account is already linked to a student of this teacher.", ErrorCodes.AccountAlreadyLinked);
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        student.UserId = accountId;
        student.UpdatedAtUtc = now;
        invitation.AcceptedAtUtc = now;
        invitation.AcceptedByUserId = accountId;
        invitation.GuardianConsentAtUtc = student.IsMinor ? now : null;
        audit.Record("student.account-linked", "Student", student.Id.ToString(), invitation.WorkspaceId, actorUserId: accountId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            // The filtered unique index (WorkspaceId, UserId) backs the check above.
            throw new ConflictException("Your account is already linked to a student of this teacher.", ErrorCodes.AccountAlreadyLinked);
        }

        await transaction.CommitAsync(cancellationToken);
        var workspace = (await workspaces.FindAsync([invitation.WorkspaceId], cancellationToken))[invitation.WorkspaceId];
        return (accountId, new StudentTeacherResponse(invitation.WorkspaceId, workspace.Name, student.Id, student.FirstName, student.LastName));
    }

    /// <summary>The teachers the signed-in user is an active student of.</summary>
    public async Task<IReadOnlyList<StudentTeacherResponse>> TeachersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var mine = await accounts.ActiveForUserAsync(userId, cancellationToken);
        var names = await workspaces.FindAsync([.. mine.Select(s => s.WorkspaceId).Distinct()], cancellationToken);
        return [.. mine.Select(s => new StudentTeacherResponse(s.WorkspaceId, names[s.WorkspaceId].Name, s.Id, s.FirstName, s.LastName))];
    }

    /// <summary>The student's own record in the current (student:self) workspace.</summary>
    public async Task<StudentSelfResponse> SelfAsync(CancellationToken cancellationToken)
    {
        var studentId = tenant.StudentId ?? throw new InvalidOperationException("Not a student request.");
        var student = await db.Set<Student>().AsNoTracking().SingleAsync(s => s.Id == studentId, cancellationToken);
        var subjects = await db.Set<StudentSubject>().AsNoTracking().Where(x => x.StudentId == studentId).Select(x => x.SubjectId).ToListAsync(cancellationToken);
        var workspace = (await workspaces.FindAsync([student.WorkspaceId], cancellationToken))[student.WorkspaceId];
        return new StudentSelfResponse(student.WorkspaceId, workspace.Name, student.Id, student.FirstName, student.LastName, subjects);
    }

    /// <summary>Revokes the student's pending invitations (inside the caller's unit of work); returns how many.</summary>
    internal static async Task<int> RevokePendingAsync(ClassHubDbContext db, Guid studentId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var pending = await db.Set<StudentInvitation>().Where(i => i.StudentId == studentId && i.AcceptedAtUtc == null && i.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var invitation in pending)
        {
            invitation.RevokedAtUtc = nowUtc;
        }

        return pending.Count;
    }

    private async Task<(StudentInvitation Invitation, Student Student)> LoadValidAsync(string? token, bool tracking, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            throw InvalidToken();
        }

        var invitation = await accounts.FindInvitationAsync(Hash(token.Trim()), tracking, cancellationToken);
        if (invitation is null || !invitation.IsPending(clock.GetUtcNow().UtcDateTime))
        {
            throw InvalidToken();
        }

        var student = await accounts.FindStudentAsync(invitation.WorkspaceId, invitation.StudentId, tracking, cancellationToken);
        return student is null || student.ArchivedAtUtc is not null ? throw InvalidToken() : (invitation, student);
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static BadRequestException InvalidToken() =>
        new("The invitation is invalid or has expired. Ask your teacher for a new one.", ErrorCodes.InvalidToken);
}
