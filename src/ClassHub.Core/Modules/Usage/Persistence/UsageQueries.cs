using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Usage.Contracts;
using ClassHub.Core.Modules.Usage.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Usage.Persistence;

/// <summary>
/// The usage counters' writes and the platform-wide reads (allow-listed tenant bypass: totals across workspaces for staff,
/// never a workspace's own rows).
/// </summary>
internal sealed class UsageQueries(ClassHubDbContext db)
{
    private const int DeadlockVictim = 1205;
    private const int MaxAttempts = 3;

    /// <summary>
    /// One more use on the day, atomically: parallel uses never lose a count. An UPDATE increments an existing row under
    /// its row lock; only the first use of a day inserts. When two first uses race, the loser's INSERT hits the primary
    /// key and it increments the winner's row instead. Only row locks are taken: a serializable MERGE (HOLDLOCK) locked
    /// key ranges shared with other workspaces and lost counts to deadlocks under load. A deadlock is still retried.
    /// Never called inside a transaction (the callers commit first), so a retry repeats only this statement.
    /// </summary>
    public async Task IncrementAsync(Guid workspaceId, DateOnly day, UsageEvent usage, string subject, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.ExecuteSqlAsync(
                    $"""
                    UPDATE [UsageCounters] SET [Count] = [Count] + 1
                    WHERE [WorkspaceId] = {workspaceId} AND [Day] = {day} AND [Event] = {(byte)usage} AND [Subject] = {subject};
                    IF @@ROWCOUNT = 0
                    BEGIN
                        BEGIN TRY
                            INSERT INTO [UsageCounters] ([WorkspaceId], [Day], [Event], [Subject], [Count])
                            VALUES ({workspaceId}, {day}, {(byte)usage}, {subject}, 1);
                        END TRY
                        BEGIN CATCH
                            IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
                            UPDATE [UsageCounters] SET [Count] = [Count] + 1
                            WHERE [WorkspaceId] = {workspaceId} AND [Day] = {day} AND [Event] = {(byte)usage} AND [Subject] = {subject};
                        END CATCH
                    END
                    """,
                    cancellationToken);
                return;
            }
            catch (SqlException ex) when (ex.Number == DeadlockVictim && attempt < MaxAttempts && db.Database.CurrentTransaction is null)
            {
                // Chosen as a deadlock victim: nothing was written; count again.
            }
        }
    }

    private IQueryable<UsageCounter> AllWorkspaces(DateOnly from, DateOnly to) =>
        db.Set<UsageCounter>().IgnoreQueryFilters().AsNoTracking().Where(c => c.Day >= from && c.Day <= to);

    public async Task<List<UsageTotal>> PlatformTotalsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await AllWorkspaces(from, to).GroupBy(c => new { c.Event, c.Subject })
            .Select(g => new UsageTotal(g.Key.Event, g.Key.Subject, g.Sum(c => c.Count))).ToListAsync(cancellationToken);

    public async Task<List<UsageDailyTotal>> PlatformDailyAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await AllWorkspaces(from, to).GroupBy(c => new { c.Day, c.Event })
            .Select(g => new UsageDailyTotal(g.Key.Day, g.Key.Event, g.Sum(c => c.Count))).ToListAsync(cancellationToken);

    public Task<int> WorkspacesUsingAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        AllWorkspaces(from, to).Select(c => c.WorkspaceId).Distinct().CountAsync(cancellationToken);
}

internal sealed class UsageCounterConfiguration : IEntityTypeConfiguration<UsageCounter>
{
    public void Configure(EntityTypeBuilder<UsageCounter> builder)
    {
        builder.ToTable("UsageCounters", t =>
        {
            t.HasCheckConstraint("CK_UsageCounters_Event", "[Event] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_UsageCounters_Count", "[Count] >= 0");
        });
        builder.HasKey(x => new { x.WorkspaceId, x.Day, x.Event, x.Subject });
        builder.Property(x => x.Event).HasConversion<byte>();
        builder.Property(x => x.Subject).HasMaxLength(UsageCounter.SubjectMaxLength).IsUnicode(false);
        builder.HasIndex(x => new { x.Day, x.Event });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(UsageCounter.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
    }
}
