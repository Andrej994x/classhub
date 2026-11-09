using ClassHub.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Finance.Persistence;

/// <summary>The finance lock (raw SQL, allow-listed).</summary>
internal sealed class FinanceQueries(ClassHubDbContext db)
{
    /// <summary>
    /// Serializes changes to one student's lessons (consumption, corrections, reversals) until the caller's transaction
    /// ends: the package chosen for a lesson always still has the lesson, and parallel workers never consume twice.
    /// </summary>
    public Task LockStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:finance:{studentId:N}";
        return db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50010, 'Could not acquire the finance lock.', 1;",
            cancellationToken);
    }
}
