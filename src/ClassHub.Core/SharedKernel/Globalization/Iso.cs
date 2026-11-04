using System.Collections.Frozen;

namespace ClassHub.Core.SharedKernel.Globalization;

/// <summary>
/// Active ISO 4217 currency codes (circulating currencies; no funds, metals or test codes). A fixed list,
/// so validation does not depend on the ICU data of the machine.
/// </summary>
public static class Currencies
{
    private const string ActiveCodes =
        "AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND BOB BRL BSD BTN BWP BYN BZD " +
        "CAD CDF CHF CLP CNY COP CRC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP " +
        "GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW KWD " +
        "KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MYR MZN NAD NGN " +
        "NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR SDG SEK SGD SHP " +
        "SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD UYU UZS VES VND " +
        "VUV WST XAF XCD XCG XOF XPF YER ZAR ZMW ZWG";

    public static FrozenSet<string> Active { get; } =
        ActiveCodes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True for an active ISO 4217 code (case-insensitive; store it upper-case).</summary>
    public static bool IsActiveIsoCode(string? code) =>
        code is { Length: 3 } && Active.Contains(code.ToUpperInvariant());
}

/// <summary>IANA time zone ids (never Windows ids), resolvable on this machine.</summary>
public static class TimeZones
{
    public const int MaxLength = 64;

    public static bool IsIanaId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= MaxLength &&
        TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _) &&
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
