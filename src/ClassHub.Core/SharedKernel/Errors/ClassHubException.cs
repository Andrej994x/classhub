namespace ClassHub.Core.SharedKernel.Errors;

/// <summary>
/// Base type for expected, client-facing errors. The message must be safe to show to the caller
/// (it becomes the ProblemDetails title); never put secrets or other users' data in it.
/// </summary>
public abstract class ClassHubException : Exception
{
    protected ClassHubException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Stable error code (see <see cref="ErrorCodes"/>).</summary>
    public string Code { get; }

    /// <summary>HTTP status code to return.</summary>
    public int StatusCode { get; }
}

/// <summary>The resource does not exist or belongs to another tenant (never reveal which).</summary>
public sealed class NotFoundException(string message = "The resource was not found.", string code = ErrorCodes.NotFound)
    : ClassHubException(code, message, 404);

/// <summary>The request conflicts with the current state (duplicate, already done, overlap).</summary>
public sealed class ConflictException(string message, string code = ErrorCodes.Conflict)
    : ClassHubException(code, message, 409);

/// <summary>A business rule rejects the request (for example a plan limit or an invalid status change).</summary>
public sealed class BusinessRuleException(string message, string code = ErrorCodes.BusinessRuleViolation)
    : ClassHubException(code, message, 422);

/// <summary>Authentication failed (wrong credentials). The message never says which part was wrong.</summary>
public sealed class AuthenticationFailedException(string message = "The email or password is incorrect.", string code = ErrorCodes.InvalidCredentials)
    : ClassHubException(code, message, 401);

/// <summary>
/// Field-level validation errors found after request validation (for example password rules from
/// ASP.NET Core Identity). Returned as a 400 ValidationProblem with <see cref="ErrorCodes.ValidationFailed"/>.
/// </summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : ClassHubException(ErrorCodes.ValidationFailed, "One or more validation errors occurred.", 400)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The request is invalid in a way the client must fix (for example an expired token).</summary>
public sealed class BadRequestException(string message, string code = ErrorCodes.BadRequest)
    : ClassHubException(code, message, 400);

/// <summary>The caller is authenticated but not allowed to perform this operation.</summary>
public sealed class ForbiddenOperationException(string message = "You are not allowed to perform this operation.", string code = ErrorCodes.Forbidden)
    : ClassHubException(code, message, 403);
