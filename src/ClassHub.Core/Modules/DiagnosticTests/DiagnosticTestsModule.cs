using System.Text.RegularExpressions;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.DiagnosticTests.Application;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using ClassHub.Core.Modules.DiagnosticTests.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClassHub.Core.Modules.DiagnosticTests;

/// <summary>Diagnostic tests (M04): the teacher's test builder (R1-P6); public taking and results in R1-P7.</summary>
public sealed class DiagnosticTestsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<DiagnosticTestQueries>();
        services.AddScoped<DiagnosticTestService>();
        // R1-P10 replaces this with the plan-aware quota.
        services.TryAddScoped<IPublishedTestQuota, UnlimitedPublishedTestQuota>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var tests = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/tests").WithTags("Diagnostic tests").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.TestsRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.TestsManage);

        tests.MapGet("/", async (DiagnosticTestService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)))
            .RequireAuthorization(read).WithName("ListDiagnosticTests");

        tests.MapPost("/", async (SaveDiagnosticTestRequest request, DiagnosticTestService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return TypedResults.Created((string?)null, created);
            })
            .WithValidation<SaveDiagnosticTestRequest>()
            .RequireAuthorization(manage).WithName("CreateDiagnosticTest")
            .WithSummary("Create a draft test with its questions and options. 409 TEST_SLUG_TAKEN for a used address.");

        tests.MapGet("/{testId:guid}", async (Guid testId, DiagnosticTestService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(testId, ct)))
            .RequireAuthorization(read).WithName("GetDiagnosticTest")
            .WithSummary("The whole test including the correct options (teacher only).");

        tests.MapPut("/{testId:guid}", async (Guid testId, SaveDiagnosticTestRequest request, DiagnosticTestService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(testId, request, ct)))
            .WithValidation<SaveDiagnosticTestRequest>()
            .RequireAuthorization(manage).WithName("UpdateDiagnosticTest")
            .WithSummary("Replace the test (questions keep their ids). 409 on a stale version; 409 TEST_HAS_ATTEMPTS for destructive edits after attempts (D-38).");

        tests.MapPost("/{testId:guid}/publish", async (Guid testId, TestVersionRequest request, DiagnosticTestService service, CancellationToken ct) =>
                TypedResults.Ok(await service.PublishAsync(testId, request, ct)))
            .WithValidation<TestVersionRequest>()
            .RequireAuthorization(manage).WithName("PublishDiagnosticTest")
            .WithSummary("Publish after the checks: a question, two options and a correct answer each, points. 400 lists what is missing.");

        tests.MapPost("/{testId:guid}/deactivate", async (Guid testId, TestVersionRequest request, DiagnosticTestService service, CancellationToken ct) =>
                TypedResults.Ok(await service.DeactivateAsync(testId, request, ct)))
            .WithValidation<TestVersionRequest>()
            .RequireAuthorization(manage).WithName("DeactivateDiagnosticTest");

        tests.MapPost("/{testId:guid}/duplicate", async (Guid testId, DiagnosticTestService service, CancellationToken ct) =>
            {
                var copy = await service.DuplicateAsync(testId, ct);
                return TypedResults.Created((string?)null, copy);
            })
            .RequireAuthorization(manage).WithName("DuplicateDiagnosticTest")
            .WithSummary("An editable draft copy with new ids and no attempts (D-38: \"duplicate as new version\").");

        tests.MapDelete("/{testId:guid}", async (Guid testId, DiagnosticTestService service, CancellationToken ct) =>
            {
                await service.ArchiveAsync(testId, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(manage).WithName("ArchiveDiagnosticTest")
            .WithSummary("Archive: hidden from the list and the public; attempts and the address are kept.");
    }
}

internal sealed partial class SaveDiagnosticTestRequestValidator : AbstractValidator<SaveDiagnosticTestRequest>
{
    public SaveDiagnosticTestRequestValidator()
    {
        RuleFor(x => x.Title).Cascade(CascadeMode.Stop).NotEmpty().Must(t => t!.Trim().Length > 0).MaximumLength(DiagnosticTest.TitleMaxLength);
        RuleFor(x => x.Slug).Must(s => SlugShape().IsMatch(s!.Trim()) && s.Trim().Length is >= DiagnosticTest.SlugMinLength and <= DiagnosticTest.SlugMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage($"Use {DiagnosticTest.SlugMinLength}–{DiagnosticTest.SlugMaxLength} lowercase letters, digits and single hyphens.");
        RuleFor(x => x.SubjectId).NotNull();
        RuleFor(x => x.Description).MaximumLength(DiagnosticTest.DescriptionMaxLength);
        RuleFor(x => x.Difficulty).Must(d => Enum.TryParse<TestDifficulty>(d, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed) && !char.IsDigit(d![0]))
            .WithMessage("Difficulty must be Beginner, Intermediate or Advanced.");
        RuleFor(x => x.Questions).Must(q => q is null || q.Count <= DiagnosticTest.MaxQuestions).WithMessage($"At most {DiagnosticTest.MaxQuestions} questions.");
        RuleFor(x => x.Questions).Must(q => q is null || q.All(i => i is not null)).WithMessage("Questions cannot be empty values.");
        RuleForEach(x => x.Questions).ChildRules(question =>
        {
            question.RuleFor(q => q.Text).Cascade(CascadeMode.Stop).NotEmpty().Must(t => t!.Trim().Length > 0).MaximumLength(TestQuestion.TextMaxLength);
            question.RuleFor(q => q.Points).InclusiveBetween(0, TestQuestion.MaxPoints);
            question.RuleFor(q => q.Options).Must(o => o is null || o.Count <= TestQuestion.MaxOptions).WithMessage($"At most {TestQuestion.MaxOptions} options.");
            question.RuleFor(q => q.Options).Must(o => o is null || o.All(i => i is not null)).WithMessage("Options cannot be empty values.");
            question.RuleForEach(q => q.Options).ChildRules(option =>
                option.RuleFor(o => o.Text).Cascade(CascadeMode.Stop).NotEmpty().Must(t => t!.Trim().Length > 0).MaximumLength(TestOption.TextMaxLength));
        }).When(x => x.Questions is not null && x.Questions.All(q => q is not null));
    }

    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9]))*\z")]
    private static partial Regex SlugShape();
}

internal sealed class TestVersionRequestValidator : AbstractValidator<TestVersionRequest>
{
    public TestVersionRequestValidator() => RuleFor(x => x.Version).NotNull();
}
