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
