using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Modules.Identity.Application;
using ClassHub.Core.Modules.Identity.Contracts;

namespace ClassHub.UnitTests.Identity;

public sealed class IdentityUnitTests
{
    [Theory]
    [InlineData("1234567890", false)]
    [InlineData("Password123", false)]
    [InlineData("qwertyuiop", false)]
    [InlineData("zzzzzzzzzzzz", false)]
    [InlineData("ana.petrovska2026", false)] // contains the email's local part
    [InlineData("Correct-Horse-Battery-7", true)]
    public async Task Common_password_validator(string password, bool valid)
    {
        var validator = new CommonPasswordValidator();
        var user = new AppUser { Email = "ana.petrovska@example.mk" };

        var result = await validator.ValidateAsync(null!, user, password);

        result.Succeeded.ShouldBe(valid);
    }

    [Fact]
    public void Account_emails_are_localized_and_html_encoded()
    {
        var link = "http://localhost:5173/verify-email#userId=1&token=a<b>\"c";

        var mk = AccountEmails.ConfirmEmail("a@example.test", "mk", link);
        var en = AccountEmails.ConfirmEmail("a@example.test", "en", link);
        var fallback = AccountEmails.ConfirmEmail("a@example.test", "de", link);

        mk.Subject.ShouldBe("Потврдете ја вашата е-пошта за ClassHub");
        en.Subject.ShouldBe("Confirm your email for ClassHub");
        fallback.Subject.ShouldBe(mk.Subject);
        en.HtmlBody.ShouldNotContain("<b>");
        en.HtmlBody.ShouldContain("a&lt;b&gt;&quot;c");
        en.TextBody.ShouldContain(link);
        en.Template.ShouldBe(AccountEmails.ConfirmEmailTemplate);
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(20, 3600)]
    public void Outbox_retries_back_off_exponentially_up_to_an_hour(int attempts, int seconds)
    {
        EmailOutboxDispatcher.Backoff(attempts).ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData("  Ana@Example.MK ", "ana@example.mk")]
    [InlineData("x@y.z", "x@y.z")]
    public void Emails_are_normalized_to_trimmed_lowercase(string input, string expected)
    {
        AccountService.NormalizeEmail(input).ShouldBe(expected);
    }
}
