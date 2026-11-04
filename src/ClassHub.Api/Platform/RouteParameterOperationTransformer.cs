using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ClassHub.Api.Platform;

/// <summary>
/// Documents every route parameter of an endpoint, also when the handler does not bind it. Workspace APIs
/// (<c>/api/w/{workspaceId:guid}</c>) read the workspace from the route in the authorization handler, not in
/// the handler signature, so without this the contract (and the generated TypeScript types) would say the
/// path takes no parameters.
/// </summary>
internal sealed partial class RouteParameterOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var template = context.Description.ActionDescriptor.AttributeRouteInfo?.Template ?? context.Description.RelativePath ?? string.Empty;
        foreach (Match match in RouteParameter().Matches(template))
        {
            var name = match.Groups["name"].Value;
            operation.Parameters ??= [];
            if (operation.Parameters.Any(p => p.In == ParameterLocation.Path && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var isGuid = match.Groups["constraints"].Value.Split(':', StringSplitOptions.RemoveEmptyEntries).Contains("guid", StringComparer.OrdinalIgnoreCase);
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Path,
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = isGuid ? "uuid" : null },
            });
        }

        return Task.CompletedTask;
    }

    /// <summary><c>{name}</c> or <c>{name:constraint:...}</c>; catch-all (<c>**</c>) and optional markers are ignored.</summary>
    [GeneratedRegex(@"\{\**(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<constraints>(:[^}=?]+)*)[^}]*\}")]
    private static partial Regex RouteParameter();
}
