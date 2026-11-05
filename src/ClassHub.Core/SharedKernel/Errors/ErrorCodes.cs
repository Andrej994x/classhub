namespace ClassHub.Core.SharedKernel.Errors;

/// <summary>
/// Stable, machine-readable error codes returned in every ProblemDetails response as <c>code</c>.
/// The web app maps them to translated messages. Never change an existing value.
/// </summary>
public static class ErrorCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Conflict = "CONFLICT";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string BusinessRuleViolation = "BUSINESS_RULE_VIOLATION";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string RateLimited = "RATE_LIMITED";
    public const string AntiforgeryInvalid = "ANTIFORGERY_INVALID";
    public const string InternalError = "INTERNAL_ERROR";

    // Accounts (R0-P4)
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";
    public const string InvalidToken = "INVALID_TOKEN";
    public const string EmailAlreadyConfirmed = "EMAIL_ALREADY_CONFIRMED";

    // Files
    public const string ImageInvalid = "IMAGE_INVALID";

    // Public site (R1-P3)
    public const string SlugTaken = "SLUG_TAKEN";
    public const string SiteNotCreated = "SITE_NOT_CREATED";
    public const string EmailNotConfirmed = "EMAIL_NOT_CONFIRMED";

    // Leads (R1-P5)
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string FormExpired = "FORM_EXPIRED";
    public const string LeadsUnavailable = "LEADS_UNAVAILABLE";

    // Diagnostic tests (R1-P6)
    public const string TestSlugTaken = "TEST_SLUG_TAKEN";
    public const string TestHasAttempts = "TEST_HAS_ATTEMPTS";
    public const string TestChanged = "TEST_CHANGED";

    // Platform administration (R1-P9)
    public const string MfaRequired = "MFA_REQUIRED";
    public const string MfaCodeInvalid = "MFA_CODE_INVALID";
    public const string SignInExpired = "SIGN_IN_EXPIRED";
    public const string AdminTargetForbidden = "ADMIN_TARGET_FORBIDDEN";

    /// <summary>Default code for an HTTP status when no specific code was set.</summary>
    public static string ForStatus(int statusCode) => statusCode switch
    {
        400 => BadRequest,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        405 => MethodNotAllowed,
        409 => Conflict,
        413 => PayloadTooLarge,
        415 => UnsupportedMediaType,
        422 => BusinessRuleViolation,
        429 => RateLimited,
        _ when statusCode >= 500 => InternalError,
        _ => BadRequest,
    };
}
