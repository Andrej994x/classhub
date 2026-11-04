using System.Security.Claims;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Identity.Application;

/// <summary>
/// Account use cases on top of ASP.NET Core Identity. Responses never reveal whether an email is
/// registered (register, resend confirmation and forgot password always answer the same way).
/// </summary>
internal sealed partial class AccountService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IPasswordHasher<AppUser> passwordHasher,
    IEmailOutbox outbox,
    ClassHubDbContext db,
    IOptions<AppLinkOptions> links,
    TimeProvider clock,
    ILogger<AccountService> logger)
{
    public const string AcceptedMessage = "If the address can receive email, a message is on its way.";

    /// <summary>A hash of a random value, verified when the email is unknown to keep timing similar.</summary>
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString("N")));

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email!);
        var locale = SupportedLocales.OrDefault(request.Locale);

        // Validate the password before looking the email up, so the answer is the same for known and
        // unknown addresses.
        var candidate = new AppUser { Email = email, UserName = email };
        await ThrowIfPasswordInvalidAsync(candidate, request.Password!, "password");

        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            outbox.Enqueue(AccountEmails.AlreadyRegistered(email, existing.Locale, Link("/login")));
            await db.SaveChangesAsync(cancellationToken);
            LogRegistrationForExistingEmail(logger);
            return;
        }

        var user = new AppUser
        {
            Email = email,
            UserName = email,
            Locale = locale,
            TimeZone = AppUser.DefaultTimeZone,
            Status = UserStatus.Active,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };

        // The account and its confirmation email commit together or not at all (UserManager and the
        // outbox share this scoped DbContext, so both run inside the transaction).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, request.Password!);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The unique email index rejected a parallel registration; answer like a known address.
            LogRegistrationForExistingEmail(logger);
            return;
        }

        if (!created.Succeeded)
        {
            if (created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                // A parallel registration won the race; answer exactly like a known address.
                return;
            }

            throw ToValidationException(created, "password");
        }

        await QueueConfirmationEmailAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogRegistered(logger, user.Id);
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            throw InvalidToken();
        }

        if (user.EmailConfirmed)
        {
            // Never accept a token without validating it. A valid but reused link gets a clear answer;
            // anything else is an invalid token.
            var stillValid = await users.VerifyUserTokenAsync(
                user, users.Options.Tokens.EmailConfirmationTokenProvider, UserManager<AppUser>.ConfirmEmailTokenPurpose, request.Token!);
            throw stillValid
                ? new ConflictException("This email address is already confirmed.", ErrorCodes.EmailAlreadyConfirmed)
                : InvalidToken();
        }

        var result = await users.ConfirmEmailAsync(user, request.Token!);
        if (!result.Succeeded)
        {
            throw InvalidToken();
        }
    }

    public async Task ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(NormalizeEmail(request.Email!));
        if (user is { EmailConfirmed: false, Status: UserStatus.Active })
        {
            await QueueConfirmationEmailAsync(user, cancellationToken);
        }
    }

    public async Task<MeResponse> LoginAsync(HttpContext http, LoginRequest request)
    {
        var user = await users.FindByEmailAsync(NormalizeEmail(request.Email!));
        if (user is null)
        {
            passwordHasher.VerifyHashedPassword(new AppUser(), DummyHash.Value, request.Password!);
            throw new AuthenticationFailedException();
        }

        // Counts failures and locks the account (Identity lockout); a locked account answers like a wrong password.
        var check = await signIn.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            LogLoginFailed(logger, user.Id, check.IsLockedOut);
            throw new AuthenticationFailedException();
        }

        if (user.Status != UserStatus.Active)
        {
            // Only reachable with the correct password, so it reveals nothing to a guesser.
            throw new ForbiddenOperationException("This account is suspended.", ErrorCodes.AccountSuspended);
        }

        await signIn.SignInAsync(user, new AuthenticationProperties { IsPersistent = request.RememberMe }, "pwd");

        user.LastLoginAtUtc = clock.GetUtcNow().UtcDateTime;
        await users.UpdateAsync(user);

        // Antiforgery tokens are bound to the user: issue tokens for the new identity.
        http.User = await signIn.CreateUserPrincipalAsync(user);
        http.IssueAntiforgeryTokens();

        LogLoggedIn(logger, user.Id);
        return await ToMeAsync(user);
    }

    public async Task LogoutAsync(HttpContext http)
    {
        await signIn.SignOutAsync();
        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        http.IssueAntiforgeryTokens();
    }

    public async Task<MeResponse> GetMeAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal) ?? throw new AuthenticationFailedException("You are not signed in.", ErrorCodes.Unauthorized);
        return await ToMeAsync(user);
    }

    public async Task ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(NormalizeEmail(request.Email!));
        if (user is null)
        {
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = Link($"/reset-password#email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}");
        outbox.Enqueue(AccountEmails.ResetPassword(user.Email!, user.Locale, link));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(NormalizeEmail(request.Email!));
        if (user is null)
        {
            throw InvalidToken();
        }

        await ThrowIfPasswordInvalidAsync(user, request.NewPassword!, "newPassword");

        // Identity rotates the security stamp: every existing session ends at the next validation.
        var result = await users.ResetPasswordAsync(user, request.Token!, request.NewPassword!);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                throw InvalidToken();
            }

            throw ToValidationException(result, "newPassword");
        }

        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        LogPasswordReset(logger, user.Id);
    }

    private async Task QueueConfirmationEmailAsync(AppUser user, CancellationToken cancellationToken)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = Link($"/verify-email#userId={user.Id:D}&token={Uri.EscapeDataString(token)}");
        outbox.Enqueue(AccountEmails.ConfirmEmail(user.Email!, user.Locale, link));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ThrowIfPasswordInvalidAsync(AppUser user, string password, string field)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            errors.AddRange(result.Errors);
        }

        if (errors.Count > 0)
        {
            throw ToValidationException(IdentityResult.Failed([.. errors]), field);
        }
    }

    private async Task<MeResponse> ToMeAsync(AppUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return new MeResponse(user.Id, user.Email!, user.EmailConfirmed, user.Locale, user.TimeZone, [.. roles.Order(StringComparer.Ordinal)]);
    }

    private string Link(string pathAndFragment) => links.Value.PublicBaseUrl.TrimEnd('/') + pathAndFragment;

    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>SQL Server unique index (2601) or unique constraint (2627) violation.</summary>
    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    private static BadRequestException InvalidToken() =>
        new("The link is invalid or has expired. Request a new one.", ErrorCodes.InvalidToken);

    private static RequestValidationException ToValidationException(IdentityResult result, string field) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [.. result.Errors.Select(e => e.Description).Distinct(StringComparer.Ordinal)],
        });

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered")]
    private static partial void LogRegistered(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registration attempted for an existing account")]
    private static partial void LogRegistrationForExistingEmail(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in")]
    private static partial void LogLoggedIn(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in failed for user {UserId} (locked out: {LockedOut})")]
    private static partial void LogLoginFailed(ILogger logger, Guid userId, bool lockedOut);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} reset the password")]
    private static partial void LogPasswordReset(ILogger logger, Guid userId);
}
