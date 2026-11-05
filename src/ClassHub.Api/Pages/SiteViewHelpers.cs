using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Globalization;

namespace ClassHub.Api.Pages;

/// <summary>Formatting and template classes for the site renderer (<c>Shared/_SiteBody.cshtml</c>).</summary>
public static class SiteViewHelpers
{
    /// <summary>"1.200,00 MKD" / "1,200.00 MKD": the amount in the site language, always with the currency code.</summary>
    public static string Price(SiteOfferView offer, string language)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var digits = Currencies.MinorDigits(offer.Currency);
        return $"{offer.Price.ToString("N" + digits, PublicText.Culture(language))} {offer.Currency}";
    }

    /// <summary>Duration, lessons and seats of an offer, e.g. "60 min · 10 lessons".</summary>
    public static string Details(SiteOfferView offer, string language)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var parts = new List<string> { PublicText.Format(language, "site.minutes", offer.DurationMinutes) };
        if (offer.LessonCount is { } lessons)
        {
            parts.Add(PublicText.Format(language, "site.lessons", lessons));
        }

        if (offer.Seats is { } seats)
        {
            parts.Add(PublicText.Format(language, "site.seats", seats));
        }

        parts.Add(PublicText.Get(language, $"site.mode.{offer.Mode}"));
        return string.Join(" · ", parts);
    }

    public static string SectionClass(string template) => template switch
    {
        SiteTemplates.Modern => "rounded-r-2xl border-l-4 border-site-primary bg-site-surface p-6 shadow-sm",
        SiteTemplates.Minimal => "border-t border-site-muted/30 py-8",
        _ => "rounded-2xl bg-site-surface p-6 shadow-sm",
    };

    public static string HeroClass(string template) => template switch
    {
        SiteTemplates.Modern => "rounded-2xl bg-site-primary p-8 text-site-on-primary shadow-sm",
        SiteTemplates.Minimal => "py-10",
        _ => "rounded-2xl bg-site-surface p-8 shadow-sm",
    };

    /// <summary>Headline colour inside the hero (the modern hero is a band in the primary colour).</summary>
    public static string HeroHeadingClass(string template) =>
        template == SiteTemplates.Modern ? "text-site-on-primary" : "text-site-primary";

    public static string HeroTextClass(string template) =>
        template == SiteTemplates.Modern ? "text-site-on-primary" : "text-site-muted";
}
