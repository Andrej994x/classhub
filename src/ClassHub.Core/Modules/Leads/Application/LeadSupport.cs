using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ClassHub.Core.Infrastructure.Email;
using Microsoft.AspNetCore.DataProtection;

namespace ClassHub.Core.Modules.Leads.Application;

/// <summary>Public form limits (section <c>Leads</c>, D-19) and the provisional minors threshold (D-18).</summary>
public sealed class LeadOptions
{
    public const string SectionName = "Leads";

    /// <summary>Submissions sooner than this after the form was loaded are treated as automated and dropped.</summary>
    [Range(0, 300)] public int MinimumFillSeconds { get; set; } = 3;

    /// <summary>Accepted leads per site and hour, whatever the client address (in addition to the per-IP limiter).</summary>
    [Range(1, 10_000)] public int MaxPerSitePerHour { get; set; } = 20;

    /// <summary>Provisional age threshold of the minors notice; configurable until the legal wording is final.</summary>
    [Range(1, 21)] public int MinorAgeThreshold { get; set; } = 16;
}

/// <summary>
/// The consent and privacy notice of the public forms (D-18): <b>provisional</b> wording, versioned. Every lead stores
/// the version it was sent with and the time. A wording change gets a new version. The final legal text blocks Beta
/// (ROADMAP J.4).
/// </summary>
public static class LeadConsent
{
    public const string Version = "2026-11-05.1";

    public static string Text(string language) => language == "en"
        ? "I agree that the teacher receives my name, email, phone number (if given) and message through ClassHub to answer my request. They are not shared with anyone else. I can ask the teacher to delete them at any time."
        : "Се согласувам наставникот преку ClassHub да ги добие моето име, е-пошта, телефон (ако го наведам) и пораката за да одговори на моето барање. Тие не се споделуваат со никој друг. Во секое време можам да побарам од наставникот да ги избрише.";

    public static string MinorNotice(string language, int age) => language == "en"
        ? $"I am at least {age} years old, or I am the parent or guardian sending this request for a child."
        : $"Имам најмалку {age} години, или сум родител или старател што го праќа барањето за дете.";
}

/// <summary>
/// Signed form tokens (Data Protection) for the minimum fill time: a token names the site and when the form was
/// loaded. Tokens older than <see cref="Lifetime"/> are expired (the form fetches a new one).
/// </summary>
internal sealed class LeadFormTokens(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("ClassHub.Leads.Form.v1");

    private sealed record Payload(string Slug, long IssuedUnixMs);

    public string Issue(string slug, DateTimeOffset? issuedAt = null) =>
        _protector.Protect(JsonSerializer.Serialize(new Payload(slug, (issuedAt ?? clock.GetUtcNow()).ToUnixTimeMilliseconds())));

    /// <summary>When the form for <paramref name="slug"/> was loaded, or null for invalid, foreign or expired tokens.</summary>
    public DateTimeOffset? Validate(string? token, string slug)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            if (payload is null || payload.Slug != slug)
            {
                return null;
            }

            var issued = DateTimeOffset.FromUnixTimeMilliseconds(payload.IssuedUnixMs);
            var now = clock.GetUtcNow();
            return issued <= now.AddSeconds(5) && now - issued <= Lifetime ? issued : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>
/// The teacher's "new request" email: minimal data only (no visitor name, contact details or message), just the kind
/// of request and a link to the inbox, where the teacher signs in to read it.
/// </summary>
internal static class LeadEmails
{
    public const string NewLeadTemplate = "leads.new";

    public static EmailMessage NewLead(string to, string locale, string inboxLink, Domain.LeadType type)
    {
        var en = locale == "en";
        var subject = en ? "New request on your ClassHub page" : "Ново барање на вашата ClassHub страница";
        var intro = (en, type) switch
        {
            (true, Domain.LeadType.LessonRequest) => "Someone asked for a lesson on your public page.",
            (true, Domain.LeadType.Diagnostic) => "Someone took one of your tests and would like to be contacted.",
            (true, _) => "Someone sent you a message on your public page.",
            (false, Domain.LeadType.LessonRequest) => "Некој побара час на вашата јавна страница.",
            (false, Domain.LeadType.Diagnostic) => "Некој реши еден од вашите тестови и сака да го контактирате.",
            (false, _) => "Некој ви испрати порака на вашата јавна страница.",
        };
        var action = en ? "Open your requests" : "Отворете ги барањата";
        var safeLink = WebUtility.HtmlEncode(inboxLink);
        var html = $"<p>{WebUtility.HtmlEncode(intro)}</p><p><a href=\"{safeLink}\">{WebUtility.HtmlEncode(action)}</a></p>";
        var text = $"{intro}\n\n{action}: {inboxLink}\n";
        return new EmailMessage(to, subject, html, text, NewLeadTemplate);
    }
}
