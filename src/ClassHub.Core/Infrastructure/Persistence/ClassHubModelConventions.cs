using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Infrastructure.Persistence;

/// <summary>
/// SQL Server conventions shared by every ClassHub model (docs/ROADMAP.md B.3 and docs/DATABASE.md).
/// </summary>
public static class ClassHubModelConventions
{
    /// <summary>
    /// Database collation: Macedonian rules, case-insensitive, accent-sensitive, supplementary characters (D-35).
    /// </summary>
    public const string DatabaseCollation = "Macedonian_FYROM_100_CI_AS_SC";

    /// <summary>Precision for <c>datetime2</c> columns (milliseconds).</summary>
    public const int DateTimePrecision = 3;

    public static void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Every DateTime is stored as UTC in datetime2(3) and read back with Kind = Utc.
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>()
            .HavePrecision(DateTimePrecision);
        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<UtcDateTimeConverter>()
            .HavePrecision(DateTimePrecision);
    }

    public static void ApplyModelConventions(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.UseCollation(DatabaseCollation);

        // No cascading deletes for ClassHub entities: SQL Server rejects multiple cascade paths, and
        // ClassHub archives records instead of deleting history. ASP.NET Core Identity's own tables
        // keep their framework-defined behavior.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            var ns = foreignKey.DeclaringEntityType.ClrType.Namespace ?? string.Empty;
            if (ns.StartsWith("ClassHub.", StringComparison.Ordinal) && !foreignKey.IsOwnership)
            {
                foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
            }
        }
    }
}
