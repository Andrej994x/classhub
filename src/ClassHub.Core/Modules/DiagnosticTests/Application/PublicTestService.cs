using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using ClassHub.Core.Modules.DiagnosticTests.Persistence;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Tenancy;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.DiagnosticTests.Application;

/// <summary>Public test taking limits (section <c>DiagnosticTests</c>).</summary>
public sealed class DiagnosticTestOptions
{
    public const string SectionName = "DiagnosticTests";

    /// <summary>Attempts per test and hour, whatever the client address (in addition to the per-IP limiter).</summary>
    [Range(1, 100_000)] public int MaxAttemptsPerTestPerHour { get; set; } = 200;
}

/// <summary>
/// Public test taking (R1-P7). A test is public only while its site is published, its owner is visible and the test is
/// published and not archived. Reads run under the read-only public binding; the attempt is written in its own scope
/// with the workspace set explicitly. Scoring is server-side; the visitor only ever gets totals back.
/// </summary>
internal sealed class PublicTestService(
    IPublicSites sites,
    TenantContext tenant,
    ClassHubDbContext db,
    ICatalogLookup catalog,
    IServiceScopeFactory scopes,
    IOptions<DiagnosticTestOptions> options,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal sealed record ResolvedTest(Guid WorkspaceId, string Language, DiagnosticTest Test, List<TestQuestion> Questions, ILookup<Guid, TestOption> Options);

    public async Task<PublicTestResponse> GetAsync(string siteSlug, string testSlug, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(siteSlug, testSlug, cancellationToken) ?? throw new NotFoundException();
        return await ToPublicAsync(resolved, catalog, cancellationToken);
    }

    internal static async Task<PublicTestResponse> ToPublicAsync(ResolvedTest resolved, ICatalogLookup catalog, CancellationToken cancellationToken)
    {
        var subject = (await catalog.SubjectsAsync([resolved.Test.SubjectId], cancellationToken)).FirstOrDefault();
        return new PublicTestResponse(
            resolved.Test.Version,
            resolved.Test.Title,
            resolved.Test.Slug,
            resolved.Test.Description,
            resolved.Test.Difficulty.ToString(),
            subject is null ? null : resolved.Language == "en" ? subject.NameEn : subject.NameMk,
            resolved.Questions.Sum(q => q.Points),
            [
                .. resolved.Questions.Select(q => new PublicTestQuestion(
                    q.Id, q.Text, q.Points, resolved.Options[q.Id].Count(o => o.IsCorrect) > 1,
                    [.. resolved.Options[q.Id].Select(o => new PublicTestOption(o.Id, o.Text))])),
            ]);
    }

    /// <summary>
    /// Validates the choices against the test (only its questions and their options, each at most once), scores them on
    /// the server, and stores the anonymous attempt. Tampered submissions are refused (400) and never stored.
    /// </summary>
    public async Task<PublicAttemptResult> SubmitAsync(string siteSlug, string testSlug, SubmitAttemptRequest request, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(siteSlug, testSlug, cancellationToken) ?? throw new NotFoundException();
        // Only the version the visitor answered may be scored: otherwise old choices would meet new correct answers.
        if (request.Version != resolved.Test.Version)
        {
            throw new ConflictException("The test was changed meanwhile. Reload the page and take it again.", ErrorCodes.TestChanged);
        }

        var answers = Validate(resolved, request.Answers ?? []);
        var score = TestScoring.Score(
            [.. resolved.Questions.Select(q => new ScoringQuestion(q.Id, q.Points, resolved.Options[q.Id].Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet()))],
            answers.ToDictionary(a => a.Key, a => (IReadOnlyCollection<Guid>)a.Value));
        var now = clock.GetUtcNow().UtcDateTime;
        var attempt = new TestAttempt
        {
            Id = Guid.NewGuid(), // random v4, never the sequential default
            WorkspaceId = resolved.WorkspaceId,
            TestId = resolved.Test.Id,
            AnswersJson = JsonSerializer.Serialize(answers, Json),
            Points = score.Points,
            MaxPoints = score.MaxPoints,
            CorrectQuestions = score.CorrectQuestions,
            TotalQuestions = score.TotalQuestions,
            CompletedAtUtc = now,
        };

        await using var scope = scopes.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        var attempts = scope.ServiceProvider.GetRequiredService<TestAttemptQueries>();
        await using var transaction = await writer.Database.BeginTransactionAsync(cancellationToken);
        await attempts.LockAttemptsAsync(resolved.Test.Id, cancellationToken);
        // Scored against the version read above: if the teacher changed the test meanwhile, this score is not valid.
        if (await attempts.PublishedVersionAsync(resolved.WorkspaceId, resolved.Test.Id, cancellationToken) != resolved.Test.Version)
        {
            throw new ConflictException("The test was changed meanwhile. Reload the page and take it again.", ErrorCodes.TestChanged);
        }
        if (await attempts.CountAttemptsSinceAsync(resolved.WorkspaceId, resolved.Test.Id, now.AddHours(-1), cancellationToken) >= options.Value.MaxAttemptsPerTestPerHour)
        {
            throw new TooManyRequestsException("This test has been taken very often in the last hour. Please try again later.");
        }

        writer.Set<TestAttempt>().Add(attempt);
        await writer.SaveChangesAsync(cancellationToken);
        await attempts.IncrementAttemptCountAsync(resolved.WorkspaceId, resolved.Test.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PublicAttemptResult(attempt.Id, score.Points, score.MaxPoints, score.CorrectQuestions, score.TotalQuestions);
    }

    private async Task<ResolvedTest?> ResolveAsync(string siteSlug, string testSlug, CancellationToken cancellationToken)
    {
        if (await sites.FindPublishedWorkspaceAsync(siteSlug, cancellationToken) is not { } site)
        {
            return null;
        }

        tenant.SetPublicReadOnly(site.WorkspaceId);
        return await LoadPublishedAsync(db, site.WorkspaceId, site.Language, testSlug, cancellationToken);
    }

    /// <summary>The published test of the bound workspace (tenant filters apply), or null.</summary>
    internal static async Task<ResolvedTest?> LoadPublishedAsync(ClassHubDbContext db, Guid workspaceId, string language, string testSlug, CancellationToken cancellationToken)
    {
        var test = await db.Set<DiagnosticTest>().AsNoTracking()
            .SingleOrDefaultAsync(t => t.Slug == testSlug && t.Status == TestStatus.Published, cancellationToken);
        if (test is null)
        {
            return null;
        }

        var questions = await db.Set<TestQuestion>().AsNoTracking().Where(q => q.TestId == test.Id).OrderBy(q => q.Position).ToListAsync(cancellationToken);
        var questionIds = questions.Select(q => q.Id).ToList();
        var options = (await db.Set<TestOption>().AsNoTracking().Where(o => questionIds.Contains(o.QuestionId)).OrderBy(o => o.Position).ToListAsync(cancellationToken))
            .ToLookup(o => o.QuestionId);
        return new ResolvedTest(workspaceId, language, test, questions, options);
    }

    private static Dictionary<Guid, Guid[]> Validate(ResolvedTest test, IReadOnlyList<AttemptAnswer> answers)
    {
        var questionIds = test.Questions.Select(q => q.Id).ToHashSet();
        var result = new Dictionary<Guid, Guid[]>();
        foreach (var answer in answers)
        {
            if (answer is null || !questionIds.Contains(answer.QuestionId) || result.ContainsKey(answer.QuestionId))
            {
                throw Invalid("These answers do not belong to this test.");
            }

            var chosen = answer.OptionIds ?? [];
            var allowed = test.Options[answer.QuestionId].Select(o => o.Id).ToHashSet();
            if (chosen.Count != chosen.Distinct().Count() || !chosen.All(allowed.Contains))
            {
                throw Invalid("These answers do not belong to this test.");
            }

            result[answer.QuestionId] = [.. chosen];
        }

        return result;
    }

    private static RequestValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["answers"] = [message] });
}

