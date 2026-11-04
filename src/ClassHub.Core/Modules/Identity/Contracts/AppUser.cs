using Microsoft.AspNetCore.Identity;

namespace ClassHub.Core.Modules.Identity.Contracts;

/// <summary>
/// A ClassHub account (teacher, student, platform staff). What a user may do comes from relationships
/// (workspace memberships, student links, platform roles), not from this type. It lives in
/// <c>Contracts</c> because other modules reference users (foreign keys, read-only queries).
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int LocaleMaxLength = 10;
    public const int TimeZoneMaxLength = 64;
    public const string DefaultLocale = "mk";
    public const string DefaultTimeZone = "Europe/Skopje";

    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>UI language: <c>mk</c> or <c>en</c>.</summary>
    public string Locale { get; set; } = DefaultLocale;

    /// <summary>IANA time zone id.</summary>
    public string TimeZone { get; set; } = DefaultTimeZone;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }
}

public sealed class AppRole : IdentityRole<Guid>
{
}

/// <summary>Account status. Suspended accounts cannot sign in and lose their sessions.</summary>
public enum UserStatus
{
    Active = 0,
    Suspended = 1,
}

/// <summary>Supported UI languages.</summary>
public static class SupportedLocales
{
    public const string Macedonian = "mk";
    public const string English = "en";

    public static IReadOnlyList<string> All { get; } = [Macedonian, English];

    public static bool IsSupported(string? locale) => locale is Macedonian or English;

    public static string OrDefault(string? locale) => IsSupported(locale) ? locale! : Macedonian;
}
