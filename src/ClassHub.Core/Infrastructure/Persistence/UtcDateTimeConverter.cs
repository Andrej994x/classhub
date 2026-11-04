using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClassHub.Core.Infrastructure.Persistence;

/// <summary>
/// Stores every <see cref="DateTime"/> as UTC and marks values read from the database as UTC.
/// Local times are converted to UTC; unspecified kinds are treated as UTC (application code always
/// works with UTC, see <see cref="TimeProvider"/>).
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => ToUtc(value),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
{
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
