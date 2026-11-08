using System.Net;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Modules.Identity.Contracts;

namespace ClassHub.Core.Modules.Students.Application;

/// <summary>Localized (mk/en) student emails. The invitation token travels in the URL fragment (never in server logs).</summary>
internal static class StudentEmails
{
    public const string InvitationTemplate = "student.invitation";

    public static EmailMessage Invitation(string to, string locale, string teacherName, string studentFirstName, bool isMinor, string link)
    {
        var english = SupportedLocales.OrDefault(locale) == SupportedLocales.English;
        var subject = english ? $"{teacherName} invites you to ClassHub" : $"{teacherName} ве поканува на ClassHub";
        var intro = english
            ? $"Hello {studentFirstName}, {teacherName} invites you to your own ClassHub account, where you will see your lessons and materials. The link is valid for 7 days:"
            : $"Здраво {studentFirstName}, {teacherName} ве поканува на ваша ClassHub сметка, каде ќе ги гледате часовите и материјалите. Линкот важи 7 дена:";
        var action = english ? "Accept the invitation" : "Прифати ја поканата";
        var minor = isMinor
            ? english
                ? "The student is recorded as a minor: a parent or guardian must agree before the account is created."
                : "Ученикот е запишан како малолетен: родител или старател треба да се согласи пред да се создаде сметката."
            : null;
        var footer = english
            ? "If you did not expect this invitation, ignore this message."
            : "Ако не ја очекувавте поканата, игнорирајте ја пораката.";

        var safeLink = WebUtility.HtmlEncode(link);
        var html =
            $"<p>{WebUtility.HtmlEncode(intro)}</p>" +
            $"<p><a href=\"{safeLink}\">{WebUtility.HtmlEncode(action)}</a></p>" +
            (minor is null ? string.Empty : $"<p>{WebUtility.HtmlEncode(minor)}</p>") +
            $"<p style=\"color:#666\">{WebUtility.HtmlEncode(footer)}</p>";
        var text = $"{intro}\n\n{link}\n\n{(minor is null ? string.Empty : minor + "\n\n")}{footer}\n";
        return new EmailMessage(to, subject, html, text, InvitationTemplate);
    }
}
