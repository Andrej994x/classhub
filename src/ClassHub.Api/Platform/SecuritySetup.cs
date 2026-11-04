using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using Microsoft.AspNetCore.DataProtection;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

namespace ClassHub.Api.Platform;

/// <summary>Forwarded-headers settings (section <c>ForwardedHeaders</c>); off unless behind a known proxy.</summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }

    /// <summary>IP addresses of trusted reverse proxies.</summary>
    public IList<string> KnownProxies { get; } = [];
}

internal static class SecuritySetup
{
    /// <summary>Name of the authentication cookie.</summary>
    public const string AuthCookieName = ".ClassHub.Auth";

    /// <summary>
    /// Cookie authentication (the ASP.NET Core Identity application scheme, used by R0-P4), deny-by-default
    /// authorization, antiforgery for JSON requests, and forwarded headers.
    /// </summary>
    public static IServiceCollection AddClassHubSecurity(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var securePolicy = environment.IsDevelopment() || environment.IsEnvironment("Testing") || environment.IsEnvironment("E2E")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        void ConfigureCompanionCookie(CookieAuthenticationOptions options, string name, TimeSpan lifetime)
        {
            options.Cookie.Name = name;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securePolicy;
            options.ExpireTimeSpan = lifetime;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; },
                OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; },
            };
        }

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            // Identity's companion schemes: SignInManager and the security stamp validator sign out of
            // them, and two-factor sign-in (R1-P9) uses them. They never authenticate API requests.
            .AddCookie(IdentityConstants.ExternalScheme, o => ConfigureCompanionCookie(o, ".ClassHub.External", TimeSpan.FromMinutes(5)))
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme, o => ConfigureCompanionCookie(o, ".ClassHub.TwoFactorUserId", TimeSpan.FromMinutes(5)))
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, o => ConfigureCompanionCookie(o, ".ClassHub.TwoFactorRememberMe", TimeSpan.FromDays(14)))
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = AuthCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = securePolicy;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                // An API never redirects to a login page: 401/403 with ProblemDetails (status code pages).
                options.Events = new CookieAuthenticationEvents
                {
                    // Re-checks the cookie against the user's security stamp (password reset, suspension,
                    // sign-out everywhere) every SecurityStampValidatorOptions.ValidationInterval.
                    OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync,
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(ClassHubPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());

        // Keys protect auth cookies, antiforgery and Identity tokens; stored in the database so they
        // survive restarts (D-52). On Windows (development) they are additionally encrypted with DPAPI.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("ClassHub")
            .PersistKeysToDbContext<ClassHubDbContext>();
        if (OperatingSystem.IsWindows() && environment.IsDevelopment())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderFilter.HeaderName;
            options.Cookie.Name = AntiforgeryHeaderFilter.CookieTokenCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        var forwarded = configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>()
            ?? new ForwardedHeadersSettings();
        var trustedProxies = ValidateForwardedHeaders(forwarded);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in trustedProxies)
            {
                options.KnownProxies.Add(proxy);
            }
        });
        services.AddSingleton(forwarded);

        return services;
    }

    /// <summary>
    /// Fails closed: forwarded headers may only be enabled together with at least one valid trusted
    /// proxy address. With empty trust lists ASP.NET Core would accept X-Forwarded-For/Proto from any
    /// peer, letting clients spoof their IP (rate limits) and scheme.
    /// </summary>
    internal static IReadOnlyList<System.Net.IPAddress> ValidateForwardedHeaders(ForwardedHeadersSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var proxies = new List<System.Net.IPAddress>();
        foreach (var value in settings.KnownProxies)
        {
            if (!System.Net.IPAddress.TryParse(value?.Trim(), out var address))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies contains an invalid IP address: '{value}'.");
            }

            proxies.Add(address);
        }

        if (settings.Enabled && proxies.Count == 0)
        {
            throw new InvalidOperationException(
                "ForwardedHeaders:Enabled is true but ForwardedHeaders:KnownProxies is empty. " +
                "Configure the IP address of every trusted reverse proxy, or disable forwarded headers.");
        }

        return proxies;
    }
}
