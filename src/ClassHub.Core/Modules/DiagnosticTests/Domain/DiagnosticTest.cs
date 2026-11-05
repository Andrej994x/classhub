using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.DiagnosticTests.Domain;

public enum TestDifficulty
{
    Beginner = 1,
    Intermediate = 2,
    Advanced = 3,
}

/// <summary>Draft (being prepared, never public), Published (public at /{site}/tests/{slug}), Inactive (taken offline).</summary>
public enum TestStatus
{
    Draft = 1,
    Published = 2,
    Inactive = 3,
}

/// <summary>
/// A diagnostic test (M04): multiple-choice questions with points, used to attract students. Archived tests are hidden
/// and keep their history. Once visitors have taken it (attempts, R1-P7), destructive edits are blocked (D-38).
/// </summary>
public sealed class DiagnosticTest : ITenantOwned, IArchivable
{
    public const int TitleMaxLength = 150;
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 60;
    public const int DescriptionMaxLength = 2000;
    public const int MaxQuestions = 50;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string Title { get; set; }

    /// <summary>Address within the teacher's site (unique per workspace): <c>/{siteSlug}/tests/{slug}</c>.</summary>
    public required string Slug { get; set; }

    public Guid SubjectId { get; set; }

    public string? Description { get; set; }

    public TestDifficulty Difficulty { get; set; }

    public TestStatus Status { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>Visitor attempts so far (written by the public test taking, R1-P7). Above zero, D-38 applies.</summary>
    public int AttemptCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public int Version { get; set; }

    /// <summary>
    /// Set by platform moderation (R1-P11): the test is not public (no page, no attempts, not listed or in the
    /// sitemap) until moderation shows it again. Independent of the status; the teacher cannot clear it.
    /// </summary>
    public DateTime? HiddenAtUtc { get; set; }

    public string? HiddenReason { get; set; }
}

public sealed class TestQuestion : ITenantOwned
{
    public const int TextMaxLength = 1000;
    public const int MaxPoints = 100;
    public const int MinOptions = 2;
    public const int MaxOptions = 8;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid TestId { get; set; }

    public int Position { get; set; }

    public required string Text { get; set; }

    public int Points { get; set; }
}

public sealed class TestOption : ITenantOwned
{
    public const int TextMaxLength = 300;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid QuestionId { get; set; }

    public int Position { get; set; }

    public required string Text { get; set; }

    /// <summary>Never part of any public projection (architecture test).</summary>
    public bool IsCorrect { get; set; }
}

/// <summary>A question for scoring: its points and the ids of its correct options.</summary>
public sealed record ScoringQuestion(Guid Id, int Points, IReadOnlySet<Guid> CorrectOptionIds);

public sealed record TestScore(int Points, int MaxPoints, int CorrectQuestions, int TotalQuestions);

/// <summary>
/// Server-side scoring (M04: "the result follows the defined correct answers"). A question earns its points only when
/// exactly its correct options are chosen (all-or-nothing, also with several correct options). Answers to unknown
/// questions and unknown options are ignored; an unanswered question earns nothing.
/// </summary>
public static class TestScoring
{
    public static TestScore Score(IReadOnlyList<ScoringQuestion> questions, IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> answers)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(answers);
        var points = 0;
        var correct = 0;
        foreach (var question in questions)
        {
            if (answers.TryGetValue(question.Id, out var chosen) && chosen.ToHashSet().SetEquals(question.CorrectOptionIds))
            {
                points += question.Points;
                correct++;
            }
        }

        return new TestScore(points, questions.Sum(q => q.Points), correct, questions.Count);
    }
}
