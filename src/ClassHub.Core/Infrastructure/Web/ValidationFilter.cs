using System.Text;
using System.Text.Json;
using ClassHub.Core.SharedKernel.Errors;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Infrastructure.Web;

/// <summary>
/// Validates the endpoint argument of type <typeparamref name="T"/> with its FluentValidation validator
/// and returns a 400 ValidationProblem (code <see cref="ErrorCodes.ValidationFailed"/>, camelCase field
/// names) when it is invalid.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter
    where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The request body is missing or invalid.",
                extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.BadRequest });
        }

        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>()
            ?? throw new InvalidOperationException($"No validator is registered for {typeof(T).Name}.");

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(e => ToCamelCasePath(e.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        return TypedResults.ValidationProblem(
            errors,
            title: "One or more validation errors occurred.",
            extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationFailed });
    }

    /// <summary>"Items[0].DisplayName" → "items[0].displayName".</summary>
    internal static string ToCamelCasePath(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(propertyPath.Length);
        foreach (var segment in propertyPath.Split('.'))
        {
            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(JsonNamingPolicy.CamelCase.ConvertName(segment));
        }

        return builder.ToString();
    }
}

public static class ValidationEndpointExtensions
{
    /// <summary>Validates the <typeparamref name="T"/> request body before the handler runs.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddEndpointFilter<ValidationFilter<T>>()
            .ProducesValidationProblem();
    }
}
