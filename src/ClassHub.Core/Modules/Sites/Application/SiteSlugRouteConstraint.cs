using System.Globalization;
using ClassHub.Core.Modules.Sites.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ClassHub.Core.Modules.Sites.Application;

/// <summary>
/// Route constraint <c>{slug:siteslug}</c>: matches only valid, non-reserved site addresses (see <see cref="SiteSlugs"/>).
/// Reserved words cover every app area, so /login, /app or /admin never reach a public page route.
/// </summary>
public sealed class SiteSlugRouteConstraint : IRouteConstraint
{
    public const string Name = "siteslug";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.TryGetValue(routeKey, out var value)
            && Convert.ToString(value, CultureInfo.InvariantCulture) is { } slug
            && SiteSlugs.Problem(slug) is null;
    }
}
