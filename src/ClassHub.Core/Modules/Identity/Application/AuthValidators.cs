using ClassHub.Core.Modules.Identity.Contracts;
using FluentValidation;

namespace ClassHub.Core.Modules.Identity.Application;

internal static class AuthRules
{
    public const int EmailMaxLength = 256;
    public const int PasswordMinLength = 10;
    public const int PasswordMaxLength = 128;
    public const int TokenMaxLength = 2048;

    public static IRuleBuilderOptions<T, string?> ValidEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().MaximumLength(EmailMaxLength).EmailAddress();

    public static IRuleBuilderOptions<T, string?> ValidNewPassword<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().MinimumLength(PasswordMinLength).MaximumLength(PasswordMaxLength);
}

internal sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).ValidNewPassword();
        RuleFor(x => x.Locale)
            .Must(l => l is null || SupportedLocales.IsSupported(l))
            .WithMessage("Locale must be 'mk' or 'en'.");
    }
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(AuthRules.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(AuthRules.PasswordMaxLength);
    }
}

internal sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(AuthRules.TokenMaxLength);
    }
}

internal sealed class EmailRequestValidator : AbstractValidator<EmailRequest>
{
    public EmailRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
    }
}

internal sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(AuthRules.TokenMaxLength);
        RuleFor(x => x.NewPassword).ValidNewPassword();
    }
}
