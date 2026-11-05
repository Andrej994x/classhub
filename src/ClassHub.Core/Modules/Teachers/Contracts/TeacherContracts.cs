namespace ClassHub.Core.Modules.Teachers.Contracts;

public sealed record TeacherProfileResponse(
    string? FirstName,
    string? LastName,
    string? Bio,
    string? TeachingMode,
    string? CountryCode,
    IReadOnlyList<Guid> SubjectIds,
    IReadOnlyList<Guid> LevelIds,
    IReadOnlyList<string> Languages,
    bool HasPhoto,
    /// <summary>Changes whenever the photo changes (cache busting for the photo URL).</summary>
    string? PhotoVersion,
    DateTime? UpdatedAtUtc,
    /// <summary>Send back with the next update; null while no profile exists.</summary>
    int? Version);

/// <summary>
/// Replaces the whole profile (omitted lists become empty). <c>TimeZone</c> and <c>Currency</c> are workspace
/// settings: when given, they are changed (and audited) in the same transaction as the profile. <c>Version</c> is
/// the version the client loaded (null for a first save); a stale version is rejected with 409.
/// </summary>
public sealed record UpdateTeacherProfileRequest(
    string? FirstName,
    string? LastName,
    string? Bio,
    string? TeachingMode,
    string? CountryCode,
    IReadOnlyList<Guid>? SubjectIds,
    IReadOnlyList<Guid>? LevelIds,
    IReadOnlyList<string>? Languages,
    string? TimeZone = null,
    string? Currency = null,
    int? Version = null);

/// <summary>An onboarding step. <c>Available</c> is false while the feature arrives in a later phase.</summary>
public sealed record OnboardingStep(string Key, bool Completed, bool Available);

public sealed record OnboardingResponse(IReadOnlyList<OnboardingStep> Steps, int Completed, int Total);

public static class OnboardingSteps
{
    public const string Profile = "profile";
    public const string Subjects = "subjects";
    public const string FirstOffer = "first-offer";
    public const string PublicPage = "public-page";
    public const string Publish = "publish";
}
