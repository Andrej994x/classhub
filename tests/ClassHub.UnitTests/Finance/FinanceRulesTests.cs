using ClassHub.Core.Modules.Finance.Domain;
using ClassHub.Core.SharedKernel.Errors;

namespace ClassHub.UnitTests.Finance;

/// <summary>R2-P10: balance math per currency, the consumption policy (D-22/D-25) and lesson corrections.</summary>
public sealed class FinanceRulesTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Balances_are_kept_per_currency_and_never_summed_across_them()
    {
        var balances = FinanceRules.Balances(
            packages: [("MKD", 480_000), ("EUR", 10_000), ("MKD", 120_000)],
            adjustments: [("MKD", -50_000)],
            payments: [("MKD", 200_000), ("MKD", 100_000), ("MKD", -100_000), ("EUR", 10_000), ("USD", 5_000)]);

        balances.Select(b => b.Currency).ShouldBe(["EUR", "MKD", "USD"]);
        balances.Single(b => b.Currency == "MKD").ShouldBe(new CurrencyTotals("MKD", 550_000, 200_000));
        balances.Single(b => b.Currency == "MKD").OutstandingMinor.ShouldBe(350_000);
        balances.Single(b => b.Currency == "EUR").OutstandingMinor.ShouldBe(0);
        balances.Single(b => b.Currency == "USD").OutstandingMinor.ShouldBe(-5_000); // paid in advance
    }

    [Fact]
    public void Nothing_is_owed_without_records() => FinanceRules.Balances([], [], []).ShouldBeEmpty();

    [Theory]
    [InlineData(true, true, false, ConsumptionAction.Consume)] // held, present, not yet consumed
    [InlineData(true, true, true, ConsumptionAction.None)] // already consumed: never twice
    [InlineData(true, false, true, ConsumptionAction.Reverse)] // attendance changed to absent/excused
    [InlineData(true, false, false, ConsumptionAction.None)] // absent: nothing
    [InlineData(false, true, false, ConsumptionAction.None)] // cancelled or not held: never consumes
    [InlineData(false, true, true, ConsumptionAction.Reverse)]
    public void Only_held_lessons_with_the_student_present_consume(bool held, bool present, bool hasActive, ConsumptionAction expected) =>
        FinanceRules.Reconcile(held, present, hasActive).ShouldBe(expected);

    [Fact]
    public void A_lesson_the_teacher_took_back_does_not_consume_again()
    {
        FinanceRules.Reconcile(held: true, present: true, hasActiveConsumption: false, waivedByTeacher: true).ShouldBe(ConsumptionAction.None);
        FinanceRules.Reconcile(held: true, present: false, hasActiveConsumption: true, waivedByTeacher: true).ShouldBe(ConsumptionAction.Reverse);
    }

    [Fact]
    public void Corrections_never_go_below_the_held_lessons_of_the_package()
    {
        // 1 lesson used, and it is a held lesson: giving it back by a correction would let a later attendance change
        // take it back a second time; it is given back by reversing its own entry.
        var package = new PackageUsage(Guid.NewGuid(), T0, 4, 1, Closed: false, ActiveLessons: 1);
        Should.Throw<BusinessRuleException>(() => FinanceRules.EnsureLessonAdjustment(package, -1)).Code.ShouldBe(ErrorCodes.PackageLessonsOutOfRange);
        FinanceRules.EnsureLessonAdjustment(package with { Used = 2 }, -1); // a no-show counted on top can be given back
    }

    [Fact]
    public void The_oldest_open_package_with_a_lesson_left_is_used()
    {
        var full = new PackageUsage(Guid.NewGuid(), T0, 4, 4, Closed: false);
        var closed = new PackageUsage(Guid.NewGuid(), T0.AddDays(1), 4, 0, Closed: true);
        var older = new PackageUsage(Guid.NewGuid(), T0.AddDays(2), 4, 1, Closed: false);
        var newer = new PackageUsage(Guid.NewGuid(), T0.AddDays(3), 8, 0, Closed: false);

        FinanceRules.PickPackage([newer, full, closed, older]).ShouldBe(older.Id);
        FinanceRules.PickPackage([full, closed]).ShouldBeNull(); // an unpaid lesson (D-25)
        FinanceRules.PickPackage([]).ShouldBeNull();
    }

    [Fact]
    public void Corrections_keep_the_used_lessons_within_the_package()
    {
        var package = new PackageUsage(Guid.NewGuid(), T0, 4, 3, Closed: false);

        FinanceRules.EnsureLessonAdjustment(package, 1);
        FinanceRules.EnsureLessonAdjustment(package, -3);
        Should.Throw<BusinessRuleException>(() => FinanceRules.EnsureLessonAdjustment(package, 2)).Code.ShouldBe(ErrorCodes.PackageLessonsOutOfRange);
        Should.Throw<BusinessRuleException>(() => FinanceRules.EnsureLessonAdjustment(package, -4)).Code.ShouldBe(ErrorCodes.PackageLessonsOutOfRange);
        package.Remaining.ShouldBe(1);
    }
}
