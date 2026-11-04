using System.Net;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Modules.Identity.Contracts;

namespace ClassHub.Core.Modules.Identity.Application;

/// <summary>
/// Localized (mk/en) account emails. Links point to the web app and carry tokens in the URL fragment
/// (<c>#…</c>), which browsers never send to a server, so tokens do not reach request logs.
/// </summary>
internal static class AccountEmails
{
    public const string ConfirmEmailTemplate = "account.confirm-email";
    public const string ResetPasswordTemplate = "account.reset-password";
    public const string AlreadyRegisteredTemplate = "account.already-registered";

    public static EmailMessage ConfirmEmail(string to, string locale, string link) => Render(
        to, ConfirmEmailTemplate, locale, link,
        mk: ("Потврдете ја вашата е-пошта за ClassHub",
            "Добредојдовте во ClassHub! Потврдете ја вашата е-пошта со следниов линк (важи 24 часа):",
            "Потврди е-пошта",
            "Ако не сте се регистрирале, игнорирајте ја оваа порака."),
        en: ("Confirm your email for ClassHub",
            "Welcome to ClassHub! Confirm your email address with this link (valid for 24 hours):",
            "Confirm email",
            "If you did not sign up, you can ignore this message."));

    public static EmailMessage ResetPassword(string to, string locale, string link) => Render(
        to, ResetPasswordTemplate, locale, link,
        mk: ("Ресетирање на лозинката за ClassHub",
            "Побаравте нова лозинка. Поставете ја со следниов линк (важи 2 часа):",
            "Постави нова лозинка",
            "Ако не сте го побарале ова, игнорирајте ја пораката. Лозинката останува иста."),
        en: ("Reset your ClassHub password",
            "You asked for a new password. Set it with this link (valid for 2 hours):",
            "Set a new password",
            "If you did not ask for this, ignore this message. Your password stays the same."));

    public static EmailMessage AlreadyRegistered(string to, string locale, string loginLink) => Render(
        to, AlreadyRegisteredTemplate, locale, loginLink,
        mk: ("Веќе имате ClassHub сметка",
            "Некој (најверојатно вие) се обиде да се регистрира со оваа е-пошта, но сметката веќе постои. Најавете се тука:",
            "Најави се",
            "Ако ја заборавивте лозинката, користете „Заборавена лозинка“ на страницата за најава."),
        en: ("You already have a ClassHub account",
            "Someone (probably you) tried to sign up with this email, but the account already exists. Sign in here:",
            "Sign in",
            "If you forgot your password, use \"Forgot password\" on the sign-in page."));

    private static EmailMessage Render(
        string to, string template, string locale, string link,
        (string Subject, string Intro, string Action, string Footer) mk,
        (string Subject, string Intro, string Action, string Footer) en)
    {
        var t = SupportedLocales.OrDefault(locale) == SupportedLocales.English ? en : mk;
        var safeLink = WebUtility.HtmlEncode(link);
        var html =
            $"<p>{WebUtility.HtmlEncode(t.Intro)}</p>" +
            $"<p><a href=\"{safeLink}\">{WebUtility.HtmlEncode(t.Action)}</a></p>" +
            $"<p style=\"color:#666\">{WebUtility.HtmlEncode(t.Footer)}</p>";
        var text = $"{t.Intro}\n\n{link}\n\n{t.Footer}\n";
        return new EmailMessage(to, t.Subject, html, text, template);
    }
}
