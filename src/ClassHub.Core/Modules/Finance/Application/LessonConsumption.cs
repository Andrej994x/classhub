using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Finance.Domain;
using ClassHub.Core.Modules.Finance.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Finance.Application;

/// <summary>
/// Turns lesson events into ledger entries (D-22/D-25), in a scope bound to the event's workspace. Each event reconciles
/// the ledger with the lesson's <em>current</em> attendance, so events may arrive late, twice or out of order: a held
/// lesson consumes once per present student (from their oldest open package with a lesson left, otherwise as an unpaid
/// lesson), a changed attendance takes the consumption back, and nothing else consumes. The entries and the event's
/// processed mark commit together, under each student's finance lock.
/// </summary>
internal sealed class LessonConsumption(ClassHubDbContext db, FinanceQueries queries, ILessonLookup lessons, ILessonEventFeed feed, ITenantContext tenant, TimeProvider clock)
{
    /// <summary>Handles one event; false when another worker had already handled it.</summary>
    public async Task<bool> ProcessAsync(PendingLessonEvent lessonEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lessonEvent);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var facts = await lessons.AttendanceAsync(lessonEvent.LessonId, cancellationToken);
            if (facts is not null)
            {
                await ReconcileAsync(facts, cancellationToken);
            }

            if (!await feed.MarkProcessedAsync(lessonEvent.Id, cancellationToken))
            {
                return false; // handled meanwhile: roll back (nothing of ours is kept)
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return false; // another worker consumed this lesson first
        }
    }

    private async Task ReconcileAsync(LessonAttendanceFacts facts, CancellationToken cancellationToken)
    {
        var ledger = db.Set<LessonLedgerEntry>();
        // Students with an active consumption who are no longer present (or no longer participants) give it back.
        var consumed = await ActiveLessonEntries(facts.LessonId).Select(e => e.StudentId).ToListAsync(cancellationToken);
        var students = facts.Participants.Select(p => p.StudentId).Union(consumed).Order().ToList(); // a fixed lock order
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var studentId in students)
        {
            await queries.LockStudentAsync(studentId, cancellationToken);
            var active = await ActiveLessonEntries(facts.LessonId).SingleOrDefaultAsync(e => e.StudentId == studentId, cancellationToken);
            var present = facts.Participants.Any(p => p.StudentId == studentId && p.Present);
            switch (FinanceRules.Reconcile(facts.Held, present, active is not null))
            {
                case ConsumptionAction.Consume:
                    var occurrence = await ledger.CountAsync(e => e.Kind == LedgerEntryKind.Lesson && e.LessonId == facts.LessonId && e.StudentId == studentId, cancellationToken);
                    ledger.Add(new LessonLedgerEntry
                    {
                        Id = Guid.CreateVersion7(),
                        WorkspaceId = tenant.RequireWorkspaceId(),
                        StudentId = studentId,
                        PackageId = FinanceRules.PickPackage(await PackageUsages.OfStudentAsync(db, studentId, cancellationToken)),
                        LessonId = facts.LessonId,
                        Kind = LedgerEntryKind.Lesson,
                        Quantity = 1,
                        Occurrence = occurrence,
                        CreatedAtUtc = now,
                    });
                    break;
                case ConsumptionAction.Reverse:
                    ledger.Add(new LessonLedgerEntry
                    {
                        Id = Guid.CreateVersion7(),
                        WorkspaceId = tenant.RequireWorkspaceId(),
                        StudentId = studentId,
                        PackageId = active!.PackageId,
                        LessonId = facts.LessonId,
                        Kind = LedgerEntryKind.Reversal,
                        Quantity = -active.Quantity,
                        ReversesId = active.Id,
                        Reason = "attendance",
                        CreatedAtUtc = now,
                    });
                    break;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Lesson entries of the lesson that no reversal took back.</summary>
    private IQueryable<LessonLedgerEntry> ActiveLessonEntries(Guid lessonId) =>
        db.Set<LessonLedgerEntry>().AsNoTracking()
            .Where(e => e.Kind == LedgerEntryKind.Lesson && e.LessonId == lessonId && !db.Set<LessonLedgerEntry>().Any(r => r.ReversesId == e.Id));
}

/// <summary>Packages of a student with their used lessons (the ledger's net quantity).</summary>
internal static class PackageUsages
{
    public static async Task<IReadOnlyList<PackageUsage>> OfStudentAsync(ClassHubDbContext db, Guid studentId, CancellationToken cancellationToken) =>
        await db.Set<StudentPackage>().AsNoTracking().Where(p => p.StudentId == studentId)
            .Select(p => new PackageUsage(p.Id, p.CreatedAtUtc, p.LessonCount,
                db.Set<LessonLedgerEntry>().Where(e => e.PackageId == p.Id).Sum(e => (int?)e.Quantity) ?? 0, p.ClosedAtUtc != null))
            .ToListAsync(cancellationToken);
}
