using ClassHub.Core.SharedKernel.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Api.Platform;

/// <summary>
/// Turns exceptions into RFC 9457 ProblemDetails with a stable <c>code</c> and the <c>traceId</c>.
/// Expected errors (<see cref="ClassHubException"/>) keep their safe message; anything else becomes a
/// generic 500 without exception details.
/// </summary>
internal sealed partial class ClassHubExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ClassHubExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            ClassHubException known => (known.StatusCode, known.Code, known.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, ErrorCodes.ConcurrencyConflict,
                "The record was changed by someone else. Reload it and try again."),
            BadHttpRequestException bad => (bad.StatusCode, ErrorCodes.ForStatus(bad.StatusCode),
                "The request could not be read."),
            _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
                "An unexpected error occurred."),
        };

        if (status >= 500)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            LogHandled(logger, code, status, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code },
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request rejected with {Code} ({Status}) for {Method} {Path}")]
    private static partial void LogHandled(ILogger logger, string code, int status, string method, PathString path);
}
