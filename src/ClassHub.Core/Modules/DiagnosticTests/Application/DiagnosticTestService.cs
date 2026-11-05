using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using ClassHub.Core.Modules.DiagnosticTests.Persistence;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Globalization;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.DiagnosticTests.Application;

/// <summary>
/// Diagnostic tests of the current (verified) workspace: the whole test (questions, options, correct answers, points)
/// is saved at once with a version check. Once a test has attempts, only non-destructive edits are allowed (D-38):
/// texts, title, description, difficulty, address and order; structure, correct answers, points and subject are fixed,
/// and "duplicate" makes an editable new version.
/// </summary>
internal sealed class DiagnosticTestService(
    ClassHubDbContext db,
    DiagnosticTestQueries queries,
    TestAttemptQueries attemptLock,
    ITenantContext tenant,
    ICatalogLookup catalog,
    IPublishedTestQuota quota,
    IPublicSiteCache siteCache,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<DiagnosticTestListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var tests = await db.Set<DiagnosticTest>().AsNoTracking().OrderByDescending(t => t.UpdatedAtUtc).ToListAsync(cancellationToken);
        var ids = tests.Select(t => t.Id).ToList();
        var stats = await db.Set<TestQuestion>().AsNoTracking().Where(q => ids.Contains(q.TestId))
            .GroupBy(q => q.TestId).Select(g => new { TestId = g.Key, Count = g.Count(), Points = g.Sum(q => q.Points) })
            .ToDictionaryAsync(x => x.TestId, cancellationToken);
        return
        [
            .. tests.Select(t => new DiagnosticTestListItem(
                t.Id, t.Title, t.Slug, t.SubjectId, t.Difficulty.ToString(), t.Status.ToString(),
                stats.GetValueOrDefault(t.Id)?.Count ?? 0, stats.GetValueOrDefault(t.Id)?.Points ?? 0, t.AttemptCount, t.UpdatedAtUtc)),
        ];
    }

    public async Task<DiagnosticTestResponse> GetAsync(Guid testId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await LoadAsync(testId, tracking: false, cancellationToken), cancellationToken);

    public async Task<DiagnosticTestResponse> CreateAsync(SaveDiagnosticTestRequest request, CancellationToken cancellationToken)
    {
        await ThrowIfUnknownSubjectAsync(request.SubjectId!.Value, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var test = new DiagnosticTest
        {
            Id = Guid.NewGuid(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            Title = request.Title!.Trim(),
            Slug = await SlugForAsync(request.Slug, request.Title!, cancellationToken),
            SubjectId = request.SubjectId!.Value,
            Description = Clean(request.Description),
            Difficulty = Enum.Parse<TestDifficulty>(request.Difficulty!),
            Status = TestStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Set<DiagnosticTest>().Add(test);
        AddQuestions(test.Id, request.Questions ?? [], keepIds: false);
        await SaveAsync(cancellationToken);
        return await GetAsync(test.Id, cancellationToken);
    }

    public async Task<DiagnosticTestResponse> UpdateAsync(Guid testId, SaveDiagnosticTestRequest request, CancellationToken cancellationToken)
    {
        // The same per-test lock as attempt submission: either an attempt is counted first (and the D-38 check below
        // sees it), or this edit saves first (and the attempt, scored on the old version, is refused).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await attemptLock.LockAttemptsAsync(testId, cancellationToken);
        var test = await LoadForChangeAsync(testId, request.Version, cancellationToken);
        await ThrowIfUnknownSubjectAsync(request.SubjectId!.Value, cancellationToken);
        var questions = await db.Set<TestQuestion>().Where(q => q.TestId == test.Id).ToListAsync(cancellationToken);
        var questionIds = questions.Select(q => q.Id).ToList();
        var options = await db.Set<TestOption>().Where(o => questionIds.Contains(o.QuestionId)).ToListAsync(cancellationToken);
        var requested = request.Questions ?? [];

        // Each id at most once: a repeated id would make one row stand in for another (and slip past the D-38 checks).
        if (requested.Where(q => q.Id is not null).GroupBy(q => q.Id).Any(g => g.Count() > 1))
        {
            throw Invalid("questions", "A question appears more than once.");
        }

        if (requested.SelectMany(q => q.Options ?? []).Where(o => o.Id is not null).GroupBy(o => o.Id).Any(g => g.Count() > 1))
        {
            throw Invalid("questions", "An option appears more than once.");
        }

        // A published test must stay publishable: incomplete edits need deactivating first.
        if (test.Status == TestStatus.Published && PublishProblems(Preview(requested)) is { Count: > 0 } notReady)
        {
            throw new RequestValidationException(notReady);
        }

        // Ids must belong to this test (and options to their question): never adopt foreign rows.
        var known = questions.ToDictionary(q => q.Id);
        var knownOptions = options.ToDictionary(o => o.Id);
        for (var i = 0; i < requested.Count; i++)
        {
            if (requested[i].Id is { } id && !known.ContainsKey(id))
            {
                throw Invalid($"questions[{i}].id", "Unknown question.");
            }

            var question = requested[i];
            for (var j = 0; j < (question.Options ?? []).Count; j++)
            {
                if (question.Options![j].Id is { } optionId && (!knownOptions.TryGetValue(optionId, out var option) || option.QuestionId != question.Id))
                {
                    throw Invalid($"questions[{i}].options[{j}].id", "Unknown option.");
                }
            }
        }

        if (test.AttemptCount > 0 && ChangesStructure(test, request, questions, options))
        {
            throw new ConflictException(
                "This test already has attempts: questions, options, correct answers, points and subject cannot change. Duplicate it to edit a new version.",
                ErrorCodes.TestHasAttempts);
        }

        test.Title = request.Title!.Trim();
        test.Slug = string.IsNullOrWhiteSpace(request.Slug) ? test.Slug : request.Slug.Trim();
        test.SubjectId = request.SubjectId!.Value;
        test.Description = Clean(request.Description);
        test.Difficulty = Enum.Parse<TestDifficulty>(request.Difficulty!);

        // Removed questions and options go (with their options); kept ones are updated in place; new ones are added.
        var keptQuestionIds = requested.Where(q => q.Id is not null).Select(q => q.Id!.Value).ToHashSet();
        var keptOptionIds = requested.SelectMany(q => q.Options ?? []).Where(o => o.Id is not null).Select(o => o.Id!.Value).ToHashSet();
        db.Set<TestOption>().RemoveRange(options.Where(o => !keptOptionIds.Contains(o.Id) || !keptQuestionIds.Contains(o.QuestionId)));
        db.Set<TestQuestion>().RemoveRange(questions.Where(q => !keptQuestionIds.Contains(q.Id)));
        for (var i = 0; i < requested.Count; i++)
        {
            var item = requested[i];
            if (item.Id is { } id)
            {
                var question = known[id];
                (question.Position, question.Text, question.Points) = (i + 1, item.Text!.Trim(), item.Points);
                var itemOptions = item.Options ?? [];
                for (var j = 0; j < itemOptions.Count; j++)
                {
                    var optionItem = itemOptions[j];
                    if (optionItem.Id is { } optionId)
                    {
                        var option = knownOptions[optionId];
                        (option.Position, option.Text, option.IsCorrect) = (j + 1, optionItem.Text!.Trim(), optionItem.IsCorrect);
                    }
                    else
                    {
                        db.Set<TestOption>().Add(NewOption(id, optionItem, j + 1));
                    }
                }
            }
            else
            {
                AddQuestion(test.Id, item, i + 1, keepIds: false);
            }
        }

        Touch(test);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(test.Id, cancellationToken);
    }

    /// <summary>Publishes after the readiness checks (questions, options, a correct answer each, points) and the plan hook.</summary>
    public async Task<DiagnosticTestResponse> PublishAsync(Guid testId, TestVersionRequest request, CancellationToken cancellationToken)
    {
        var test = await LoadForChangeAsync(testId, request.Version, cancellationToken);
        if (test.Status == TestStatus.Published)
        {
            return await ToResponseAsync(test, cancellationToken);
        }

        var response = await ToResponseAsync(test, cancellationToken);
        if (PublishProblems(response) is { Count: > 0 } problems)
        {
            throw new RequestValidationException(problems);
        }

        await quota.EnsureCanPublishAsync(cancellationToken);
        test.Status = TestStatus.Published;
        test.PublishedAtUtc = clock.GetUtcNow().UtcDateTime;
        Touch(test);
        await SaveAsync(cancellationToken);
        return await ToResponseAsync(test, cancellationToken);
    }

    public async Task<DiagnosticTestResponse> DeactivateAsync(Guid testId, TestVersionRequest request, CancellationToken cancellationToken)
    {
        var test = await LoadForChangeAsync(testId, request.Version, cancellationToken);
        if (test.Status != TestStatus.Published)
        {
            throw new BusinessRuleException("Only a published test can be deactivated.", ErrorCodes.InvalidStatusTransition);
        }

        test.Status = TestStatus.Inactive;
        Touch(test);
        await SaveAsync(cancellationToken);
        return await ToResponseAsync(test, cancellationToken);
    }

    /// <summary>D-38: an editable copy (draft, new ids, no attempts) with a free address.</summary>
    public async Task<DiagnosticTestResponse> DuplicateAsync(Guid testId, CancellationToken cancellationToken)
    {
        var source = await ToResponseAsync(await LoadAsync(testId, tracking: false, cancellationToken), cancellationToken);
        var title = Truncate($"{source.Title} (2)", DiagnosticTest.TitleMaxLength);
        var copy = new SaveDiagnosticTestRequest(
            title,
            await UniqueSlugAsync(Truncate($"{source.Slug}-2", DiagnosticTest.SlugMaxLength - 4).TrimEnd('-'), cancellationToken),
            source.SubjectId,
            source.Description,
            source.Difficulty,
            [.. source.Questions.Select(q => new SaveTestQuestion(null, q.Text, q.Points, [.. q.Options.Select(o => new SaveTestOption(null, o.Text, o.IsCorrect))]))]);
        return await CreateAsync(copy, cancellationToken);
    }

    /// <summary>Archives the test: hidden from the list and the public, attempts and the address kept.</summary>
    public async Task ArchiveAsync(Guid testId, CancellationToken cancellationToken)
    {
        var test = await LoadAsync(testId, tracking: true, cancellationToken);
        test.ArchivedAtUtc = clock.GetUtcNow().UtcDateTime;
        if (test.Status == TestStatus.Published)
        {
            test.Status = TestStatus.Inactive;
        }

        Touch(test);
        await SaveAsync(cancellationToken);
    }

    /// <summary>The requested questions as a response, for the readiness check before anything is written.</summary>
    private static DiagnosticTestResponse Preview(IReadOnlyList<SaveTestQuestion> questions)
    {
        IReadOnlyList<TestQuestionResponse> items =
        [
            .. questions.Select(q => new TestQuestionResponse(q.Id ?? Guid.Empty, q.Text ?? string.Empty, q.Points,
                [.. (q.Options ?? []).Select(o => new TestOptionResponse(o.Id ?? Guid.Empty, o.Text ?? string.Empty, o.IsCorrect))])),
        ];
        return new DiagnosticTestResponse(Guid.Empty, string.Empty, string.Empty, Guid.Empty, null, string.Empty, string.Empty, null, 0, false, items,
            items.Sum(q => q.Points), DateTime.MinValue, 0);
    }

    /// <summary>Attempts of a test: the count, and the results visitors chose to share (D-18), newest first.</summary>
    public async Task<TestAttemptsResponse> AttemptsAsync(Guid testId, CancellationToken cancellationToken)
    {
        var test = await queries.WithArchived().AsNoTracking().SingleOrDefaultAsync(t => t.Id == testId, cancellationToken) ?? throw new NotFoundException();
        var attempts = db.Set<TestAttempt>().AsNoTracking().Where(a => a.TestId == test.Id);
        var total = await attempts.CountAsync(cancellationToken);
        var shared = await attempts.Where(a => a.ResultSharedAtUtc != null).OrderByDescending(a => a.CompletedAtUtc).Take(100)
            .Select(a => new SharedAttemptResult(a.Id, a.Points, a.MaxPoints, a.CorrectQuestions, a.TotalQuestions, a.CompletedAtUtc, a.LeadId))
            .ToListAsync(cancellationToken);
        var sharedCount = await attempts.CountAsync(a => a.ResultSharedAtUtc != null, cancellationToken);
        return new TestAttemptsResponse(total, sharedCount, shared);
    }

    /// <summary>Why the test cannot be published yet, by field (empty when it is ready).</summary>
    internal static Dictionary<string, string[]> PublishProblems(DiagnosticTestResponse test)
    {
        var problems = new Dictionary<string, string[]>();
        if (test.Questions.Count == 0)
        {
            problems["questions"] = ["Add at least one question."];
        }
        else if (test.TotalPoints <= 0)
        {
            problems["questions"] = ["At least one question must be worth points."];
        }

        for (var i = 0; i < test.Questions.Count; i++)
        {
            var question = test.Questions[i];
            if (question.Options.Count < TestQuestion.MinOptions)
            {
                problems[$"questions[{i}].options"] = [$"Give at least {TestQuestion.MinOptions} options."];
            }
            else if (!question.Options.Any(o => o.IsCorrect))
            {
                problems[$"questions[{i}].options"] = ["Mark at least one correct option."];
            }
        }

        return problems;
    }

    private static bool ChangesStructure(DiagnosticTest test, SaveDiagnosticTestRequest request, List<TestQuestion> questions, List<TestOption> options)
    {
        var requested = request.Questions ?? [];
        if (request.SubjectId != test.SubjectId || requested.Count != questions.Count || requested.Any(q => q.Id is null))
        {
            return true;
        }

        var byId = questions.ToDictionary(q => q.Id);
        foreach (var item in requested)
        {
            if (!byId.TryGetValue(item.Id!.Value, out var question) || question.Points != item.Points)
            {
                return true;
            }

            var current = options.Where(o => o.QuestionId == question.Id).ToDictionary(o => o.Id);
            var itemOptions = item.Options ?? [];
            if (itemOptions.Count != current.Count || itemOptions.Any(o => o.Id is not { } id || !current.TryGetValue(id, out var option) || option.IsCorrect != o.IsCorrect))
            {
                return true;
            }
        }

        return false;
    }

    private void AddQuestions(Guid testId, IReadOnlyList<SaveTestQuestion> questions, bool keepIds)
    {
        for (var i = 0; i < questions.Count; i++)
        {
            AddQuestion(testId, questions[i], i + 1, keepIds);
        }
    }

    private void AddQuestion(Guid testId, SaveTestQuestion item, int position, bool keepIds)
    {
        // Explicit: WorkspaceId is part of the (WorkspaceId, Id) alternate key, so EF would otherwise generate a value.
        var question = new TestQuestion
        {
            Id = keepIds && item.Id is { } id ? id : Guid.NewGuid(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            TestId = testId,
            Position = position,
            Text = item.Text!.Trim(),
            Points = item.Points,
        };
        db.Set<TestQuestion>().Add(question);
        var options = item.Options ?? [];
        for (var j = 0; j < options.Count; j++)
        {
            db.Set<TestOption>().Add(NewOption(question.Id, options[j], j + 1));
        }
    }

    private TestOption NewOption(Guid questionId, SaveTestOption item, int position) =>
        new() { Id = Guid.NewGuid(), WorkspaceId = tenant.RequireWorkspaceId(), QuestionId = questionId, Position = position, Text = item.Text!.Trim(), IsCorrect = item.IsCorrect };

    private async Task<string> SlugForAsync(string? requested, string title, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim(); // shape validated; uniqueness decided by the index (409)
        }

        var fromTitle = Slugs.FromText(title, DiagnosticTest.SlugMaxLength - 4);
        return await UniqueSlugAsync(fromTitle.Length >= DiagnosticTest.SlugMinLength ? fromTitle : "test", cancellationToken);
    }

    /// <summary><paramref name="baseSlug"/>, or the first free "-2", "-3", … variant in this workspace (archived tests included).</summary>
    private async Task<string> UniqueSlugAsync(string baseSlug, CancellationToken cancellationToken)
    {
        var taken = await queries.SlugsStartingWithAsync(baseSlug, cancellationToken);
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{baseSlug}-{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            // The public site lists published tests and the test pages are cached: every change may show there.
            await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
            await siteCache.EvictSitemapAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("Another test of yours already uses this address. Choose another one.", ErrorCodes.TestSlugTaken);
        }
    }

    private void Touch(DiagnosticTest test)
    {
        test.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        test.Version++;
    }

    private async Task<DiagnosticTest> LoadForChangeAsync(Guid testId, int? version, CancellationToken cancellationToken)
    {
        var test = await LoadAsync(testId, tracking: true, cancellationToken);
        if (version != test.Version)
        {
            throw new ConflictException("The test was changed elsewhere. Reload it and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        return test;
    }

    private async Task<DiagnosticTest> LoadAsync(Guid testId, bool tracking, CancellationToken cancellationToken)
    {
        var tests = tracking ? db.Set<DiagnosticTest>() : db.Set<DiagnosticTest>().AsNoTracking();
        return await tests.SingleOrDefaultAsync(t => t.Id == testId, cancellationToken) ?? throw new NotFoundException();
    }

    private async Task<DiagnosticTestResponse> ToResponseAsync(DiagnosticTest test, CancellationToken cancellationToken)
    {
        var questions = await db.Set<TestQuestion>().AsNoTracking().Where(q => q.TestId == test.Id).OrderBy(q => q.Position).ToListAsync(cancellationToken);
        var questionIds = questions.Select(q => q.Id).ToList();
        var options = (await db.Set<TestOption>().AsNoTracking().Where(o => questionIds.Contains(o.QuestionId)).OrderBy(o => o.Position).ToListAsync(cancellationToken))
            .ToLookup(o => o.QuestionId);
        IReadOnlyList<TestQuestionResponse> items =
        [
            .. questions.Select(q => new TestQuestionResponse(q.Id, q.Text, q.Points, [.. options[q.Id].Select(o => new TestOptionResponse(o.Id, o.Text, o.IsCorrect))])),
        ];
        return new DiagnosticTestResponse(
            test.Id, test.Title, test.Slug, test.SubjectId, test.Description, test.Difficulty.ToString(), test.Status.ToString(), test.PublishedAtUtc,
            test.AttemptCount, test.ArchivedAtUtc is not null, items, items.Sum(q => q.Points), test.UpdatedAtUtc, test.Version);
    }

    private async Task ThrowIfUnknownSubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        if ((await catalog.UnknownSubjectsAsync([subjectId], cancellationToken)).Count > 0)
        {
            throw Invalid("subjectId", "Unknown subject.");
        }
    }

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Until R1-P10: no plan limit on published tests.</summary>
internal sealed class UnlimitedPublishedTestQuota : IPublishedTestQuota
{
    public Task EnsureCanPublishAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
