using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Globalization;
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

internal sealed class UpdateMeRequestValidator : AbstractValidator<UpdateMeRequest>
{
    public UpdateMeRequestValidator()
    {
        RuleFor(x => x.Locale)
            .Must(SupportedLocales.IsSupported).WithMessage("Locale must be 'mk' or 'en'.")
            .When(x => x.Locale is not null);
        RuleFor(x => x.TimeZone)
            .Must(id => id!.Length <= AppUser.TimeZoneMaxLength && TimeZones.IsIanaId(id)).WithMessage("Unknown time zone.")
            .When(x => x.TimeZone is not null);
    }
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

internal sealed class TwoFactorLoginRequestValidator : AbstractValidator<TwoFactorLoginRequest>
{
    public TwoFactorLoginRequestValidator()
    {
        RuleFor(x => x).Must(x => (x.Code is null) != (x.RecoveryCode is null))
            .OverridePropertyName("code").WithMessage("Send either an authenticator code or a recovery code.");
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).When(x => x.Code is not null);
        RuleFor(x => x.RecoveryCode).NotEmpty().MaximumLength(40).When(x => x.RecoveryCode is not null);
    }
}

internal sealed class MfaCodeRequestValidator : AbstractValidator<MfaCodeRequest>
{
    public MfaCodeRequestValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
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
