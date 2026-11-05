using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.DiagnosticTests;

/// <summary>R1-P6: the diagnostic test builder (M04): CRUD, publish rules, D-38 editing rules, isolation.</summary>
[Collection(ApiCollection.Name)]
public sealed class DiagnosticTestTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_teacher_creates_a_test_with_questions_and_gets_an_address_from_the_title()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");

        var test = await CreateAsync(teacher, Body(math, "Математика: дропки"));

        (test.Title, test.Slug, test.Status, test.Difficulty, test.Version, test.AttemptCount).ShouldBe(("Математика: дропки", "matematika-dropki", "Draft", "Intermediate", 0, 0));
        test.Questions.Count.ShouldBe(2);
        test.Questions[0].Options.Select(o => (o.Text, o.IsCorrect)).ShouldBe([("3/4", true), ("2/6", false)]);
        test.TotalPoints.ShouldBe(5);
        var second = await CreateAsync(teacher, Body(math, "Математика: дропки"));
        second.Slug.ShouldBe("matematika-dropki-2"); // free variant in the same workspace

        var list = await teacher.Client.GetJsonAsync<List<DiagnosticTestListItem>>(TestsPath(teacher));
        list.Select(t => (t.Slug, t.QuestionCount, t.TotalPoints)).ShouldBe([("matematika-dropki-2", 2, 5), ("matematika-dropki", 2, 5)]);
    }

    [Fact]
    public async Task A_chosen_address_must_be_free_within_the_workspace_but_may_repeat_elsewhere()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        await CreateAsync(teacher, Body(math, "Тест", slug: "nivo-1"));

        using (var taken = await teacher.Client.PostAsync(TestsPath(teacher), Body(math, "Друг тест", slug: "nivo-1")))
        {
            taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await taken.ErrorCodeAsync()).ShouldBe(ErrorCodes.TestSlugTaken);
        }

        (await CreateAsync(other, Body(math, "Тест", slug: "nivo-1"))).Slug.ShouldBe("nivo-1");
    }

    [Fact]
    public async Task Edits_keep_question_ids_and_need_the_current_version()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var test = await CreateAsync(teacher, Body(math, "Тест"));
        var first = test.Questions[0];

        // Keep question 1 (edited, one option added), drop question 2, add a new one.
        var edited = await UpdateAsync(teacher, test.Id, new
        {
            title = "Тест (изменет)",
            slug = test.Slug,
            subjectId = math,
            difficulty = "Advanced",
            questions = new object[]
            {
                new { id = first.Id, text = "Колку е 1/2 + 1/4 (точно)?", points = 4, options = new object[]
                {
                    new { id = first.Options[0].Id, text = "3/4", isCorrect = true },
                    new { id = (Guid?)null, text = "1", isCorrect = false },
                } },
                new { id = (Guid?)null, text = "Колку е 2 · 3?", points = 1, options = new object[] { new { text = "6", isCorrect = true }, new { text = "5", isCorrect = false } } },
            },
            version = test.Version,
        });

        edited.Version.ShouldBe(1);
        edited.Questions[0].Id.ShouldBe(first.Id);
        edited.Questions[0].Options[0].Id.ShouldBe(first.Options[0].Id);
        edited.Questions.Select(q => (q.Text, q.Points)).ShouldBe([("Колку е 1/2 + 1/4 (точно)?", 4), ("Колку е 2 · 3?", 1)]);
        edited.Questions[0].Options.Select(o => o.Text).ShouldBe(["3/4", "1"]);

        using var stale = await teacher.Client.PutAsync($"{TestsPath(teacher)}/{test.Id}", Body(math, "Застарено", version: test.Version));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
        await using var db = fixture.CreateDbContext();
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM TestQuestions WHERE TestId = {test.Id}").SingleAsync(Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Ids_of_another_test_or_workspace_are_never_adopted()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var mine = await CreateAsync(teacher, Body(math, "Мој"));
        var secondOfMine = await CreateAsync(teacher, Body(math, "Втор"));
        var foreign = await CreateAsync(other, Body(math, "Туѓ"));

        foreach (var (questionId, optionId) in new[]
                 {
                     (foreign.Questions[0].Id, (Guid?)null),
                     (secondOfMine.Questions[0].Id, null),
                     (mine.Questions[0].Id, (Guid?)mine.Questions[1].Options[0].Id), // option of another question
                 })
        {
            using var response = await teacher.Client.PutAsync($"{TestsPath(teacher)}/{mine.Id}", new
            {
                title = "Т",
                subjectId = math,
                difficulty = "Beginner",
                questions = new[] { new { id = questionId, text = "П", points = 1, options = new[] { new { id = optionId, text = "О", isCorrect = true } } } },
                version = mine.Version,
            });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await GetAsync(other, foreign.Id)).Questions[0].Text.ShouldBe("Колку е 1/2 + 1/4?");
    }

    [Fact]
    public async Task Publishing_checks_readiness_and_tests_can_be_deactivated_and_published_again()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var empty = await CreateAsync(teacher, new { title = "Празен", subjectId = math, difficulty = "Beginner", questions = Array.Empty<object>() });
        using (var notReady = await teacher.Client.PostAsync($"{TestsPath(teacher)}/{empty.Id}/publish", new { version = empty.Version }))
        {
            notReady.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await notReady.ReadJsonAsync()).GetProperty("errors").TryGetProperty("questions", out _).ShouldBeTrue();
        }

        var noCorrect = await CreateAsync(teacher, new
        {
            title = "Без точен",
            subjectId = math,
            difficulty = "Beginner",
            questions = new[] { new { text = "П", points = 1, options = new[] { new { text = "А", isCorrect = false }, new { text = "Б", isCorrect = false } } } },
        });
        using (var missing = await teacher.Client.PostAsync($"{TestsPath(teacher)}/{noCorrect.Id}/publish", new { version = noCorrect.Version }))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await missing.ReadJsonAsync()).GetProperty("errors").TryGetProperty("questions[0].options", out _).ShouldBeTrue();
        }

        var test = await CreateAsync(teacher, Body(math, "Готов"));
        var published = await ChangeAsync(teacher, test.Id, "publish", test.Version);
        (published.Status, published.PublishedAtUtc is not null).ShouldBe(("Published", true));
        var inactive = await ChangeAsync(teacher, test.Id, "deactivate", published.Version);
        inactive.Status.ShouldBe("Inactive");
        using (var again = await teacher.Client.PostAsync($"{TestsPath(teacher)}/{test.Id}/deactivate", new { version = inactive.Version }))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await again.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
        }

        (await ChangeAsync(teacher, test.Id, "publish", inactive.Version)).Status.ShouldBe("Published");
    }

    [Fact]
    public async Task After_attempts_only_non_destructive_edits_are_allowed_and_a_duplicate_is_editable()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var physics = await SubjectAsync(teacher, "physics");
        var test = await CreateAsync(teacher, Body(math, "Со обиди"));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE DiagnosticTests SET AttemptCount = 3 WHERE Id = {test.Id}", Ct);
        }

        object Same(Func<TestQuestionResponse, int, object>? question = null, Guid? subject = null, string title = "Со обиди") => new
        {
            title,
            slug = test.Slug,
            subjectId = subject ?? math,
            difficulty = "Intermediate",
            questions = test.Questions.Select((q, i) => question?.Invoke(q, i) ?? new
            {
                id = q.Id,
                text = q.Text,
                points = q.Points,
                options = q.Options.Select(o => new { id = (Guid?)o.Id, text = o.Text, isCorrect = o.IsCorrect }).ToArray(),
            }).ToArray(),
            version = test.Version,
        };

        var destructive = new (string Name, object Body)[]
        {
            ("points", Same((q, i) => new { id = q.Id, text = q.Text, points = q.Points + 1, options = q.Options.Select(o => new { id = (Guid?)o.Id, text = o.Text, isCorrect = o.IsCorrect }).ToArray() })),
            ("correct answer", Same((q, i) => new { id = q.Id, text = q.Text, points = q.Points, options = q.Options.Select(o => new { id = (Guid?)o.Id, text = o.Text, isCorrect = !o.IsCorrect }).ToArray() })),
            ("new option", Same((q, i) => new { id = q.Id, text = q.Text, points = q.Points, options = q.Options.Select(o => new { id = (Guid?)o.Id, text = o.Text, isCorrect = o.IsCorrect }).Append(new { id = (Guid?)null, text = "ново", isCorrect = false }).ToArray() })),
            ("subject", Same(subject: physics)),
        };
        foreach (var (name, body) in destructive)
        {
            using var response = await teacher.Client.PutAsync($"{TestsPath(teacher)}/{test.Id}", body);
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, name);
            (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.TestHasAttempts, name);
        }

        using (var removed = await teacher.Client.PutAsync($"{TestsPath(teacher)}/{test.Id}", Body(math, "Со обиди", version: test.Version)))
        {
            (await removed.ErrorCodeAsync()).ShouldBe(ErrorCodes.TestHasAttempts); // all-new questions = removed old ones
        }

        // Texts, title and difficulty may still be corrected.
        var fixedTypos = await UpdateAsync(teacher, test.Id, Same((q, i) => new
        {
            id = q.Id,
            text = q.Text + " (поправено)",
            points = q.Points,
            options = q.Options.Select(o => new { id = (Guid?)o.Id, text = o.Text + ".", isCorrect = o.IsCorrect }).ToArray(),
        }, title: "Со обиди — поправено"));
        fixedTypos.Questions[0].Text.ShouldEndWith("(поправено)");

        using var duplicated = await teacher.Client.PostAsync($"{TestsPath(teacher)}/{test.Id}/duplicate");
        duplicated.StatusCode.ShouldBe(HttpStatusCode.Created);
        var copy = (await duplicated.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
        (copy.Status, copy.AttemptCount, copy.Slug, copy.Title).ShouldBe(("Draft", 0, $"{test.Slug}-2", "Со обиди — поправено (2)"));
        copy.Questions.Select(q => q.Id).ShouldNotContain(test.Questions[0].Id);
        copy.Questions.Select(q => q.Points).ShouldBe(fixedTypos.Questions.Select(q => q.Points));
        var editedCopy = await UpdateAsync(teacher, copy.Id, Body(math, "Нова верзија", version: copy.Version));
        editedCopy.Title.ShouldBe("Нова верзија");
    }

    [Fact]
    public async Task A_published_test_cannot_be_saved_into_a_state_that_could_not_be_published()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var test = await CreateAsync(teacher, Body(math, "Објавен"));
        var published = await ChangeAsync(teacher, test.Id, "publish", test.Version);

        foreach (var (name, questions) in new (string, object[])[]
                 {
                     ("no questions", []),
                     ("no correct option", [new { id = published.Questions[0].Id, text = "П", points = 2, options = new[] { new { id = (Guid?)published.Questions[0].Options[0].Id, text = "3/4", isCorrect = false }, new { id = (Guid?)published.Questions[0].Options[1].Id, text = "2/6", isCorrect = false } } }]),
                 })
        {
            using var response = await teacher.Client.PutAsync($"{TestsPath(teacher)}/{test.Id}", new { title = "Објавен", subjectId = math, difficulty = "Beginner", questions, version = published.Version });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
            (await response.ReadJsonAsync()).GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldContain(n => n.StartsWith("questions", StringComparison.Ordinal), name);
        }

        var unchanged = await GetAsync(teacher, test.Id);
        (unchanged.Status, unchanged.Questions.Count, unchanged.Version).ShouldBe(("Published", 2, published.Version));

        // Deactivated, it may be reworked freely again.
        var inactive = await ChangeAsync(teacher, test.Id, "deactivate", published.Version);
        (await UpdateAsync(teacher, test.Id, new { title = "Во работа", subjectId = math, difficulty = "Beginner", questions = Array.Empty<object>(), version = inactive.Version })).Questions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Repeated_question_or_option_ids_are_refused()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var test = await CreateAsync(teacher, Body(math, "Со обиди"));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE DiagnosticTests SET AttemptCount = 1 WHERE Id = {test.Id}", Ct);
        }

        object Question(TestQuestionResponse q, TestOptionResponse[] options) =>
            new { id = q.Id, text = q.Text, points = q.Points, options = options.Select(o => new { id = (Guid?)o.Id, text = o.Text, isCorrect = o.IsCorrect }).ToArray() };
        var first = test.Questions[0];
        var second = test.Questions[1];

        foreach (var (name, questions) in new (string, object[])[]
                 {
                     // Same count as before, but the second question would silently disappear.
                     ("question twice", [Question(first, [.. first.Options]), Question(first, [.. first.Options])]),
                     // Three options as before, but option 3 would be replaced by a repeat of option 1.
                     ("option twice", [Question(first, [.. first.Options]), Question(second, [second.Options[0], second.Options[1], second.Options[0]])]),
                 })
        {
            using var response = await teacher.Client.PutAsync($"{TestsPath(teacher)}/{test.Id}", new { title = "Со обиди", slug = test.Slug, subjectId = math, difficulty = "Intermediate", questions, version = test.Version });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
        }

        var unchanged = await GetAsync(teacher, test.Id);
        unchanged.Questions.Select(q => q.Options.Count).ShouldBe([2, 3]);
    }

    [Fact]
    public async Task Archived_tests_leave_the_list_and_keep_their_address()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var test = await CreateAsync(teacher, Body(math, "Архива", slug: "arhiva"));
        await ChangeAsync(teacher, test.Id, "publish", test.Version);

        using (var archived = await teacher.Client.DeleteAsync($"{TestsPath(teacher)}/{test.Id}"))
        {
            archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await teacher.Client.GetJsonAsync<List<DiagnosticTestListItem>>(TestsPath(teacher))).ShouldBeEmpty();
        (await teacher.Client.GetAsync($"{TestsPath(teacher)}/{test.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CreateAsync(teacher, Body(math, "Архива"))).Slug.ShouldBe("arhiva-2"); // the archived address stays reserved... for its title-derived variant
        using var reuse = await teacher.Client.PostAsync(TestsPath(teacher), Body(math, "Друг", slug: "arhiva"));
        (await reuse.ErrorCodeAsync()).ShouldBe(ErrorCodes.TestSlugTaken);
    }

    [Fact]
    public async Task Tests_are_isolated_between_workspaces()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var test = await CreateAsync(teacher, Body(math, "Таен"));
        var path = $"{TestsPath(teacher)}/{test.Id}";

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, TestsPath(teacher));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, TestsPath(teacher), Body(math, "Х"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, path);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, path, Body(math, "Х", version: 0));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{path}/publish", new { version = 0 });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{path}/deactivate", new { version = 0 });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{path}/duplicate");
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Delete, path);
        (await intruder.Client.GetAsync($"{TestsPath(intruder)}/{test.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(teacher, test.Id)).Title.ShouldBe("Таен");
    }

    [Fact]
    public async Task The_database_keeps_questions_and_options_inside_their_workspace()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var mine = await CreateAsync(teacher, Body(math, "Мој"));
        var foreign = await CreateAsync(other, Body(math, "Туѓ"));

        await using var db = fixture.CreateDbContext();
        var error = await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE TestQuestions SET TestId = {foreign.Id} WHERE Id = {mine.Questions[0].Id}", Ct));
        error.Number.ShouldBe(547);
        var points = await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE TestQuestions SET Points = -1 WHERE Id = {mine.Questions[0].Id}", Ct));
        points.Number.ShouldBe(547);
    }

    private static object Body(Guid subject, string title, string? slug = null, int? version = null) => new
    {
        title,
        slug,
        subjectId = subject,
        description = "Проверка на основите.",
        difficulty = "Intermediate",
        questions = new object[]
        {
            new { text = "Колку е 1/2 + 1/4?", points = 2, options = new[] { new { text = "3/4", isCorrect = true }, new { text = "2/6", isCorrect = false } } },
            new { text = "Кои се прости броеви?", points = 3, options = new[] { new { text = "2", isCorrect = true }, new { text = "3", isCorrect = true }, new { text = "4", isCorrect = false } } },
        },
        version,
    };

    private static async Task<DiagnosticTestResponse> CreateAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PostAsync(TestsPath(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
    }

    private static async Task<DiagnosticTestResponse> UpdateAsync(TeacherSession teacher, Guid testId, object body)
    {
        using var response = await teacher.Client.PutAsync($"{TestsPath(teacher)}/{testId}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
    }

    private static async Task<DiagnosticTestResponse> ChangeAsync(TeacherSession teacher, Guid testId, string action, int version)
    {
        using var response = await teacher.Client.PostAsync($"{TestsPath(teacher)}/{testId}/{action}", new { version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
    }

    private static Task<DiagnosticTestResponse> GetAsync(TeacherSession teacher, Guid testId) =>
        teacher.Client.GetJsonAsync<DiagnosticTestResponse>($"{TestsPath(teacher)}/{testId}");

    private static string TestsPath(TeacherSession teacher) => $"/api/w/{teacher.WorkspaceId}/tests";

    private static async Task<Guid> SubjectAsync(TeacherSession teacher, string code) =>
        (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == code).Id;
}
