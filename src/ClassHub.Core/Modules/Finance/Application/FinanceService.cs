using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Finance.Contracts;
using ClassHub.Core.Modules.Finance.Domain;
using ClassHub.Core.Modules.Finance.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Globalization;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Finance.Application;

/// <summary>
/// Packages and payment tracking (M11, R2-P10): packages with snapshotted terms, payments made outside ClassHub, balance
/// and lesson corrections with reasons, and reversals. Everything is appended (the history is the record); money is kept
/// per currency and never summed across currencies (D-21). Every change is audited.
/// </summary>
internal sealed class FinanceService(
    ClassHubDbContext db,
    FinanceQueries queries,
    ITenantContext tenant,
    IStudentLookup students,
    IOfferLookup offers,
    ILessonLookup lessons,
    IWorkspaceSettings workspace,
    IAuditLog audit,
    TimeProvider clock)
{
    private const int HistoryLimit = 200;
    private const long MaxAmountMinor = StudentPackage.MaxPriceMinor;

    public async Task<StudentFinanceResponse> GetStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        await RequireStudentAsync(studentId, cancellationToken);
        var packages = await db.Set<StudentPackage>().AsNoTracking().Where(p => p.StudentId == studentId).ToListAsync(cancellationToken);
        var ledger = await db.Set<LessonLedgerEntry>().AsNoTracking().Where(e => e.StudentId == studentId).ToListAsync(cancellationToken);
        var payments = await db.Set<Payment>().AsNoTracking().Where(p => p.StudentId == studentId).ToListAsync(cancellationToken);
        var adjustments = await db.Set<BalanceAdjustment>().AsNoTracking().Where(a => a.StudentId == studentId).ToListAsync(cancellationToken);

        var used = ledger.Where(e => e.PackageId is not null).GroupBy(e => e.PackageId!.Value).ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity));
        var titles = packages.ToDictionary(p => p.Id, p => p.Title);
        var lessonTitles = await lessons.FindAsync([.. ledger.Where(e => e.LessonId is not null).Select(e => e.LessonId!.Value).Distinct()], cancellationToken);
        var reversedEntries = ledger.Where(e => e.ReversesId is not null).Select(e => e.ReversesId!.Value).ToHashSet();
        var reversedPayments = payments.Where(p => p.ReversesPaymentId is not null).Select(p => p.ReversesPaymentId!.Value).ToHashSet();

        var entries = new List<FinanceEntryResponse>();
        entries.AddRange(packages.Select(p => new FinanceEntryResponse(p.Id, "PackageAssigned", p.CreatedAtUtc, p.PriceMinor, p.Currency, p.LessonCount, p.Id, p.Title, null, null, null, null, null, false)));
        entries.AddRange(payments.Select(p => new FinanceEntryResponse(p.Id, p.ReversesPaymentId is null ? "Payment" : "PaymentReversal", p.CreatedAtUtc, p.AmountMinor, p.Currency, null,
            p.PackageId, p.PackageId is { } pid ? titles.GetValueOrDefault(pid) : null, null, null, p.PaidOn, p.Method.ToString(), p.Note,
            p.ReversesPaymentId is null && !reversedPayments.Contains(p.Id))));
        entries.AddRange(adjustments.Select(a => new FinanceEntryResponse(a.Id, "Adjustment", a.CreatedAtUtc, a.AmountMinor, a.Currency, null, null, null, null, null, null, null, a.Reason, false)));
        entries.AddRange(ledger.Select(e => new FinanceEntryResponse(e.Id,
            e.Kind switch { LedgerEntryKind.Lesson => "LessonConsumed", LedgerEntryKind.Manual => "LessonAdjusted", _ => "LessonReversed" },
            e.CreatedAtUtc, null, null, e.Quantity, e.PackageId, e.PackageId is { } pid ? titles.GetValueOrDefault(pid) : null,
            e.LessonId, e.LessonId is { } lid && lessonTitles.TryGetValue(lid, out var lesson) ? lesson.Title : null, null, null, e.Reason,
            e.Kind != LedgerEntryKind.Reversal && !reversedEntries.Contains(e.Id))));

        return new StudentFinanceResponse(
            studentId,
            [.. Balances(packages, adjustments, payments)],
            [.. packages.OrderByDescending(p => p.CreatedAtUtc).Select(p => ToPackage(p, used.GetValueOrDefault(p.Id)))],
            ledger.Where(e => e.PackageId is null).Sum(e => e.Quantity),
            [.. entries.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).Take(HistoryLimit)]);
    }

    /// <summary>Assigns a package with its terms copied now: from an offer, or custom.</summary>
    public async Task<StudentFinanceResponse> AssignPackageAsync(Guid studentId, AssignPackageRequest request, CancellationToken cancellationToken)
    {
        var student = await RequireStudentAsync(studentId, cancellationToken);
        if (student.Archived)
        {
            throw new BusinessRuleException("Restore the student before assigning a package.", ErrorCodes.InvalidStatusTransition);
        }

        string title;
        int lessonCount;
        long price;
        string currency;
        if (request.OfferId is { } offerId)
        {
            var terms = await offers.TermsAsync(offerId, cancellationToken) ?? throw Invalid("offerId", "Unknown offer.");
            (title, lessonCount, price, currency) = (terms.Title, terms.LessonCount, terms.PriceMinor, terms.Currency);
        }
        else
        {
            (title, lessonCount, price, currency) = (request.Title!.Trim(), request.LessonCount!.Value, request.PriceMinor!.Value, request.Currency!);
        }

        var package = new StudentPackage
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            StudentId = studentId,
            OfferId = request.OfferId,
            Title = title.Length > StudentPackage.TitleMaxLength ? title[..StudentPackage.TitleMaxLength] : title,
            LessonCount = Math.Clamp(lessonCount, 1, StudentPackage.MaxLessons),
            PriceMinor = price,
            Currency = currency,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = tenant.UserId,
        };
        db.Set<StudentPackage>().Add(package);
        audit.Record("finance.package.assigned", nameof(StudentPackage), package.Id.ToString(), package.WorkspaceId,
            after: new { package.StudentId, package.OfferId, package.Title, package.LessonCount, package.PriceMinor, package.Currency });
        await db.SaveChangesAsync(cancellationToken);
        return await GetStudentAsync(studentId, cancellationToken);
    }

    /// <summary>A closed package is no longer consumed (what it cost stays charged).</summary>
    public async Task<StudentFinanceResponse> ClosePackageAsync(Guid packageId, PackageVersionRequest request, CancellationToken cancellationToken)
    {
        var package = await db.Set<StudentPackage>().SingleOrDefaultAsync(p => p.Id == packageId, cancellationToken) ?? throw new NotFoundException();
        if (package.Version != request.Version)
        {
            throw Conflict();
        }

        if (package.ClosedAtUtc is null)
        {
            package.ClosedAtUtc = clock.GetUtcNow().UtcDateTime;
            package.Version++;
            audit.Record("finance.package.closed", nameof(StudentPackage), package.Id.ToString(), package.WorkspaceId, after: new { package.StudentId });
            await SaveAsync(cancellationToken);
        }

        return await GetStudentAsync(package.StudentId, cancellationToken);
    }

    public async Task<StudentFinanceResponse> RecordPaymentAsync(Guid studentId, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        await RequireStudentAsync(studentId, cancellationToken);
        var today = await TodayAsync(cancellationToken);
        if (request.PaidOn > today.AddDays(1))
        {
            throw Invalid("paidOn", "The payment date cannot be in the future.");
        }

        if (request.PackageId is { } packageId)
        {
            var package = await db.Set<StudentPackage>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == packageId && p.StudentId == studentId, cancellationToken)
                ?? throw Invalid("packageId", "Unknown package.");
            if (package.Currency != request.Currency)
            {
                throw Invalid("currency", $"The package is in {package.Currency}.");
            }
        }

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            StudentId = studentId,
            PackageId = request.PackageId,
            AmountMinor = request.AmountMinor!.Value,
            Currency = request.Currency!,
            PaidOn = request.PaidOn!.Value,
            Method = Enum.Parse<PaymentMethod>(request.Method!),
            Note = Clean(request.Note),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = tenant.UserId,
        };
        db.Set<Payment>().Add(payment);
        audit.Record("finance.payment.recorded", nameof(Payment), payment.Id.ToString(), payment.WorkspaceId,
            after: new { payment.StudentId, payment.AmountMinor, payment.Currency, payment.PaidOn, Method = payment.Method.ToString(), payment.PackageId });
        await db.SaveChangesAsync(cancellationToken);
        return await GetStudentAsync(studentId, cancellationToken);
    }

    /// <summary>Takes back a mistaken payment with a reversal row (the payment itself never changes); once only.</summary>
    public async Task<StudentFinanceResponse> ReversePaymentAsync(Guid paymentId, ReverseRequest request, CancellationToken cancellationToken)
    {
        var payment = await db.Set<Payment>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken) ?? throw new NotFoundException();
        if (payment.ReversesPaymentId is not null)
        {
            throw new BusinessRuleException("A reversal cannot be reversed.", ErrorCodes.InvalidStatusTransition);
        }

        if (await db.Set<Payment>().AnyAsync(p => p.ReversesPaymentId == paymentId, cancellationToken))
        {
            throw AlreadyReversed();
        }

        var reversal = new Payment
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = payment.WorkspaceId,
            StudentId = payment.StudentId,
            PackageId = payment.PackageId,
            AmountMinor = -payment.AmountMinor,
            Currency = payment.Currency,
            PaidOn = await TodayAsync(cancellationToken),
            Method = payment.Method,
            Note = request.Reason!.Trim(),
            ReversesPaymentId = payment.Id,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = tenant.UserId,
        };
        db.Set<Payment>().Add(reversal);
        audit.Record("finance.payment.reversed", nameof(Payment), payment.Id.ToString(), payment.WorkspaceId, after: new { ReversalId = reversal.Id, Reason = reversal.Note });
        await SaveUniqueAsync(cancellationToken);
        return await GetStudentAsync(payment.StudentId, cancellationToken);
    }

    public async Task<StudentFinanceResponse> AdjustBalanceAsync(Guid studentId, BalanceAdjustmentRequest request, CancellationToken cancellationToken)
    {
        await RequireStudentAsync(studentId, cancellationToken);
        var adjustment = new BalanceAdjustment
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            StudentId = studentId,
            AmountMinor = request.AmountMinor!.Value,
            Currency = request.Currency!,
            Reason = request.Reason!.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = tenant.UserId,
        };
        db.Set<BalanceAdjustment>().Add(adjustment);
        audit.Record("finance.balance.adjusted", nameof(BalanceAdjustment), adjustment.Id.ToString(), adjustment.WorkspaceId,
            after: new { adjustment.StudentId, adjustment.AmountMinor, adjustment.Currency, adjustment.Reason });
        await db.SaveChangesAsync(cancellationToken);
        return await GetStudentAsync(studentId, cancellationToken);
    }

    /// <summary>A teacher's correction of a package's used lessons (D-22: e.g. a no-show that counts), with a reason.</summary>
    public async Task<StudentFinanceResponse> AdjustLessonsAsync(Guid packageId, LessonAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var package = await db.Set<StudentPackage>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == packageId, cancellationToken) ?? throw new NotFoundException();
        if (request.LessonId is { } lessonId && !(await lessons.FindAsync([lessonId], cancellationToken)).ContainsKey(lessonId))
        {
            throw Invalid("lessonId", "Unknown lesson.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockStudentAsync(package.StudentId, cancellationToken);
        var usage = (await PackageUsages.OfStudentAsync(db, package.StudentId, cancellationToken)).Single(p => p.Id == packageId);
        FinanceRules.EnsureLessonAdjustment(usage, request.Quantity!.Value);
        var entry = new LessonLedgerEntry
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = package.WorkspaceId,
            StudentId = package.StudentId,
            PackageId = package.Id,
            LessonId = request.LessonId,
            Kind = LedgerEntryKind.Manual,
            Quantity = request.Quantity.Value,
            Reason = request.Reason!.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = tenant.UserId,
        };
        db.Set<LessonLedgerEntry>().Add(entry);
        audit.Record("finance.lessons.adjusted", nameof(LessonLedgerEntry), entry.Id.ToString(), entry.WorkspaceId,
            after: new { entry.StudentId, entry.PackageId, entry.LessonId, entry.Quantity, entry.Reason });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetStudentAsync(package.StudentId, cancellationToken);
    }

    /// <summary>Takes back a lesson entry or correction with a reversal row (once); the package's lessons stay in range.</summary>
    public async Task<StudentFinanceResponse> ReverseEntryAsync(Guid entryId, ReverseRequest request, CancellationToken cancellationToken)
    {
        var entry = await db.Set<LessonLedgerEntry>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken) ?? throw new NotFoundException();
        if (entry.Kind == LedgerEntryKind.Reversal)
        {
            throw new BusinessRuleException("A reversal cannot be reversed.", ErrorCodes.InvalidStatusTransition);
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await queries.LockStudentAsync(entry.StudentId, cancellationToken);
            if (await db.Set<LessonLedgerEntry>().AnyAsync(e => e.ReversesId == entryId, cancellationToken))
            {
                throw AlreadyReversed();
            }

            if (entry.PackageId is { } packageId)
            {
                FinanceRules.EnsureLessonAdjustment((await PackageUsages.OfStudentAsync(db, entry.StudentId, cancellationToken)).Single(p => p.Id == packageId), -entry.Quantity);
            }

            var reversal = new LessonLedgerEntry
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = entry.WorkspaceId,
                StudentId = entry.StudentId,
                PackageId = entry.PackageId,
                LessonId = entry.LessonId,
                Kind = LedgerEntryKind.Reversal,
                Quantity = -entry.Quantity,
                ReversesId = entry.Id,
                Reason = request.Reason!.Trim(),
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
                CreatedByUserId = tenant.UserId,
            };
            db.Set<LessonLedgerEntry>().Add(reversal);
            audit.Record("finance.lessons.reversed", nameof(LessonLedgerEntry), entry.Id.ToString(), entry.WorkspaceId, after: new { ReversalId = reversal.Id, reversal.Reason });
            await SaveUniqueAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetStudentAsync(entry.StudentId, cancellationToken);
    }

    /// <summary>Every student with finance records: what they owe per currency and the lessons left.</summary>
    public async Task<FinanceOverviewResponse> OverviewAsync(CancellationToken cancellationToken)
    {
        var packages = await db.Set<StudentPackage>().AsNoTracking().ToListAsync(cancellationToken);
        var ledger = await db.Set<LessonLedgerEntry>().AsNoTracking().Select(e => new { e.StudentId, e.PackageId, e.Quantity }).ToListAsync(cancellationToken);
        var payments = await db.Set<Payment>().AsNoTracking().Select(p => new { p.StudentId, p.Currency, p.AmountMinor }).ToListAsync(cancellationToken);
        var adjustments = await db.Set<BalanceAdjustment>().AsNoTracking().Select(a => new { a.StudentId, a.Currency, a.AmountMinor }).ToListAsync(cancellationToken);
        var ids = packages.Select(p => p.StudentId).Concat(ledger.Select(e => e.StudentId)).Concat(payments.Select(p => p.StudentId)).Concat(adjustments.Select(a => a.StudentId)).ToHashSet();
        var names = await students.FindAsync(ids, cancellationToken);
        var used = ledger.Where(e => e.PackageId is not null).GroupBy(e => e.PackageId!.Value).ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity));

        return new FinanceOverviewResponse([.. ids.Where(names.ContainsKey).Select(id =>
            {
                var student = names[id];
                var own = packages.Where(p => p.StudentId == id).ToList();
                var balances = FinanceRules.Balances(
                    own.Select(p => (p.Currency, p.PriceMinor)),
                    adjustments.Where(a => a.StudentId == id).Select(a => (a.Currency, a.AmountMinor)),
                    payments.Where(p => p.StudentId == id).Select(p => (p.Currency, p.AmountMinor)));
                return new StudentFinanceSummary(id, Name(student), student.Archived,
                    [.. balances.Select(b => new CurrencyBalanceResponse(b.Currency, b.ChargedMinor, b.PaidMinor, b.OutstandingMinor))],
                    own.Where(p => p.ClosedAtUtc is null).Sum(p => p.LessonCount - used.GetValueOrDefault(p.Id)),
                    ledger.Where(e => e.StudentId == id && e.PackageId is null).Sum(e => e.Quantity));
            })
            .OrderBy(s => s.Archived).ThenBy(s => s.Name, StringComparer.CurrentCulture)]);
    }

    /// <summary>A period's money per currency (local days of the workspace) and the lessons consumed in it.</summary>
    public async Task<FinanceReportResponse> ReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            throw Invalid("to", "Choose a period of at most one year, ending on or after its start.");
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(await workspace.GetTimeZoneAsync(cancellationToken));
        var fromUtc = StartOfDayUtc(from, zone);
        var toUtc = StartOfDayUtc(to.AddDays(1), zone);
        var packages = await db.Set<StudentPackage>().AsNoTracking().Where(p => p.CreatedAtUtc >= fromUtc && p.CreatedAtUtc < toUtc)
            .Select(p => new { p.Currency, p.PriceMinor }).ToListAsync(cancellationToken);
        var adjustments = await db.Set<BalanceAdjustment>().AsNoTracking().Where(a => a.CreatedAtUtc >= fromUtc && a.CreatedAtUtc < toUtc)
            .Select(a => new { a.Currency, a.AmountMinor }).ToListAsync(cancellationToken);
        var payments = await db.Set<Payment>().AsNoTracking().Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .Select(p => new { p.Currency, p.AmountMinor }).ToListAsync(cancellationToken);
        var lessonsInPeriod = await db.Set<LessonLedgerEntry>().AsNoTracking().Where(e => e.CreatedAtUtc >= fromUtc && e.CreatedAtUtc < toUtc && e.LessonId != null)
            .Select(e => new { e.PackageId, e.Quantity }).ToListAsync(cancellationToken);

        var currencies = packages.Select(p => p.Currency).Concat(adjustments.Select(a => a.Currency)).Concat(payments.Select(p => p.Currency)).Distinct().Order(StringComparer.Ordinal);
        return new FinanceReportResponse(from, to,
            [.. currencies.Select(c => new CurrencyReportResponse(c,
                packages.Count(p => p.Currency == c),
                packages.Where(p => p.Currency == c).Sum(p => p.PriceMinor) + adjustments.Where(a => a.Currency == c).Sum(a => a.AmountMinor),
                payments.Where(p => p.Currency == c).Sum(p => p.AmountMinor)))],
            lessonsInPeriod.Sum(e => e.Quantity),
            lessonsInPeriod.Where(e => e.PackageId is null).Sum(e => e.Quantity));
    }

    private static IEnumerable<CurrencyBalanceResponse> Balances(List<StudentPackage> packages, List<BalanceAdjustment> adjustments, List<Payment> payments) =>
        FinanceRules.Balances(
                packages.Select(p => (p.Currency, p.PriceMinor)),
                adjustments.Select(a => (a.Currency, a.AmountMinor)),
                payments.Select(p => (p.Currency, p.AmountMinor)))
            .Select(b => new CurrencyBalanceResponse(b.Currency, b.ChargedMinor, b.PaidMinor, b.OutstandingMinor));

    private static PackageResponse ToPackage(StudentPackage p, int used) =>
        new(p.Id, p.Title, p.OfferId, p.LessonCount, used, p.LessonCount - used, p.PriceMinor, p.Currency, p.ClosedAtUtc is not null, p.CreatedAtUtc, p.Version);

    private async Task<StudentSummary> RequireStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        (await students.FindAsync([studentId], cancellationToken)).GetValueOrDefault(studentId) ?? throw new NotFoundException();

    private async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await workspace.GetTimeZoneAsync(cancellationToken));
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, zone));
    }

    private static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        return zone.IsAmbiguousTime(local)
            ? DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc)
            : TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static string Name(StudentSummary s) => string.Join(' ', new[] { s.FirstName, s.LastName }.Where(n => !string.IsNullOrEmpty(n)));

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict();
        }
    }

    /// <summary>Saves a reversal; a parallel reversal of the same entry or payment loses on the unique index.</summary>
    private async Task SaveUniqueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw AlreadyReversed();
        }
    }

    private static ConflictException AlreadyReversed() => new("This was already reversed.", ErrorCodes.ConcurrencyConflict);

    private static ConflictException Conflict() => new("The package was changed elsewhere. Reload and try again.", ErrorCodes.ConcurrencyConflict);

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static bool ValidCurrency(string? currency) => Currencies.IsActiveIsoCode(currency);

    internal const long MaxAmount = MaxAmountMinor;
}
