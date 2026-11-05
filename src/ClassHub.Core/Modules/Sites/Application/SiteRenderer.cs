using System.Text.Json;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Domain;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.SharedKernel.Globalization;

namespace ClassHub.Core.Modules.Sites.Application;

/// <summary>
/// What publishing freezes: template, palette, language and sections of the saved draft. Profile data (name, photo,
/// subjects) and the offers marked "show on public site" stay live; the slug is the address and applies at once.
/// </summary>
internal sealed record SiteSnapshot(string Template, string Palette, string Language, IReadOnlyList<SiteSection> Sections)
{
    public static SiteSnapshot FromDraft(Site site) =>
        new(site.Template, site.Palette, site.Language, [.. SiteService.Read(site.DraftJson).Sections ?? []]);

    public static SiteSnapshot? Read(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<SiteSnapshot>(json, SiteService.Json);

    public string Serialize() => JsonSerializer.Serialize(this, SiteService.Json);
}

/// <summary>
/// Builds the <see cref="SiteRenderModel"/> for the preview and the public page (one renderer). It runs inside the
/// site's workspace (tenant filters apply) and copies only public fields: visible sections, the profile's public
/// fields, and public offers (only when their sections are visible).
/// </summary>
internal sealed class SiteRenderer(ITeacherPublicProfile profiles, ICatalogLookup catalog, IOfferLookup offers)
{
    public async Task<SiteRenderModel> RenderAsync(string slug, SiteSnapshot snapshot, Func<string, string> photoUrl, CancellationToken cancellationToken)
    {
        var en = snapshot.Language == SiteLanguages.English;
        var visible = snapshot.Sections.Where(s => s.Visible).ToList();
        bool Shows(string type) => visible.Any(s => s.Type == type);

        var teacher = await profiles.GetPublicProfileAsync(cancellationToken);
        var name = $"{teacher.FirstName} {teacher.LastName}".Trim();
        var hero = visible.FirstOrDefault(s => s.Type == SiteSectionTypes.Hero);
        var photo = hero is { ShowPhoto: true } && teacher.PhotoVersion is { } version ? photoUrl(version) : null;

        var teaching = new SiteTeaching([], [], [], null);
        if (Shows(SiteSectionTypes.Subjects))
        {
            var subjects = await catalog.SubjectsAsync(teacher.SubjectIds, cancellationToken);
            var levels = await catalog.LevelsAsync(teacher.LevelIds, cancellationToken);
            var languages = TeachingLanguages.All.Where(l => teacher.Languages.Contains(l.Code)).Select(l => en ? l.NameEn : l.NameMk);
            teaching = new SiteTeaching(
                [.. subjects.Select(s => en ? s.NameEn : s.NameMk)], [.. levels.Select(l => en ? l.NameEn : l.NameMk)], [.. languages], teacher.TeachingMode);
        }

        IReadOnlyList<SiteOfferView> offerViews = [];
        if (Shows(SiteSectionTypes.Offers))
        {
            var publicOffers = await offers.ListPublicAsync(cancellationToken);
            var subjectNames = (await catalog.SubjectsAsync([.. publicOffers.Select(o => o.SubjectId).Distinct()], cancellationToken))
                .ToDictionary(s => s.Id, s => en ? s.NameEn : s.NameMk);
            offerViews =
            [
                .. publicOffers.Select(o => new SiteOfferView(
                    o.Title, o.Description, o.Kind, subjectNames.GetValueOrDefault(o.SubjectId), o.DurationMinutes,
                    Currencies.ToMajorUnits(o.PriceMinor, o.Currency), o.Currency, o.Mode, o.LessonCount, o.Seats)),
            ];
        }

        // Visible sections without anything to show (no text, no items, no profile data) are left out, so visitors
        // never see empty boxes. Hero and contact always stay.
        bool HasContent(SiteSection section) => section.Type switch
        {
            SiteSectionTypes.Hero or SiteSectionTypes.Contact => true,
            SiteSectionTypes.Subjects => section.Text is not null || teaching.Subjects.Count > 0 || teaching.Levels.Count > 0
                || teaching.Languages.Count > 0 || teaching.Mode is not null,
            SiteSectionTypes.Offers => section.Text is not null || offerViews.Count > 0,
            _ when SiteSectionTypes.WithItems.Contains(section.Type ?? string.Empty) =>
                (section.Items ?? []).Any(i => i.Title is not null || i.Subtitle is not null || i.Text is not null),
            _ => section.Text is not null,
        };

        return new SiteRenderModel(
            slug,
            SiteTemplates.All.Contains(snapshot.Template) ? snapshot.Template : SiteTemplates.Classic,
            SitePalettes.Find(snapshot.Palette) ?? SitePalettes.All[0],
            en ? SiteLanguages.English : SiteLanguages.Macedonian,
            [.. visible.Where(HasContent)],
            name.Length == 0 ? null : name,
            photo,
            teaching,
            offerViews);
    }
}

internal static class SiteLanguages
{
    public const string Macedonian = "mk";
    public const string English = "en";
}
