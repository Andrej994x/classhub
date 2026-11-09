namespace ClassHub.Core.Modules.Finance.Contracts;

/// <summary>Assign a package: from an offer (its terms are copied) or custom (title, lessons, price, currency).</summary>
public sealed record AssignPackageRequest(Guid? OfferId, string? Title, int? LessonCount, long? PriceMinor, string? Currency);

public sealed record PackageVersionRequest(int? Version);

/// <summary>A payment made outside ClassHub. <c>Method</c>: Cash, BankTransfer, Card or Other.</summary>
public sealed record RecordPaymentRequest(long? AmountMinor, string? Currency, DateOnly? PaidOn, string? Method, string? Note, Guid? PackageId);

/// <summary>Takes back a mistaken payment or ledger entry; the reason is kept.</summary>
public sealed record ReverseRequest(string? Reason);

/// <summary>A correction of what the student owes (+ more, − less), with a reason.</summary>
public sealed record BalanceAdjustmentRequest(long? AmountMinor, string? Currency, string? Reason);

/// <summary>A correction of a package's used lessons (+ uses, − gives back), with a reason and optionally the lesson.</summary>
public sealed record LessonAdjustmentRequest(int? Quantity, string? Reason, Guid? LessonId);

public sealed record CurrencyBalanceResponse(string Currency, long ChargedMinor, long PaidMinor, long OutstandingMinor);

public sealed record PackageResponse(
    Guid Id,
    string Title,
    Guid? OfferId,
    int LessonCount,
    int UsedLessons,
    int RemainingLessons,
    long PriceMinor,
    string Currency,
    bool Closed,
    DateTime CreatedAtUtc,
    int Version);

/// <summary>
/// One line of the student's finance history. <c>Kind</c>: PackageAssigned, Payment, PaymentReversal, Adjustment,
/// LessonConsumed, LessonAdjusted, LessonReversed.
/// </summary>
public sealed record FinanceEntryResponse(
    Guid Id,
    string Kind,
    DateTime OccurredAtUtc,
    long? AmountMinor,
    string? Currency,
    int? Lessons,
    Guid? PackageId,
    string? PackageTitle,
    Guid? LessonId,
    string? LessonTitle,
    DateOnly? PaidOn,
    string? Method,
    string? Note,
    bool Reversible);

/// <summary>The student's finance with one page of the history (newest first; every entry stays reachable).</summary>
public sealed record StudentFinanceResponse(
    Guid StudentId,
    IReadOnlyList<CurrencyBalanceResponse> Balances,
    IReadOnlyList<PackageResponse> Packages,
    int UnpaidLessons,
    IReadOnlyList<FinanceEntryResponse> Entries,
    int HistoryTotal,
    int HistoryPage,
    int HistoryPageSize);

/// <summary>A student in the finance overview: what they owe per currency and the lessons left.</summary>
public sealed record StudentFinanceSummary(Guid StudentId, string Name, bool Archived, IReadOnlyList<CurrencyBalanceResponse> Balances, int RemainingLessons, int UnpaidLessons);

public sealed record FinanceOverviewResponse(IReadOnlyList<StudentFinanceSummary> Students);

/// <summary>A period's money in one currency (never summed across currencies).</summary>
public sealed record CurrencyReportResponse(string Currency, int PackagesSold, long ChargedMinor, long ReceivedMinor);

public sealed record FinanceReportResponse(DateOnly From, DateOnly To, IReadOnlyList<CurrencyReportResponse> Currencies, int LessonsConsumed, int UnpaidLessons);
