using ClassHub.Core.Infrastructure.Persistence;

namespace ClassHub.UnitTests.Persistence;

public sealed class UtcDateTimeConverterTests
{
    [Fact]
    public void Utc_values_are_stored_unchanged()
    {
        var value = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        UtcDateTimeConverter.ToUtc(value).ShouldBe(value);
    }

    [Fact]
    public void Local_values_are_converted_to_utc()
    {
        var local = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Local);

        var stored = UtcDateTimeConverter.ToUtc(local);

        stored.Kind.ShouldBe(DateTimeKind.Utc);
        stored.ShouldBe(local.ToUniversalTime());
    }

    [Fact]
    public void Unspecified_values_are_treated_as_utc()
    {
        var unspecified = new DateTime(2026, 1, 1, 8, 30, 0, DateTimeKind.Unspecified);

        var stored = UtcDateTimeConverter.ToUtc(unspecified);

        stored.Kind.ShouldBe(DateTimeKind.Utc);
        stored.Ticks.ShouldBe(unspecified.Ticks);
    }

    [Fact]
    public void Values_read_from_the_database_are_marked_utc()
    {
        var converter = new UtcDateTimeConverter();
        var fromDb = new DateTime(2026, 1, 1, 8, 30, 0, DateTimeKind.Unspecified);

        var model = (DateTime)converter.ConvertFromProvider(fromDb)!;

        model.Kind.ShouldBe(DateTimeKind.Utc);
        model.Ticks.ShouldBe(fromDb.Ticks);
    }
}