/// <summary>
/// Published tests of the currently bound workspace (site renderer, Razor test page) and of visible workspaces (sitemap).
/// No dependency on the Sites module: the site renderer uses it.
/// </summary>
internal sealed class PublishedTests(ClassHubDbContext db, ITenantContext tenant, ICatalogLookup catalog, TestAttemptQueries queries) : IPublicTests
{
    public async Task<PublicTestResponse?> FindAsync(string testSlug, string language, CancellationToken cancellationToken) =>
        await PublicTestService.LoadPublishedAsync(db, tenant.RequireWorkspaceId(), language, testSlug, cancellationToken) is { } resolved
            ? await PublicTestService.ToPublicAsync(resolved, catalog, cancellationToken)
            : null;

    public async Task<IReadOnlyList<PublishedTestLink>> ListPublishedAsync(CancellationToken cancellationToken) =>
        await db.Set<DiagnosticTest>().AsNoTracking()
            .Where(t => t.Status == TestStatus.Published)
            .OrderByDescending(t => t.PublishedAtUtc)
            .Select(t => new PublishedTestLink(t.Slug, t.Title, t.PublishedAtUtc!.Value))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(Guid WorkspaceId, PublishedTestLink Test)>> ListPublishedForWorkspacesAsync(
        IReadOnlyCollection<Guid> workspaceIds, CancellationToken cancellationToken)
    {
        var rows = await queries.ListPublishedAsync(workspaceIds, cancellationToken);
        return [.. rows.Select(r => (r.WorkspaceId, new PublishedTestLink(r.Slug, r.Title, r.PublishedAtUtc)))];
    }
}

/// <summary>Attaches leads to attempts and shows results to the teacher (Leads module, through Contracts).</summary>
internal sealed class DiagnosticAttemptLink(ClassHubDbContext db, TestAttemptQueries attempts, DiagnosticTestQueries tests) : IDiagnosticAttemptLink
{
    public Task<bool> CanLinkAsync(Guid attemptId, CancellationToken cancellationToken) =>
        db.Set<TestAttempt>().AnyAsync(a => a.Id == attemptId && a.LeadId == null, cancellationToken);

    public async Task LinkAsync(Guid workspaceId, Guid attemptId, Guid leadId, bool shareResult, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!await attempts.LinkLeadAsync(workspaceId, attemptId, leadId, shareResult ? nowUtc : null, cancellationToken))
        {
            throw new ConflictException("A request was already sent for this result.");
        }
    }

    public async Task<DiagnosticResultSummary?> GetResultAsync(Guid attemptId, CancellationToken cancellationToken) =>
        await db.Set<TestAttempt>().AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Join(tests.WithArchived(), a => a.TestId, t => t.Id, (a, t) => new { a, t.Title })
            .Select(x => new DiagnosticResultSummary(
                x.a.TestId, x.Title,
                x.a.ResultSharedAtUtc == null ? null : x.a.Points,
                x.a.ResultSharedAtUtc == null ? null : x.a.MaxPoints))
            .SingleOrDefaultAsync(cancellationToken);
}
