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
    DateTime? UpdatedAtUtc);

/// <summary>Replaces the whole profile (omitted lists become empty).</summary>
public sealed record UpdateTeacherProfileRequest(
    string? FirstName,
    string? LastName,
    string? Bio,
    string? TeachingMode,
    string? CountryCode,
    IReadOnlyList<Guid>? SubjectIds,
    IReadOnlyList<Guid>? LevelIds,
    IReadOnlyList<string>? Languages);

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
