using ClassHub.Core.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;

namespace ClassHub.Core.Modules.Identity.Application;

/// <summary>
/// Rejects passwords that are on a list of very common passwords, consist of a single repeated
/// character, or contain the email's local part. Length (≥ 10) is enforced by Identity options; there
/// are no composition rules (NIST SP 800-63B).
/// </summary>
internal sealed class CommonPasswordValidator : IPasswordValidator<AppUser>
{
    public const string ErrorCode = "PasswordTooCommon";

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890", "12345678910", "123456789012", "0123456789", "0987654321", "1111111111", "1234512345",
        "1q2w3e4r5t", "1qaz2wsx3edc", "qwertyuiop", "qwerty1234", "qwerty12345", "qwertyuiop1", "asdfghjkl1",
        "asdfghjkl;", "zxcvbnm123", "password12", "password123", "password1234", "passw0rd12", "p@ssw0rd123",
        "iloveyou12", "letmein123", "welcome123", "welcome1234", "football12", "baseball12", "superman12",
        "trustno1234", "abc1234567", "abcdefghij", "abcd123456", "aa12345678", "a123456789", "q1w2e3r4t5",
        "administrator", "admin12345", "changeme123", "computer12", "internet12", "sunshine12", "princess12",
        "lozinka123", "lozinka1234", "makedonija", "makedonija1", "skopje1234", "classhub123", "classhub1234",
        "nastavnik1", "nastavnik123", "uchenik123", "teacher123", "student123", "school1234",
    };

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var tooCommon = CommonPasswords.Contains(password) || password.Distinct().Count() == 1;

        var localPart = user.Email?.Split('@')[0];
        var containsEmail = localPart is { Length: >= 4 } && password.Contains(localPart, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(tooCommon || containsEmail
            ? IdentityResult.Failed(new IdentityError
            {
                Code = ErrorCode,
                Description = "Choose a less common password that does not contain your email address.",
            })
            : IdentityResult.Success);
    }
}
