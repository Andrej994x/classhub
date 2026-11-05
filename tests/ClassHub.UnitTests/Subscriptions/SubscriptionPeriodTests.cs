using ClassHub.Core.Modules.Subscriptions.Domain;

namespace ClassHub.UnitTests.Subscriptions;

public sealed class SubscriptionPeriodTests
{
    private static readonly Guid Free = Guid.NewGuid();
    private static readonly Guid Pro = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static WorkspaceSubscription Paid(DateTime start, DateTime? end) => new() { PlanId = Pro, StartsAtUtc = start, EndsAtUtc = end };

    [Fact]
    public void Without_a_subscription_the_default_plan_applies() =>
        SubscriptionPeriods.EffectivePlan(null, Free, Now).ShouldBe(Free);

    [Fact]
    public void A_paid_plan_applies_until_its_end_and_not_after()
    {
        var subscription = Paid(Now.AddMonths(-1), Now.AddSeconds(1));
        SubscriptionPeriods.EffectivePlan(subscription, Free, Now).ShouldBe(Pro);
        SubscriptionPeriods.EffectivePlan(subscription, Free, Now.AddSeconds(1)).ShouldBe(Free);
        SubscriptionPeriods.EffectivePlan(Paid(Now.AddMonths(-1), null), Free, Now).ShouldBe(Free);
    }

    [Fact]
    public void A_first_activation_starts_now()
    {
        var (kind, start, end) = SubscriptionPeriods.Add(null, Pro, 3, Now);
        kind.ShouldBe(SubscriptionEventKind.Activated);
        start.ShouldBe(Now);
        end.ShouldBe(Now.AddMonths(3));
    }

    [Fact]
    public void An_active_period_is_extended_from_its_end()
    {
        var current = Paid(Now.AddDays(-10), Now.AddDays(20));
        var (kind, start, end) = SubscriptionPeriods.Add(current, Pro, 1, Now);
        kind.ShouldBe(SubscriptionEventKind.Extended);
        start.ShouldBe(current.StartsAtUtc);
        end.ShouldBe(Now.AddDays(20).AddMonths(1));
    }

    [Fact]
    public void An_ended_period_starts_again_now()
    {
        var (kind, start, end) = SubscriptionPeriods.Add(Paid(Now.AddMonths(-3), Now.AddDays(-1)), Pro, 1, Now);
        kind.ShouldBe(SubscriptionEventKind.Activated);
        start.ShouldBe(Now);
        end.ShouldBe(Now.AddMonths(1));
    }

    [Fact]
    public void Months_must_be_positive() =>
        Should.Throw<ArgumentOutOfRangeException>(() => SubscriptionPeriods.Add(null, Pro, 0, Now));
}
