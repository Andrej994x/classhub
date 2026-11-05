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

/// <summary>ISO 3166-1 alpha-2 country codes (officially assigned). Fixed list, independent of ICU data.</summary>
public static class Countries
{
    private const string Codes =
        "AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ " +
        "CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR " +
        "GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO " +
        "JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR " +
        "MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO " +
        "RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV " +
        "TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW";

    public static FrozenSet<string> All { get; } = Codes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True for an assigned ISO 3166-1 alpha-2 code (upper case).</summary>
    public static bool IsIsoCode(string? code) => code is { Length: 2 } && All.Contains(code);
}
