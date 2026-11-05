using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Globalization;

namespace ClassHub.Core.Modules.Sites.Application;

/// <summary>
/// Title, description, canonical URL, social image and JSON-LD (schema.org Person with its offers, and FAQPage) of a
/// public page. Built only from the render model, so it can never contain more than the page itself shows.
/// </summary>
public static class SiteSeo
{
    public const int TitleMaxLength = 70;
    public const int DescriptionMaxLength = 160;

    /// <summary>
    /// JSON for a <c>&lt;script type="application/ld+json"&gt;</c> block. The encoder escapes &lt;, &gt;, &amp; and
    /// quotes (<…), so teacher text can never close the script element; Cyrillic stays readable.
    /// </summary>
    private static readonly JsonSerializerOptions JsonLdOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.Cyrillic, UnicodeRanges.GeneralPunctuation),
    };

    public static SiteSeoHead Build(SiteRenderModel site, string publicBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(site);
        var baseUrl = publicBaseUrl.TrimEnd('/');
        var canonical = $"{baseUrl}/{site.Slug}";
        var hero = site.Sections.FirstOrDefault(s => s.Type == SiteSectionTypes.Hero);
        var about = site.Sections.FirstOrDefault(s => s.Type == SiteSectionTypes.About);

        var title = (site.TeacherName, hero?.Title) switch
        {
            ({ } name, { } headline) => $"{name} — {headline}",
            ({ } name, null) => name,
            (null, { } headline) => headline,
            _ => site.Slug,
        };
        var description = FirstText(hero?.Text, about?.Text, site.Teaching.Subjects.Count > 0 ? string.Join(", ", site.Teaching.Subjects) : null)
            ?? title;
        var image = site.PhotoUrl is null ? null : baseUrl + site.PhotoUrl;

        return new SiteSeoHead(
            Truncate(Collapse(title), TitleMaxLength),
            Truncate(Collapse(description), DescriptionMaxLength),
            canonical,
            image,
            OgLocale(site.Language),
            JsonLd(site, canonical, image, Truncate(Collapse(description), DescriptionMaxLength)));
    }

    public static string OgLocale(string language) => language == "en" ? "en_US" : "mk_MK";

    /// <summary>Serializes a JSON-LD object safely for an HTML script data block.</summary>
    public static string Serialize(JsonNode node) => node.ToJsonString(JsonLdOptions);

    private static string JsonLd(SiteRenderModel site, string canonical, string? image, string description)
    {
        var person = new JsonObject
        {
            ["@type"] = "Person",
            ["@id"] = $"{canonical}#person",
            ["name"] = site.TeacherName ?? site.Slug,
            ["url"] = canonical,
            ["description"] = description,
        };
        if (image is not null)
        {
            person["image"] = image;
        }

        if (site.Teaching.Subjects.Count > 0)
        {
            person["knowsAbout"] = new JsonArray([.. site.Teaching.Subjects.Select(s => (JsonNode)s)]);
        }

        if (site.Offers.Count > 0)
        {
            person["makesOffer"] = new JsonArray([.. site.Offers.Select(o => (JsonNode)Offer(o))]);
        }

        var graph = new JsonArray(person);
        var faq = site.Sections.FirstOrDefault(s => s.Type == SiteSectionTypes.Faq);
        var questions = (faq?.Items ?? []).Where(i => i.Title is not null && i.Text is not null).ToList();
        if (questions.Count > 0)
        {
            graph.Add(new JsonObject
            {
                ["@type"] = "FAQPage",
                ["mainEntity"] = new JsonArray([.. questions.Select(q => (JsonNode)new JsonObject
                {
                    ["@type"] = "Question",
                    ["name"] = q.Title,
                    ["acceptedAnswer"] = new JsonObject { ["@type"] = "Answer", ["text"] = q.Text },
                })]),
            });
        }

        return Serialize(new JsonObject { ["@context"] = "https://schema.org", ["@graph"] = graph });
    }

    private static JsonObject Offer(SiteOfferView offer)
    {
        var service = new JsonObject { ["@type"] = "Service", ["name"] = offer.Title };
        if (offer.Subject is not null)
        {
            service["serviceType"] = offer.Subject;
        }

        var node = new JsonObject
        {
            ["@type"] = "Offer",
            ["name"] = offer.Title,
            ["price"] = offer.Price.ToString("F" + Currencies.MinorDigits(offer.Currency), CultureInfo.InvariantCulture),
            ["priceCurrency"] = offer.Currency,
            ["itemOffered"] = service,
        };
        if (offer.Description is not null)
        {
            node["description"] = Truncate(Collapse(offer.Description), DescriptionMaxLength);
        }

        return node;
    }

    private static string? FirstText(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var part in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(part);
        }

        return builder.ToString();
    }

    /// <summary>Cuts at a word boundary and adds an ellipsis when the text is longer than <paramref name="max"/>.</summary>
    internal static string Truncate(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var cut = value[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd(' ', ',', '.', ';', ':', '—', '-') + "…";
    }
}
