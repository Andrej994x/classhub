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
        services.AddScoped<ITestModeration, Persistence.TestModerationQueries>();
        services.AddOptions<DiagnosticTestOptions>().Bind(configuration.GetSection(DiagnosticTestOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<DiagnosticTestQueries>();
        services.AddScoped<TestAttemptQueries>();
        services.AddScoped<DiagnosticTestService>();
        services.AddScoped<PublicTestService>();
        services.AddScoped<IPublicTests, PublishedTests>();
        services.AddScoped<IDiagnosticAttemptLink, DiagnosticAttemptLink>();
        services.AddScoped<ITestStats, TestStatsService>();
        // "{testSlug:testslug}": the address shape of a test (also used by the Razor test page).
        services.Configure<RouteOptions>(options => options.SetParameterPolicy<TestSlugRouteConstraint>(TestSlugRouteConstraint.Name));
        // R1-P10 replaces this with the plan-aware quota.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Public, anonymous: the published test without correct answers, and attempt submission (scored on the server).
        var site = endpoints.MapGroup("/public/sites/{slug:siteslug}/tests/{testSlug:testslug}").WithTags("Public tests").AllowAnonymous();
        site.MapGet("/", async (string slug, string testSlug, HttpContext http, PublicTestService service, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return TypedResults.Ok(await service.GetAsync(slug, testSlug, ct));
            })
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .WithName("GetPublicTest")
            .WithSummary("A published test with its questions and options (never the correct answers).");

        site.MapPost("/attempts", async (string slug, string testSlug, SubmitAttemptRequest request, PublicTestService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SubmitAsync(slug, testSlug, request, ct)))
            .RequireRateLimiting(RateLimitPolicies.PublicForm)
            .WithName("SubmitTestAttempt")
            .WithSummary("Submit the chosen options; the server scores them and returns the totals and an attempt id for an optional contact request.");

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

        tests.MapGet("/{testId:guid}/attempts", async (Guid testId, DiagnosticTestService service, CancellationToken ct) => TypedResults.Ok(await service.AttemptsAsync(testId, ct)))
            .RequireAuthorization(read).WithName("ListTestAttempts")
            .WithSummary("How often the test was taken, and the results visitors shared with their contact request.");

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

/// <summary>Route constraint <c>{testSlug:testslug}</c>: lowercase letters, digits and single hyphens, 3–60 characters.</summary>
public sealed partial class TestSlugRouteConstraint : IRouteConstraint
{
    public const string Name = "testslug";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.TryGetValue(routeKey, out var value)
            && Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) is { Length: >= DiagnosticTest.SlugMinLength and <= DiagnosticTest.SlugMaxLength } slug
            && Shape().IsMatch(slug);
    }

    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9]))*\z")]
    private static partial Regex Shape();
}
