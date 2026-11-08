using System.Security.Claims;
using System.Text.RegularExpressions;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Students.Application;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Students.Domain;
using ClassHub.Core.Modules.Students.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Students;

/// <summary>Students (M06, R2-P1): the teacher's own student records, lead conversion, private notes.</summary>
public sealed class StudentsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<StudentQueries>();
        services.AddScoped<StudentService>();
        services.AddScoped<IStudentLookup, StudentLookup>();
        services.AddScoped<ILeadStudents, LeadStudents>();
        services.AddScoped<StudentAccountQueries>();
        services.AddScoped<IStudentSelfLookup>(sp => sp.GetRequiredService<StudentAccountQueries>());
        services.AddScoped<StudentAccountService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var students = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/students").WithTags("Students").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.StudentsRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.StudentsManage);

        students.MapGet("", async (string? q, bool? archived, int? page, int? pageSize, StudentService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(q, archived ?? false, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization(read)
            .WithName("ListStudents")
            .WithSummary("Active (or archived) students, by name; search by name, email, phone or guardian.");

        students.MapGet("/{studentId:guid}", async (Guid studentId, StudentService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(studentId, ct)))
            .RequireAuthorization(read)
            .WithName("GetStudent");

        students.MapPost("", async (SaveStudentRequest request, StudentService service, CancellationToken ct) =>
                TypedResults.Created((string?)null, await service.CreateAsync(request, ct)))
            .WithValidation<SaveStudentRequest>()
            .RequireAuthorization(manage)
            .WithName("CreateStudent")
            .WithSummary("Add a student (no login needed). The email must be unique among your students; the Free plan limits active students.");

        students.MapPut("/{studentId:guid}", async (Guid studentId, SaveStudentRequest request, StudentService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(studentId, request, ct)))
            .WithValidation<SaveStudentRequest>()
            .RequireAuthorization(manage)
            .WithName("UpdateStudent");

        students.MapPost("/{studentId:guid}/archive", async (Guid studentId, StudentVersionRequest request, StudentService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(studentId, request, archived: true, ct)))
            .RequireAuthorization(manage)
            .WithName("ArchiveStudent");

        students.MapPost("/{studentId:guid}/restore", async (Guid studentId, StudentVersionRequest request, StudentService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(studentId, request, archived: false, ct)))
            .RequireAuthorization(manage)
            .WithName("RestoreStudent");

        students.MapPost("/from-lead", async (ConvertLeadRequest request, StudentService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ConvertAsync(request, ct)))
            .WithValidation<ConvertLeadRequest>()
            .RequireAuthorization(manage)
            .WithName("ConvertLeadToStudent")
            .WithSummary("D-12: link the request to the student with the same email, or create one; idempotent.");

        students.MapGet("/{studentId:guid}/notes", async (Guid studentId, StudentService service, CancellationToken ct) => TypedResults.Ok(await service.NotesAsync(studentId, ct)))
            .RequireAuthorization(read)
            .WithName("ListStudentNotes")
            .WithSummary("The teacher's private notes, newest first. Never part of any student-facing response.");

        students.MapPost("/{studentId:guid}/notes", async (Guid studentId, AddStudentNoteRequest request, StudentService service, CancellationToken ct) =>
                TypedResults.Created((string?)null, await service.AddNoteAsync(studentId, request, ct)))
            .WithValidation<AddStudentNoteRequest>()
            .RequireAuthorization(manage)
            .WithName("AddStudentNote");

        // Student accounts (R2-P3): the teacher's side.
        students.MapPost("/{studentId:guid}/invitation", async (Guid studentId, InviteStudentRequest request, StudentAccountService service, CancellationToken ct) =>
                TypedResults.Ok(await service.InviteAsync(studentId, request, ct)))
            .WithValidation<InviteStudentRequest>()
            .RequireAuthorization(manage)
            .WithName("InviteStudent")
            .WithSummary("Email the student a 7-day, single-use link to their own account; replaces a pending invitation.");

        students.MapDelete("/{studentId:guid}/invitation", async (Guid studentId, StudentAccountService service, CancellationToken ct) =>
                TypedResults.Ok(await service.RevokeInvitationAsync(studentId, ct)))
            .RequireAuthorization(manage)
            .WithName("RevokeStudentInvitation");

        students.MapDelete("/{studentId:guid}/account", async (Guid studentId, StudentAccountService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UnlinkAsync(studentId, ct)))
            .RequireAuthorization(manage)
            .WithName("UnlinkStudentAccount")
            .WithSummary("End the student's access with their account (the account itself stays).");

        // The invitation link (anonymous or signed in; the token is the capability).
        var invitations = endpoints.MapGroup("/student-invitations").WithTags("Student accounts").RequireAntiforgeryHeader();
        invitations.MapPost("/preview", async (InvitationTokenRequest request, StudentAccountService service, CancellationToken ct) =>
                TypedResults.Ok(await service.PreviewAsync(request, ct)))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("PreviewStudentInvitation");

        invitations.MapPost("/accept", async (AcceptInvitationRequest request, ClaimsPrincipal user, StudentAccountService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AcceptAsync(UserId(user), request, ct)))
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("AcceptStudentInvitation")
            .WithSummary("Link the signed-in account to the invited student.");

        invitations.MapPost("/accept-with-account", async (AcceptInvitationWithAccountRequest request, HttpContext http, StudentAccountService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AcceptWithAccountAsync(http, request, ct)))
            .WithValidation<AcceptInvitationWithAccountRequest>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("AcceptStudentInvitationWithAccount")
            .WithSummary("Create an account for the invited email, link it and sign in.");

        // The student portal (R2-P3 shell): only the signed-in user's own records.
        var portal = endpoints.MapGroup("/student").WithTags("Student portal").RequireAntiforgeryHeader();
        portal.MapGet("/teachers", async (ClaimsPrincipal user, StudentAccountService service, CancellationToken ct) =>
                TypedResults.Ok(await service.TeachersAsync(UserId(user), ct)))
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithName("ListMyTeachers")
            .WithSummary("The teachers the signed-in user is an active student of (D-16).");

        portal.MapGet("/w/{workspaceId:guid}/profile", async (StudentAccountService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SelfAsync(ct)))
            .RequireAuthorization(ClassHubPolicies.StudentSelf)
            .WithName("GetMyStudentProfile");
    }

    private static Guid UserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No user id claim."));
}

internal sealed partial class SaveStudentRequestValidator : AbstractValidator<SaveStudentRequest>
{
    public SaveStudentRequestValidator()
    {
        RuleFor(x => x.FirstName).Must(n => n is not null && n.Trim().Length is >= 1 and <= Student.NameMaxLength)
            .WithMessage($"Enter the first name (up to {Student.NameMaxLength} characters).");
        RuleFor(x => x.LastName).MaximumLength(Student.NameMaxLength);
        RuleFor(x => x.Email).MaximumLength(Student.EmailMaxLength).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(Student.PhoneMaxLength).Matches(PhonePattern()).When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.GuardianName).MaximumLength(Student.GuardianNameMaxLength);
        RuleFor(x => x.GuardianEmail).MaximumLength(Student.EmailMaxLength).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.GuardianEmail));
        RuleFor(x => x.GuardianPhone).MaximumLength(Student.PhoneMaxLength).Matches(PhonePattern()).When(x => !string.IsNullOrWhiteSpace(x.GuardianPhone));
        RuleFor(x => x.SubjectIds).Must(s => s is null || s.Count <= Student.MaxSubjects).WithMessage($"At most {Student.MaxSubjects} subjects.");
    }

    /// <summary>The same format the public request form accepts, so numbers copied from a request stay valid.</summary>
    [GeneratedRegex(@"\A[0-9+()\-./ ]{5,40}\z")]
    private static partial Regex PhonePattern();
}

internal sealed class AddStudentNoteRequestValidator : AbstractValidator<AddStudentNoteRequest>
{
    public AddStudentNoteRequestValidator() =>
        RuleFor(x => x.Text).Must(t => t is not null && t.Trim().Length is >= 1 and <= StudentNote.TextMaxLength)
            .WithMessage($"Write a note (up to {StudentNote.TextMaxLength} characters).");
}

internal sealed class ConvertLeadRequestValidator : AbstractValidator<ConvertLeadRequest>
{
    public ConvertLeadRequestValidator() => RuleFor(x => x.LeadId).NotNull();
}

internal sealed class InviteStudentRequestValidator : AbstractValidator<InviteStudentRequest>
{
    public InviteStudentRequestValidator() =>
        RuleFor(x => x.Email).MaximumLength(Student.EmailMaxLength).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
}

internal sealed class AcceptInvitationWithAccountRequestValidator : AbstractValidator<AcceptInvitationWithAccountRequest>
{
    public AcceptInvitationWithAccountRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Locale).MaximumLength(10);
    }
}
