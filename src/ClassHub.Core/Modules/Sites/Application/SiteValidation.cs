using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using FluentValidation;

namespace ClassHub.Core.Modules.Sites.Application;

/// <summary>Limits of the structured section content (plain text; the renderer encodes everything).</summary>
public static class SiteContentLimits
{
    public const int TitleMax = 120;
    public const int TextMax = 3000;
    public const int ItemTitleMax = 200;
    public const int ItemSubtitleMax = 80;
    public const int ItemTextMax = 1500;
    public const int MaxItems = 30;
}

internal sealed class SaveSiteRequestValidator : AbstractValidator<SaveSiteRequest>
{
    public SaveSiteRequestValidator()
    {
        RuleFor(x => x.Slug).Must(s => SiteSlugs.Problem(s) is null)
            .WithMessage(x => $"Invalid address ({SiteSlugs.Problem(x.Slug)}).");
        RuleFor(x => x.Template).Must(t => SiteTemplates.All.Contains(t)).WithMessage("Unknown template.");
        RuleFor(x => x.Palette).Must(p => SitePalettes.Find(p) is not null).WithMessage("Unknown colour palette.");
        RuleFor(x => x.Language).Must(SupportedLocales.IsSupported).WithMessage("Language must be 'mk' or 'en'.");
        RuleFor(x => x.Draft).Cascade(CascadeMode.Stop).NotNull().SetValidator(new SiteContentValidator()!);
    }
}

internal sealed class SiteContentValidator : AbstractValidator<SiteContent>
{
    public SiteContentValidator()
    {
        RuleFor(x => x.Sections).Cascade(CascadeMode.Stop).NotNull()
            .Must(sections => sections!.All(s => s is not null)).WithMessage("Sections cannot be empty values.")
            .Must(sections => sections!.Select(s => s.Type).Order(StringComparer.Ordinal).SequenceEqual(SiteSectionTypes.All.Order(StringComparer.Ordinal)))
            .WithMessage("Every section type must appear exactly once.");
        RuleForEach(x => x.Sections).ChildRules(section =>
        {
            section.RuleFor(s => s.Type).NotEmpty();
            section.RuleFor(s => s.Title).MaximumLength(SiteContentLimits.TitleMax);
            section.RuleFor(s => s.Text).MaximumLength(SiteContentLimits.TextMax);
            section.RuleFor(s => s.Items).Must(items => items is null || items.Count <= SiteContentLimits.MaxItems)
                .WithMessage($"At most {SiteContentLimits.MaxItems} items.");
            section.RuleFor(s => s.Items).Must(items => items is null || items.All(i => i is not null))
                .WithMessage("Items cannot be empty values.");
            section.RuleFor(s => s.Items).Must(items => items is null || items.Count == 0)
                .WithMessage("This section has no items.")
                .When(s => s.Type is not null && !SiteSectionTypes.WithItems.Contains(s.Type));
            section.RuleForEach(s => s.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.Title).MaximumLength(SiteContentLimits.ItemTitleMax);
                item.RuleFor(i => i.Subtitle).MaximumLength(SiteContentLimits.ItemSubtitleMax);
                item.RuleFor(i => i.Text).MaximumLength(SiteContentLimits.ItemTextMax);
            });
        });
    }
}
