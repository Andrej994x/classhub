using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Infrastructure.Persistence;

public static class SqlServerErrors
{
    /// <summary>SQL Server unique index (2601) or unique constraint (2627) violation.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex?.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}
