using System.Globalization;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Teachers.Contracts;

namespace ClassHub.UnitTests.Sites;

public sealed class SiteRulesTests
{
    [Theory]
    [InlineData("ana-math")]
    [InlineData("ana")]
    [InlineData("matematika-so-ana-2026")]
    [InlineData("a1b")]
    public void Valid_slugs_are_accepted(string slug) => SiteSlugs.Problem(slug).ShouldBeNull();

    [Theory]
    [InlineData("ab", "length")]
    [InlineData("a-very-long-address-that-goes-beyond-forty-c", "length")]
    [InlineData("Ana-Math", "format")]
    [InlineData("-ana", "format")]
    [InlineData("ana-", "format")]
    [InlineData("ana--math", "format")]
    [InlineData("ana_math", "format")]
    [InlineData("ана", "format")]
    [InlineData("ana math", "format")]
    [InlineData("app", "reserved")]
    [InlineData("admin", "reserved")]
    [InlineData("preview", "reserved")]
    [InlineData("api", "reserved")]
    public void Invalid_or_reserved_slugs_are_refused(string slug, string reason) => SiteSlugs.Problem(slug).ShouldBe(reason);

    [Theory]
    [InlineData("Ана", "Петровска", "ana-petrovska")]
    [InlineData("Ѓорѓи", "Џамбаз", "gjorgji-djambaz")]
    [InlineData("John", "O'Neil", "john-o-neil")]
    public void Slugs_are_suggested_from_the_teacher_name(string first, string last, string expected) =>
        SiteService.SuggestSlug(new TeacherSummary(first, last, null)).ShouldBe(expected);

    [Fact]
    public void Without_a_usable_name_a_random_teacher_slug_is_suggested() =>
        SiteService.SuggestSlug(new TeacherSummary(null, null, null)).ShouldMatch("^teacher-[0-9a-f]{6}$");

    [Fact]
    public void Every_palette_meets_wcag_aa_contrast_for_text_and_buttons()
    {
        foreach (var palette in SitePalettes.All)
        {
            Contrast(palette.Text, palette.Background).ShouldBeGreaterThanOrEqualTo(4.5, $"{palette.Key} text");
            Contrast(palette.Text, palette.Surface).ShouldBeGreaterThanOrEqualTo(4.5, $"{palette.Key} text on surface");
            Contrast(palette.Muted, palette.Surface).ShouldBeGreaterThanOrEqualTo(4.5, $"{palette.Key} muted");
            Contrast(palette.OnPrimary, palette.Primary).ShouldBeGreaterThanOrEqualTo(4.5, $"{palette.Key} button");
            Contrast(palette.Primary, palette.Surface).ShouldBeGreaterThanOrEqualTo(4.5, $"{palette.Key} headings");
        }
    }

    [Fact]
    public void The_default_content_is_valid_in_both_languages()
    {
        var validator = new SiteContentValidator();
        validator.Validate(SiteService.DefaultContent("mk")).IsValid.ShouldBeTrue();
        validator.Validate(SiteService.DefaultContent("en")).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Content_must_have_every_section_once_and_items_only_where_allowed()
    {
        var validator = new SiteContentValidator();
        var content = SiteService.DefaultContent("mk");

        validator.Validate(content with { Sections = [.. content.Sections!.Skip(1)] }).IsValid.ShouldBeFalse();
        validator.Validate(content with { Sections = [.. content.Sections!, content.Sections![0]] }).IsValid.ShouldBeFalse();
        var withItemsInAbout = content.Sections!.Select(s => s.Type == SiteSectionTypes.About ? s with { Items = [new SiteSectionItem("x", null, null)] } : s).ToList();
        validator.Validate(content with { Sections = withItemsInAbout }).IsValid.ShouldBeFalse();
        var tooLong = content.Sections!.Select(s => s.Type == SiteSectionTypes.About ? s with { Text = new string('a', SiteContentLimits.TextMax + 1) } : s).ToList();
        validator.Validate(content with { Sections = tooLong }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Normalizing_trims_texts_and_drops_fields_a_section_does_not_use()
    {
        var content = SiteService.DefaultContent("en");
        var messy = content.Sections!.Select(s => s.Type == SiteSectionTypes.About ? s with { Title = "  About  ", ShowPhoto = true, Items = [] } : s).ToList();

        var about = SiteService.Normalize(new SiteContent(messy)).Sections!.Single(s => s.Type == SiteSectionTypes.About);

        (about.Title, about.ShowPhoto, about.Items).ShouldBe(("About", false, (IReadOnlyList<SiteSectionItem>?)null));
    }

    /// <summary>WCAG 2.x contrast ratio of two #RRGGBB colours.</summary>
    private static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            double Channel(int offset)
            {
                var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
        }

        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
