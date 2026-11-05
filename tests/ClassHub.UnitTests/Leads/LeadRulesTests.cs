using ClassHub.Core.Modules.Leads.Application;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Leads.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace ClassHub.UnitTests.Leads;

/// <summary>R1-P5: status transitions, form tokens, request validation and the minimal teacher email.</summary>
public sealed class LeadRulesTests
{
    [Theory]
    [InlineData(LeadStatus.New, LeadStatus.Contacted, true)]
    [InlineData(LeadStatus.New, LeadStatus.Converted, true)]
    [InlineData(LeadStatus.New, LeadStatus.Closed, true)]
    [InlineData(LeadStatus.Contacted, LeadStatus.New, true)]
    [InlineData(LeadStatus.Contacted, LeadStatus.Converted, true)]
    [InlineData(LeadStatus.Contacted, LeadStatus.Closed, true)]
    [InlineData(LeadStatus.Converted, LeadStatus.Contacted, true)]
    [InlineData(LeadStatus.Converted, LeadStatus.Closed, true)]
    [InlineData(LeadStatus.Converted, LeadStatus.New, false)]
    [InlineData(LeadStatus.Closed, LeadStatus.New, true)]
    [InlineData(LeadStatus.Closed, LeadStatus.Contacted, true)]
    [InlineData(LeadStatus.Closed, LeadStatus.Converted, false)]
    public void Status_transitions_follow_the_pipeline(LeadStatus from, LeadStatus to, bool allowed) =>
        LeadStatusRules.CanChange(from, to).ShouldBe(allowed);

    [Fact]
    public void Every_status_has_rules_and_none_allows_staying_in_place()
    {
        foreach (var status in Enum.GetValues<LeadStatus>())
        {
            LeadStatusRules.CanChange(status, status).ShouldBeFalse();
            Enum.GetValues<LeadStatus>().Count(to => LeadStatusRules.CanChange(status, to)).ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void Form_tokens_name_their_site_and_expire()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
        var tokens = new LeadFormTokens(new EphemeralDataProtectionProvider(), clock);
        var token = tokens.Issue("ana-math");

        tokens.Validate(token, "ana-math").ShouldBe(clock.GetUtcNow());
        tokens.Validate(token, "other-site").ShouldBeNull();
        tokens.Validate(token + "x", "ana-math").ShouldBeNull();
        tokens.Validate(null, "ana-math").ShouldBeNull();
        tokens.Validate(new string('a', 3000), "ana-math").ShouldBeNull();

        clock.Advance(LeadFormTokens.Lifetime + TimeSpan.FromSeconds(1));
        tokens.Validate(token, "ana-math").ShouldBeNull();
        tokens.Validate(tokens.Issue("ana-math", clock.GetUtcNow().AddMinutes(5)), "ana-math").ShouldBeNull(); // from the future
    }

    private static SubmitLeadRequest Valid() =>
        new("Message", "Ана", "ana@example.test", "+389 70 123 456", "Здраво", null, null, true, true, LeadConsent.Version, "token", "");

    [Fact]
    public void A_complete_request_is_valid()
    {
        var result = new SubmitLeadRequestValidator().Validate(Valid());
        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("email")]
    [InlineData("phone")]
    [InlineData("message")]
    [InlineData("consent")]
    [InlineData("ageConfirmed")]
    [InlineData("consentVersion")]
    public void Invalid_fields_are_reported(string field)
    {
        var request = field switch
        {
            "type" => Valid() with { Type = "message" },
            "email" => Valid() with { Email = "ana" },
            "phone" => Valid() with { Phone = "ab" },
            "message" => Valid() with { Message = "  " },
            "consent" => Valid() with { Consent = false },
            "ageConfirmed" => Valid() with { AgeConfirmed = false },
            _ => Valid() with { ConsentVersion = "old" },
        };

        new SubmitLeadRequestValidator().Validate(request).Errors.ShouldContain(e => string.Equals(e.PropertyName, field, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_lesson_request_needs_no_message_and_no_phone() =>
        new SubmitLeadRequestValidator().Validate(Valid() with { Type = "LessonRequest", Message = null, Phone = null }).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("mk", "Ново барање на вашата ClassHub страница")]
    [InlineData("en", "New request on your ClassHub page")]
    public void The_teacher_email_is_localized_and_links_to_the_inbox(string locale, string subject)
    {
        var mail = LeadEmails.NewLead("teacher@example.test", locale, "https://classhub.app/app/w/1/leads", LeadType.LessonRequest);

        (mail.ToAddress, mail.Subject, mail.Template).ShouldBe(("teacher@example.test", subject, LeadEmails.NewLeadTemplate));
        mail.TextBody.ShouldContain("https://classhub.app/app/w/1/leads");
        mail.HtmlBody.ShouldContain("href=\"https://classhub.app/app/w/1/leads\"");
    }

    [Fact]
    public void The_consent_texts_exist_in_both_languages_and_name_the_age_threshold()
    {
        LeadConsent.Text("mk").ShouldNotBe(LeadConsent.Text("en"));
        LeadConsent.MinorNotice("mk", 16).ShouldContain("16");
        LeadConsent.MinorNotice("en", 18).ShouldContain("18");
        LeadConsent.Version.Length.ShouldBeLessThanOrEqualTo(Lead.ConsentVersionMaxLength);
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
