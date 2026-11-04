using ClassHub.Core.SharedKernel.Globalization;

namespace ClassHub.UnitTests.Globalization;

public sealed class IsoTests
{
    [Theory]
    [InlineData("MKD")]
    [InlineData("EUR")]
    [InlineData("usd")]
    [InlineData("CHF")]
    public void Active_currencies_are_accepted(string code) => Currencies.IsActiveIsoCode(code).ShouldBeTrue();

    [Theory]
    [InlineData("ABC")] // right shape, not a currency
    [InlineData("HRK")] // withdrawn (Croatia uses EUR since 2023)
    [InlineData("XAU")] // gold, not a currency
    [InlineData("EURO")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_inactive_currencies_are_rejected(string? code) => Currencies.IsActiveIsoCode(code).ShouldBeFalse();

    [Theory]
    [InlineData("Europe/Skopje")]
    [InlineData("UTC")]
    [InlineData("Etc/UTC")]
    [InlineData("America/Argentina/Buenos_Aires")]
    public void Iana_time_zones_are_accepted(string id) => TimeZones.IsIanaId(id).ShouldBeTrue();

    [Theory]
    [InlineData("Central European Standard Time")] // Windows id
    [InlineData("UTC+1")]
    [InlineData("Mars/Olympus")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Windows_ids_and_unknown_zones_are_rejected(string? id) => TimeZones.IsIanaId(id).ShouldBeFalse();
}
