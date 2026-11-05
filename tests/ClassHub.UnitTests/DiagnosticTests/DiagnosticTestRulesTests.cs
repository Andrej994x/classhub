using ClassHub.Core.Modules.DiagnosticTests;
using ClassHub.Core.Modules.DiagnosticTests.Application;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using ClassHub.Core.SharedKernel.Globalization;

namespace ClassHub.UnitTests.DiagnosticTests;

/// <summary>R1-P6: scoring, publish readiness, request validation and slugs.</summary>
public sealed class DiagnosticTestRulesTests
{
    private static readonly Guid Q1 = Guid.NewGuid(), Q2 = Guid.NewGuid(), Q3 = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    private static readonly ScoringQuestion[] Questions =
    [
        new(Q1, 2, new HashSet<Guid> { A }),
        new(Q2, 3, new HashSet<Guid> { B, C }), // several correct options
        new(Q3, 0, new HashSet<Guid> { D }), // worth nothing
    ];

    private static TestScore Score(params (Guid Question, Guid[] Chosen)[] answers) =>
        TestScoring.Score(Questions, answers.ToDictionary(a => a.Question, a => (IReadOnlyCollection<Guid>)a.Chosen));

    [Fact]
    public void All_correct_answers_earn_every_point() =>
        Score((Q1, [A]), (Q2, [C, B]), (Q3, [D])).ShouldBe(new TestScore(5, 5, 3, 3));

    [Fact]
    public void Several_correct_options_must_all_be_chosen_and_nothing_else()
    {
        Score((Q2, [B])).Points.ShouldBe(0);
        Score((Q2, [B, C, A])).Points.ShouldBe(0);
        Score((Q2, [B, C, B])).Points.ShouldBe(3); // duplicates do not matter
    }

    [Fact]
    public void Unanswered_unknown_and_empty_answers_earn_nothing()
    {
        Score().ShouldBe(new TestScore(0, 5, 0, 3));
        Score((Q1, [])).Points.ShouldBe(0);
        Score((Guid.NewGuid(), [A])).Points.ShouldBe(0); // unknown question
        Score((Q1, [Guid.NewGuid()])).Points.ShouldBe(0); // unknown option
    }

    [Fact]
    public void A_zero_point_question_counts_as_correct_without_points() =>
        Score((Q3, [D])).ShouldBe(new TestScore(0, 5, 1, 3));

    private static DiagnosticTestResponse Test(params TestQuestionResponse[] questions) =>
        new(Guid.NewGuid(), "Т", "t-1", Guid.NewGuid(), null, "Beginner", "Draft", null, 0, false, questions, questions.Sum(q => q.Points), DateTime.UtcNow, 1);

    private static TestQuestionResponse Question(int points, params bool[] correct) =>
        new(Guid.NewGuid(), "Колку е 2 + 2?", points, [.. correct.Select((c, i) => new TestOptionResponse(Guid.NewGuid(), $"{i}", c))]);

    [Fact]
    public void A_complete_test_is_ready_to_publish() =>
        DiagnosticTestService.PublishProblems(Test(Question(1, false, true), Question(0, true, false))).ShouldBeEmpty();

    [Fact]
    public void Publishing_needs_questions_options_a_correct_answer_and_points()
    {
        DiagnosticTestService.PublishProblems(Test()).Keys.ShouldBe(["questions"]);
        DiagnosticTestService.PublishProblems(Test(Question(0, true, false))).Keys.ShouldBe(["questions"]);
        DiagnosticTestService.PublishProblems(Test(Question(1, true))).Keys.ShouldBe(["questions[0].options"]);
        DiagnosticTestService.PublishProblems(Test(Question(1, true, false), Question(1, false, false))).Keys.ShouldBe(["questions[1].options"]);
    }

    private static SaveDiagnosticTestRequest Valid() =>
        new("Математика: дропки", "", Guid.NewGuid(), null, "Intermediate", [new SaveTestQuestion(null, "Колку е 1/2 + 1/4?", 2, [new(null, "3/4", true), new(null, "2/6", false)])]);

    [Fact]
    public void A_complete_request_is_valid()
    {
        var result = new SaveDiagnosticTestRequestValidator().Validate(Valid());
        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("slug")]
    [InlineData("difficulty")]
    [InlineData("points")]
    [InlineData("questionText")]
    [InlineData("optionText")]
    [InlineData("tooManyOptions")]
    public void Invalid_requests_are_refused(string problem)
    {
        var valid = Valid();
        var question = valid.Questions![0];
        var request = problem switch
        {
            "title" => valid with { Title = "  " },
            "slug" => valid with { Slug = "Fractions--1" },
            "difficulty" => valid with { Difficulty = "2" },
            "points" => valid with { Questions = [question with { Points = -1 }] },
            "questionText" => valid with { Questions = [question with { Text = "" }] },
            "optionText" => valid with { Questions = [question with { Options = [new(null, " ", true)] }] },
            _ => valid with { Questions = [question with { Options = [.. Enumerable.Range(0, TestQuestion.MaxOptions + 1).Select(i => new SaveTestOption(null, $"{i}", i == 0))] }] },
        };

        new SaveDiagnosticTestRequestValidator().Validate(request).IsValid.ShouldBeFalse(problem);
    }

    [Fact]
    public void A_null_question_is_a_validation_error_not_a_crash() =>
        new SaveDiagnosticTestRequestValidator().Validate(Valid() with { Questions = [null!] }).IsValid.ShouldBeFalse();

    [Theory]
    [InlineData("Математика: дропки", 60, "matematika-dropki")]
    [InlineData("Ѓорѓи и Џамбаз", 60, "gjorgji-i-djambaz")]
    [InlineData("!!!", 60, "")]
    [InlineData("a very long title that goes on", 10, "a-very-lon")]
    public void Slugs_are_made_from_text(string text, int max, string expected) => Slugs.FromText(text, max).ShouldBe(expected);
}
