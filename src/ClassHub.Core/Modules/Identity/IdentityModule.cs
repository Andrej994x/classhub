using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Identity.Application;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ClassHub.Core.Modules.Identity;

/// <summary>Response of <c>GET /api/auth/antiforgery</c>: the request token for the <c>X-XSRF-TOKEN</c> header.</summary>
public sealed record AntiforgeryTokenResponse(string RequestToken);

/// <summary>Identity settings (section <c>Identity</c>).</summary>
public sealed class IdentitySettings
{
    public const string SectionName = "Identity";

    /// <summary>How often a session cookie is re-checked against the user's security stamp (D-51).</summary>
    public int SecurityStampValidationIntervalSeconds { get; set; } = 60;
}

/// <summary>Accounts and authentication (<c>/api/auth</c>): ASP.NET Core Identity with cookie sessions.</summary>
public sealed class IdentityModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(IdentitySettings.SectionName).Get<IdentitySettings>() ?? new IdentitySettings();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty; // user name = email; no character restrictions
                options.Password.RequiredLength = AuthRules.PasswordMinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedEmail = false; // D-10b: confirmation gates publishing, not sign-in
                options.SignIn.RequireConfirmedAccount = false;
                options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider<AppUser>.ProviderName;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<ClassHubDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider<AppUser>>(EmailConfirmationTokenProvider<AppUser>.ProviderName)
            .AddPasswordValidator<CommonPasswordValidator>();

        // AddIdentityCore (unlike AddIdentity) does not register the security stamp validator; without it
        // the cookie's OnValidatePrincipal hook silently does nothing and revoked sessions stay valid.
        services.TryAddScoped<ISecurityStampValidator, SecurityStampValidator<AppUser>>();

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        services.Configure<SecurityStampValidatorOptions>(o =>
            o.ValidationInterval = TimeSpan.FromSeconds(Math.Max(0, settings.SecurityStampValidationIntervalSeconds)));

        services.AddScoped<AccountService>();
        services.AddScoped<IUserAccountStatus, UserAccountStatusService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth").WithTags("Auth").RequireAntiforgeryHeader();

        // Issues the antiforgery token the SPA must send (X-XSRF-TOKEN) on every unsafe request.
        auth.MapGet("/antiforgery", (HttpContext http) =>
                TypedResults.Ok(new AntiforgeryTokenResponse(http.IssueAntiforgeryTokens())))
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issue an antiforgery request token (also in the X-XSRF-TOKEN response header).");

        auth.MapPost("/register", async (RegisterRequest request, AccountService accounts, CancellationToken ct) =>
            {
                await accounts.RegisterAsync(request, ct);
                return TypedResults.Accepted((string?)null, new AcceptedResponse(AccountService.AcceptedMessage));
            })
            .WithValidation<RegisterRequest>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("Register")
            .WithSummary("Create a teacher account. Always answers 202 (no account enumeration).");

        auth.MapPost("/confirm-email", async (ConfirmEmailRequest request, AccountService accounts) =>
            {
                await accounts.ConfirmEmailAsync(request);
                return TypedResults.NoContent();
            })
            .WithValidation<ConfirmEmailRequest>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("ConfirmEmail");

        auth.MapPost("/resend-confirmation", async (EmailRequest request, AccountService accounts, CancellationToken ct) =>
            {
                await accounts.ResendConfirmationAsync(request, ct);
                return TypedResults.Accepted((string?)null, new AcceptedResponse(AccountService.AcceptedMessage));
            })
            .WithValidation<EmailRequest>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("ResendConfirmation");

        auth.MapPost("/login", async (LoginRequest request, HttpContext http, AccountService accounts) =>
                TypedResults.Ok(await accounts.LoginAsync(http, request)))
            .WithValidation<LoginRequest>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("Login");

        auth.MapPost("/logout", async (HttpContext http, AccountService accounts) =>
            {
                await accounts.LogoutAsync(http);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithName("Logout");

        auth.MapGet("/me", async (HttpContext http, AccountService accounts) =>
                TypedResults.Ok(await accounts.GetMeAsync(http.User)))
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithName("GetMe");

        auth.MapPost("/forgot-password", async (EmailRequest request, AccountService accounts, CancellationToken ct) =>
            {
                await accounts.ForgotPasswordAsync(request, ct);
                return TypedResults.Accepted((string?)null, new AcceptedResponse(AccountService.AcceptedMessage));
            })
            .WithValidation<EmailRequest>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("ForgotPassword");

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, AccountService accounts) =>
            {
                await accounts.ResetPasswordAsync(request);
                return TypedResults.NoContent();
            })
            .WithValidation<ResetPasswordRequest>()
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("ResetPassword");

        MapDevMailbox(endpoints);
    }

    /// <summary>
    /// Development/E2E only: lists messages delivered to the local dev mailbox. Never mapped in other
    /// environments (and the dev mailbox provider is never used there).
    /// </summary>
    private static void MapDevMailbox(IEndpointRouteBuilder endpoints)
    {
        var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!(environment.IsDevelopment() || environment.IsEnvironment("Testing") || environment.IsEnvironment("E2E")))
        {
            return;
        }

        endpoints.MapGet("/dev/mailbox", async Task<Ok<List<DevMailboxItem>>> (string? to, ClassHubDbContext db, CancellationToken ct) =>
            {
                var query = db.DevMailbox.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(to))
                {
                    var address = AccountService.NormalizeEmail(to);
                    query = query.Where(m => m.ToAddress == address);
                }

                var items = await query
                    .OrderByDescending(m => m.ReceivedAtUtc)
                    .Take(50)
                    .Select(m => new DevMailboxItem(m.Id, m.ToAddress, m.Subject, m.TextBody, m.HtmlBody, m.Template, m.ReceivedAtUtc))
                    .ToListAsync(ct);
                return TypedResults.Ok(items);
            })
            .AllowAnonymous()
            .WithTags("Development")
            .WithName("GetDevMailbox")
            .ExcludeFromDescription();
    }
}

public sealed record DevMailboxItem(Guid Id, string To, string Subject, string Text, string Html, string Template, DateTime ReceivedAtUtc);
