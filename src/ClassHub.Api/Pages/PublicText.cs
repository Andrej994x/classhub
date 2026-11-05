using System.Collections.Frozen;
using System.Globalization;

namespace ClassHub.Api.Pages;

/// <summary>
/// Texts of the server-rendered public pages (page chrome only; teacher content is rendered as entered) in
/// Macedonian and English. A unit test checks that both languages have the same keys.
/// </summary>
public static class PublicText
{
    public static FrozenDictionary<string, string> Mk { get; } = new Dictionary<string, string>
    {
        ["preview.banner"] = "Преглед — не е објавено",
        ["site.contactCta"] = "Контактирајте ме",
        ["site.subjects"] = "Предмети",
        ["site.levels"] = "Нивоа",
        ["site.languages"] = "Јазици на настава",
        ["site.mode"] = "Начин на настава",
        ["site.mode.Online"] = "Онлајн",
        ["site.mode.InPerson"] = "Во живо",
        ["site.mode.Hybrid"] = "Онлајн и во живо",
        ["site.kind.SingleLesson"] = "Поединечен час",
        ["site.kind.Package"] = "Пакет часови",
        ["site.kind.GroupProgram"] = "Групна програма",
        ["site.minutes"] = "{0} мин.",
        ["site.lessons"] = "{0} часа",
        ["site.seats"] = "до {0} ученици",
        ["site.noOffers"] = "Понудите наскоро ќе бидат објавени.",
        ["site.contactPreview"] = "Формуларот за контакт се прикажува на објавената страница.",
        ["site.contactNoScript"] = "За да испратите барање, вклучете JavaScript во прелистувачот.",
        ["site.tests"] = "Бесплатни тестови",
        ["site.testsLead"] = "Проверете го вашето знаење за неколку минути.",
        ["test.kicker"] = "Бесплатен тест",
        ["test.difficulty.Beginner"] = "Почетно ниво",
        ["test.difficulty.Intermediate"] = "Средно ниво",
        ["test.difficulty.Advanced"] = "Напредно ниво",
        ["test.questions"] = "{0} прашања",
        ["test.points"] = "{0} бода",
        ["test.privacy"] = "Тестот е анонимен: не бараме име ниту е-пошта. Резултатот го гледате веднаш, а наставникот го гледа само ако подоцна сами одлучите да го споделите.",
        ["test.noScript"] = "За да го решите тестот, вклучете JavaScript во прелистувачот.",
        ["site.copyLink"] = "Копирај линк",
        ["site.copied"] = "Линкот е копиран",
        ["site.madeWith"] = "Направено со ClassHub",
        ["notFound.title"] = "Страницата не е пронајдена",
        ["notFound.text"] = "Оваа страница не постои или моментално не е објавена.",
        ["notFound.home"] = "Кон ClassHub",
        ["landing.title"] = "ClassHub — вашата онлајн настава на едно место",
        ["landing.description"] = "Професионална веб-страница, понуди и прашања од ученици за приватни наставници — бесплатно за почеток.",
        ["landing.headline"] = "Вашата настава, вашата страница.",
        ["landing.lead"] = "ClassHub им помага на приватните наставници да се претстават онлајн, да ги објават своите понуди и да примаат барања за часови.",
        ["landing.register"] = "Креирајте бесплатна страница",
        ["landing.signIn"] = "Најава",
        ["landing.feature1.title"] = "Ваша веб-страница",
        ["landing.feature1.text"] = "Изберете адреса, изглед и бои, напишете ги текстовите и објавете ја страницата за неколку минути.",
        ["landing.feature2.title"] = "Понуди и цени",
        ["landing.feature2.text"] = "Поединечни часови, пакети и групни програми со јасни цени и времетраење.",
        ["landing.feature3.title"] = "Лесно се наоѓате",
        ["landing.feature3.text"] = "Страниците се брзи на мобилен, споделливи на социјални мрежи и подготвени за пребарувачи.",
        ["landing.otherLanguage"] = "English",
        ["landing.footer"] = "ClassHub — алатки за приватни наставници.",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static FrozenDictionary<string, string> En { get; } = new Dictionary<string, string>
    {
        ["preview.banner"] = "Preview — not published",
        ["site.contactCta"] = "Contact me",
        ["site.subjects"] = "Subjects",
        ["site.levels"] = "Levels",
        ["site.languages"] = "Teaching languages",
        ["site.mode"] = "Teaching mode",
        ["site.mode.Online"] = "Online",
        ["site.mode.InPerson"] = "In person",
        ["site.mode.Hybrid"] = "Online and in person",
        ["site.kind.SingleLesson"] = "Single lesson",
        ["site.kind.Package"] = "Lesson package",
        ["site.kind.GroupProgram"] = "Group program",
        ["site.minutes"] = "{0} min",
        ["site.lessons"] = "{0} lessons",
        ["site.seats"] = "up to {0} students",
        ["site.noOffers"] = "Offers will be published soon.",
        ["site.contactPreview"] = "The contact form is shown on the published page.",
        ["site.contactNoScript"] = "To send a request, turn on JavaScript in your browser.",
        ["site.tests"] = "Free tests",
        ["site.testsLead"] = "Check what you know in a few minutes.",
        ["test.kicker"] = "Free test",
        ["test.difficulty.Beginner"] = "Beginner level",
        ["test.difficulty.Intermediate"] = "Intermediate level",
        ["test.difficulty.Advanced"] = "Advanced level",
        ["test.questions"] = "{0} questions",
        ["test.points"] = "{0} points",
        ["test.privacy"] = "The test is anonymous: we ask for no name or email. You see your result at once; the teacher sees it only if you later choose to share it.",
        ["test.noScript"] = "To take the test, turn on JavaScript in your browser.",
        ["site.copyLink"] = "Copy link",
        ["site.copied"] = "Link copied",
        ["site.madeWith"] = "Made with ClassHub",
        ["notFound.title"] = "Page not found",
        ["notFound.text"] = "This page does not exist or is not published at the moment.",
        ["notFound.home"] = "Go to ClassHub",
        ["landing.title"] = "ClassHub — your private teaching in one place",
        ["landing.description"] = "A professional website, offers and lesson requests for private teachers — free to start.",
        ["landing.headline"] = "Your teaching, your page.",
        ["landing.lead"] = "ClassHub helps private teachers present themselves online, publish their offers and receive lesson requests.",
        ["landing.register"] = "Create your free page",
        ["landing.signIn"] = "Sign in",
        ["landing.feature1.title"] = "Your own website",
        ["landing.feature1.text"] = "Choose an address, a layout and colours, write your texts and publish your page in minutes.",
        ["landing.feature2.title"] = "Offers and prices",
        ["landing.feature2.text"] = "Single lessons, packages and group programs with clear prices and durations.",
        ["landing.feature3.title"] = "Easy to find",
        ["landing.feature3.text"] = "Pages are fast on phones, shareable on social networks and ready for search engines.",
        ["landing.otherLanguage"] = "Македонски",
        ["landing.footer"] = "ClassHub — tools for private teachers.",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The text for <paramref name="key"/> in <paramref name="language"/> (Macedonian unless "en").</summary>
    public static string Get(string language, string key) =>
        (language == "en" ? En : Mk).TryGetValue(key, out var text) ? text : key;

    public static string Format(string language, string key, object value) =>
        string.Format(Culture(language), Get(language, key), value);

    public static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "mk-MK");
}
