using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Finance.Domain;

/// <summary>
/// A package of lessons sold to a student (R2-P10). Title, lesson count, price and currency are a snapshot taken when it
/// is assigned: later changes of the offer never alter it (M11). Closed packages are no longer consumed.
/// </summary>
public sealed class StudentPackage : ITenantOwned
{
    public const int TitleMaxLength = 160;
    public const int MaxLessons = 500;
    public const long MaxPriceMinor = 100_000_000_000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>The offer it was copied from (null for a custom package).</summary>
    public Guid? OfferId { get; set; }

    public required string Title { get; set; }

    public int LessonCount { get; set; }

    /// <summary>The whole package's price in minor units (D-21).</summary>
    public long PriceMinor { get; set; }

    public required string Currency { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public int Version { get; set; }
}

public enum LedgerEntryKind
{
    /// <summary>A held lesson with the student present (D-22/D-25), recorded automatically.</summary>
    Lesson = 1,

    /// <summary>A teacher's correction with a reason (for example a no-show that counts).</summary>
    Manual = 2,

    /// <summary>Takes back one earlier entry (attendance changed, or the teacher's correction).</summary>
    Reversal = 3,
}

/// <summary>
/// The lesson ledger (append-only): every consumed lesson, correction and reversal is a new row; nothing is updated or
/// deleted. A lesson entry without a package is an unpaid lesson (D-25). At most one active lesson entry exists per
/// lesson and student: lesson entries are numbered per pair (<see cref="Occurrence"/>, unique) and a reversal names the
/// entry it takes back (unique).
/// </summary>
public sealed class LessonLedgerEntry : ITenantOwned, IAppendOnly
{
    public const int ReasonMaxLength = 500;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid StudentId { get; set; }

    public Guid? PackageId { get; set; }

    public Guid? LessonId { get; set; }

    public LedgerEntryKind Kind { get; set; }

    /// <summary>Lessons used (+) or given back (−).</summary>
    public int Quantity { get; set; }

    /// <summary>Lesson entries only: 0 for the first consumption of the pair, then 1, 2, … after each reversal.</summary>
    public int? Occurrence { get; set; }

    public Guid? ReversesId { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
}

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Card = 3,
    Other = 4,
}

/// <summary>
/// A payment the student made outside ClassHub (M11), recorded by the teacher; full or partial. A mistaken payment is
/// corrected by a reversal row (negative amount naming the payment), never by changing it.
/// </summary>
public sealed class Payment : ITenantOwned, IAppendOnly
{
    public const int NoteMaxLength = 500;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid StudentId { get; set; }

    public Guid? PackageId { get; set; }

    public long AmountMinor { get; set; }

    public required string Currency { get; set; }

    /// <summary>The day it was paid (local to the workspace).</summary>
    public DateOnly PaidOn { get; set; }

    public PaymentMethod Method { get; set; }

    public string? Note { get; set; }

    public Guid? ReversesPaymentId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
}

/// <summary>A correction of what the student owes, with a reason (+ owes more, − owes less).</summary>
public sealed class BalanceAdjustment : ITenantOwned, IAppendOnly
{
    public const int ReasonMaxLength = 500;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid StudentId { get; set; }

    public long AmountMinor { get; set; }

    public required string Currency { get; set; }

    public required string Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
}

/// <summary>A student's money in one currency (never summed across currencies, D-21).</summary>
public sealed record CurrencyTotals(string Currency, long ChargedMinor, long PaidMinor)
{
    /// <summary>What the student still owes (negative: paid in advance).</summary>
    public long OutstandingMinor => ChargedMinor - PaidMinor;
}

public enum ConsumptionAction
{
    None,
    Consume,
    Reverse,
}

/// <summary>
/// A package as the consumption policy sees it. <see cref="ActiveLessons"/>: held lessons consumed from it that were not
/// taken back (they are part of <see cref="Used"/>).
/// </summary>
public sealed record PackageUsage(Guid Id, DateTime CreatedAtUtc, int LessonCount, int Used, bool Closed, int ActiveLessons = 0)
{
    public int Remaining => LessonCount - Used;
}

/// <summary>The finance rules (R2-P10, D-21/D-22/D-25).</summary>
public static class FinanceRules
{
    /// <summary>
    /// Balances per currency: packages and adjustments are charged, payments (net of reversals) are paid. Currencies are
    /// kept apart and listed in a stable order.
    /// </summary>
    public static IReadOnlyList<CurrencyTotals> Balances(
        IEnumerable<(string Currency, long AmountMinor)> packages,
        IEnumerable<(string Currency, long AmountMinor)> adjustments,
        IEnumerable<(string Currency, long AmountMinor)> payments)
    {
        var charged = new Dictionary<string, long>(StringComparer.Ordinal);
        var paid = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (currency, amount) in packages.Concat(adjustments))
        {
            charged[currency] = checked(charged.GetValueOrDefault(currency) + amount);
        }

        foreach (var (currency, amount) in payments)
        {
            paid[currency] = checked(paid.GetValueOrDefault(currency) + amount);
        }

        return [.. charged.Keys.Union(paid.Keys).Order(StringComparer.Ordinal)
            .Select(c => new CurrencyTotals(c, charged.GetValueOrDefault(c), paid.GetValueOrDefault(c)))];
    }

    /// <summary>
    /// D-22: only a held lesson with the student Present consumes automatically; a change of attendance takes the
    /// consumption back. Cancelled (never held) lessons never consume, and a lesson the teacher took back stays waived.
    /// </summary>
    public static ConsumptionAction Reconcile(bool held, bool present, bool hasActiveConsumption, bool waivedByTeacher = false) =>
        (held && present, hasActiveConsumption) switch
        {
            // The teacher took a held lesson back (reversed its entry): it does not consume again automatically.
            (true, false) when waivedByTeacher => ConsumptionAction.None,
            (true, false) => ConsumptionAction.Consume,
            (false, true) => ConsumptionAction.Reverse,
            _ => ConsumptionAction.None,
        };

    /// <summary>
    /// D-25: the student's oldest open package with a lesson left; none (an unpaid lesson) when there is no such package.
    /// </summary>
    public static Guid? PickPackage(IEnumerable<PackageUsage> packages) =>
        packages.Where(p => !p.Closed && p.Remaining > 0).OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id).Select(p => (Guid?)p.Id).FirstOrDefault();

    /// <summary>
    /// A teacher's correction keeps the package's used lessons within its size and never below its held lessons that
    /// were not taken back: such a lesson is given back by reversing its own entry, so an attendance change can always
    /// take it back later without leaving the package below zero.
    /// </summary>
    public static void EnsureLessonAdjustment(PackageUsage package, int quantity)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Used + quantity < package.ActiveLessons || package.Used + quantity > package.LessonCount)
        {
            throw new BusinessRuleException(
                $"The package has {package.LessonCount} lessons, {package.Used} used ({package.ActiveLessons} held); this correction would leave {package.Used + quantity}.",
                ErrorCodes.PackageLessonsOutOfRange);
        }
    }
}
