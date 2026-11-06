using System.Text.RegularExpressions;
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
    }
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

    [GeneratedRegex(@"^\+?[0-9 ()\-]{5,40}$")]
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
