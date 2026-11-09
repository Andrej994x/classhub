using System.Net;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;

namespace ClassHub.Core.Modules.Notifications.Application;

/// <summary>What a notification says: the teacher, the lesson or homework and its time (never anyone else's data).</summary>
/// <remarks><paramref name="Kept"/>: when a series ends, its next lesson that stays (changed on its own, so kept).</remarks>
internal sealed record NotificationContent(string TeacherName, string StudentFirstName, string Title, string? When, DateOnly? DueDate, string? Link, string? Kept = null);

/// <summary>Localized (mk/en) notification emails (R2-P11). Lesson times are local to the lesson's time zone and name it.</summary>
internal static class NotificationEmails
{
    public static string Template(NotificationKind kind) => kind switch
    {
        NotificationKind.LessonScheduled => "notify.lesson-scheduled",
        NotificationKind.LessonRescheduled => "notify.lesson-rescheduled",
        NotificationKind.LessonCancelled => "notify.lesson-cancelled",
        NotificationKind.LessonReminder => "notify.lesson-reminder",
        NotificationKind.HomeworkAssigned => "notify.homework-assigned",
        NotificationKind.SeriesScheduled => "notify.series-scheduled",
        NotificationKind.SeriesEnded => "notify.series-ended",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static EmailMessage Create(NotificationKind kind, string to, string locale, NotificationContent content)
    {
        var en = SupportedLocales.OrDefault(locale) == SupportedLocales.English;
        var (subject, line) = kind switch
        {
            NotificationKind.LessonScheduled => en
                ? ($"New lesson with {content.TeacherName}: {content.Title}", $"{content.TeacherName} scheduled a lesson for you: {content.Title}, {content.When}.")
                : ($"Нов час со {content.TeacherName}: {content.Title}", $"{content.TeacherName} ви закажа час: {content.Title}, {content.When}."),
            NotificationKind.SeriesScheduled => en
                ? ($"New recurring lessons with {content.TeacherName}: {content.Title}",
                    $"{content.TeacherName} scheduled recurring lessons for you: {content.Title}, starting {content.When}. You will get a reminder before each lesson.")
                : ($"Нови редовни часови со {content.TeacherName}: {content.Title}",
                    $"{content.TeacherName} ви закажа редовни часови: {content.Title}, почнувајќи од {content.When}. Пред секој час ќе добиете потсетник."),
            NotificationKind.SeriesEnded => en
                ? ($"Recurring lessons cancelled: {content.Title}", $"{content.TeacherName} cancelled your recurring lessons {content.Title} from {content.When} on."
                    + (content.Kept is null ? string.Empty : $" Lessons that were changed individually stay as scheduled; the next one is {content.Kept}."))
                : ($"Редовните часови се откажани: {content.Title}", $"{content.TeacherName} ги откажа редовните часови {content.Title} од {content.When} натаму."
                    + (content.Kept is null ? string.Empty : $" Часовите што беа посебно изменети остануваат закажани; следниот е {content.Kept}.")),
            NotificationKind.LessonRescheduled => en
                ? ($"Lesson moved: {content.Title}", $"{content.TeacherName} moved your lesson {content.Title} to {content.When}.")
                : ($"Часот е преместен: {content.Title}", $"{content.TeacherName} го премести часот {content.Title} за {content.When}."),
            NotificationKind.LessonCancelled => en
                ? ($"Lesson cancelled: {content.Title}", $"{content.TeacherName} cancelled your lesson {content.Title} on {content.When}.")
                : ($"Часот е откажан: {content.Title}", $"{content.TeacherName} го откажа часот {content.Title} на {content.When}."),
            NotificationKind.LessonReminder => en
                ? ($"Reminder: {content.Title}", $"A reminder of your lesson with {content.TeacherName}: {content.Title}, {content.When}.")
                : ($"Потсетник: {content.Title}", $"Потсетник за вашиот час со {content.TeacherName}: {content.Title}, {content.When}."),
            NotificationKind.HomeworkAssigned => en
                ? ($"New homework: {content.Title}", $"{content.TeacherName} gave you new homework: {content.Title}{(content.DueDate is { } d ? $", due {d:yyyy-MM-dd}" : string.Empty)}.")
                : ($"Нова домашна задача: {content.Title}", $"{content.TeacherName} ви зададе домашна задача: {content.Title}{(content.DueDate is { } due ? $", рок {due:dd.MM.yyyy}" : string.Empty)}."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var greeting = en ? $"Hello {content.StudentFirstName}," : $"Здраво {content.StudentFirstName},";
        var action = en ? "Open ClassHub" : "Отвори ClassHub";
        var footer = en
            ? "You get this email because your teacher uses ClassHub. Reminders and homework emails can be switched off in your settings."
            : "Ја добивате пораката бидејќи вашиот наставник користи ClassHub. Потсетниците и пораките за домашни задачи можете да ги исклучите во поставките.";

        var html = $"<p>{WebUtility.HtmlEncode(greeting)}</p><p>{WebUtility.HtmlEncode(line)}</p>" +
            (content.Link is null ? string.Empty : $"<p><a href=\"{WebUtility.HtmlEncode(content.Link)}\">{WebUtility.HtmlEncode(action)}</a></p>") +
            $"<p style=\"color:#666\">{WebUtility.HtmlEncode(footer)}</p>";
        var text = $"{greeting}\n\n{line}\n\n{(content.Link is null ? string.Empty : content.Link + "\n\n")}{footer}\n";
        return new EmailMessage(to, subject, html, text, Template(kind));
    }
}
