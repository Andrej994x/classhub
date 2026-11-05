using ClassHub.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Moderation.Persistence;

/// <summary>Locks for report intake (allow-listed raw SQL: <c>sp_getapplock</c> only, no tenant data).</summary>
internal sealed class ReportQueries(ClassHubDbContext db)
{
    /// <summary>
    /// Serializes report intake per reported workspace until the caller's transaction ends, so the hourly cap is a real
    /// limit even when many visitors report at the same moment.
    /// </summary>
    public Task LockIntakeAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:report-intake:{workspaceId:N}";
        return db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50007, 'Could not acquire the report intake lock.', 1;",
            cancellationToken);
    }

    /// <summary>
    /// Serializes every staff decision (hide, dismiss) about one piece of reported content until the caller's transaction
    /// ends: the decision reads the report and the other open reports about the content only after taking it.
    /// </summary>
    public Task LockTargetAsync(Guid workspaceId, int target, string? sectionType, Guid? testId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:moderation-target:{workspaceId:N}:{target}:{sectionType ?? testId?.ToString("N") ?? "site"}";
        return db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50008, 'Could not acquire the moderation lock.', 1;",
            cancellationToken);
    }
}
