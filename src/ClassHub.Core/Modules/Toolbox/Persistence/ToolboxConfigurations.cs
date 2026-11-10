using System.Security.Cryptography;
using System.Text;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Toolbox.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Toolbox.Persistence;

internal sealed class ToolConfiguration : IEntityTypeConfiguration<Tool>
{
    public void Configure(EntityTypeBuilder<Tool> builder)
    {
        builder.ToTable("Tools", t =>
        {
            t.HasCheckConstraint("CK_Tools_Kind", "([Kind] = 1 AND [Url] IS NOT NULL AND [BuiltInKey] IS NULL) OR ([Kind] = 2 AND [BuiltInKey] IS NOT NULL AND [Url] IS NULL)");
            t.HasCheckConstraint("CK_Tools_Category", "[Category] BETWEEN 1 AND 6");
            t.HasCheckConstraint("CK_Tools_Url", "[Url] IS NULL OR [Url] LIKE 'https://%'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(Tool.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.NameMk).HasMaxLength(Tool.NameMaxLength).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(Tool.NameMaxLength).IsRequired();
        builder.Property(x => x.DescriptionMk).HasMaxLength(Tool.DescriptionMaxLength).IsRequired();
        builder.Property(x => x.DescriptionEn).HasMaxLength(Tool.DescriptionMaxLength).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(Tool.UrlMaxLength).IsUnicode(false);
        builder.Property(x => x.BuiltInKey).HasMaxLength(Tool.CodeMaxLength).IsUnicode(false);
        builder.HasIndex(x => x.BuiltInKey).IsUnique().HasFilter("[BuiltInKey] IS NOT NULL");
        builder.Property(x => x.LicenseNoteMk).HasMaxLength(Tool.LicenseNoteMaxLength);
        builder.Property(x => x.LicenseNoteEn).HasMaxLength(Tool.LicenseNoteMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasData(ToolboxSeed.Tools());
    }
}

internal sealed class ToolSubjectConfiguration : IEntityTypeConfiguration<ToolSubject>
{
    public void Configure(EntityTypeBuilder<ToolSubject> builder)
    {
        builder.ToTable("ToolSubjects");
        builder.HasKey(x => new { x.ToolId, x.SubjectId });
        builder.HasIndex(x => x.SubjectId);
        builder.HasOne<Tool>().WithMany().HasForeignKey(x => x.ToolId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(ToolSubject.SubjectId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasData(ToolboxSeed.Subjects());
    }
}

/// <summary>
/// The first catalog (R3-P1): well-known free tools opened by their official links, and the two boards built into
/// ClassHub (the drawing board since R3-P3, the geometry board since R3-P4).
/// </summary>
internal static class ToolboxSeed
{
    private static readonly DateTime Seeded = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private static readonly (string Code, ToolKind Kind, ToolCategory Category, string? Url, string? Key, bool Active, bool Featured,
        string NameMk, string NameEn, string DescriptionMk, string DescriptionEn, string LicenseMk, string LicenseEn, string[] Subjects)[] Rows =
    [
        ("geogebra", ToolKind.External, ToolCategory.Geometry, "https://www.geogebra.org/classic", null, true, true,
            "GeoGebra", "GeoGebra",
            "Геометрија, алгебра, графици и табели во едно; конструкции со точки, прави, кругови и функции.",
            "Geometry, algebra, graphs and spreadsheets in one; constructions with points, lines, circles and functions.",
            "Бесплатно за некомерцијална употреба според лиценцата на GeoGebra; се отвора на geogebra.org.", "Free for non-commercial use under the GeoGebra license; opened on geogebra.org.", ["math", "physics"]),
        ("desmos", ToolKind.External, ToolCategory.Graphing, "https://www.desmos.com/calculator", null, true, true,
            "Desmos калкулатор за графици", "Desmos graphing calculator",
            "Цртање графици на функции, неравенки и табели, со лизгачи за параметри.",
            "Plot functions, inequalities and tables, with sliders for parameters.",
            "Бесплатно на desmos.com според условите за користење на Desmos.", "Free to use on desmos.com under the Desmos terms of service.", ["math"]),
        ("phet", ToolKind.External, ToolCategory.Simulations, "https://phet.colorado.edu/", null, true, true,
            "PhET интерактивни симулации", "PhET interactive simulations",
            "Интерактивни симулации по физика, хемија, математика и биологија.",
            "Interactive simulations for physics, chemistry, mathematics and biology.",
            "Бесплатни симулации (CC BY 4.0) од Универзитетот во Колорадо Болдер; се отвораат на phet.colorado.edu.", "Free simulations (CC BY 4.0) by the University of Colorado Boulder; opened on phet.colorado.edu.", ["physics", "chemistry", "math", "biology"]),
        ("molview", ToolKind.External, ToolCategory.Chemistry, "https://molview.org/", null, true, false,
            "MolView", "MolView",
            "Цртање и 3D приказ на молекули.",
            "Draw molecules and view them in 3D.",
            "Бесплатна веб-апликација; се отвора на molview.org.", "Free web application; opened on molview.org.", ["chemistry", "biology"]),
        ("drawing-board", ToolKind.BuiltIn, ToolCategory.Drawing, null, "drawing-board", true, true,
            "Табла за цртање", "Drawing board",
            "Слободно цртање, текст, форми, стрелки и дијаграми; зачувување и повторно отворање.",
            "Free drawing, text, shapes, arrows and diagrams; save and reopen.",
            "Вградена во ClassHub.", "Built into ClassHub.", []),
        ("geometry-board", ToolKind.BuiltIn, ToolCategory.Geometry, null, "geometry-board", true, true,
            "Геометриска табла", "Geometry board",
            "Точки, прави, агли, триаголници, кругови, координатен систем и графици; зачувување и повторно отворање.",
            "Points, lines, angles, triangles, circles, a coordinate system and graphs; save and reopen.",
            "Вградена во ClassHub.", "Built into ClassHub.", ["math"]),
    ];

    public static Guid IdFor(string code)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"classhub-toolbox:{code}"));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    public static IEnumerable<Tool> Tools() =>
        Rows.Select((r, i) => new Tool
        {
            Id = IdFor(r.Code),
            Code = r.Code,
            Kind = r.Kind,
            Category = r.Category,
            Url = r.Url,
            BuiltInKey = r.Key,
            IsActive = r.Active,
            IsFeatured = r.Featured,
            NameMk = r.NameMk,
            NameEn = r.NameEn,
            DescriptionMk = r.DescriptionMk,
            DescriptionEn = r.DescriptionEn,
            LicenseNoteMk = r.LicenseMk,
            LicenseNoteEn = r.LicenseEn,
            SortOrder = (i + 1) * 10,
            CreatedAtUtc = Seeded,
            UpdatedAtUtc = Seeded,
            Version = 1,
        });

    public static IEnumerable<ToolSubject> Subjects() =>
        Rows.SelectMany(r => r.Subjects.Select(code => new ToolSubject { ToolId = IdFor(r.Code), SubjectId = CatalogForeignKeyTargets.SeededSubjectId(code) }));
}
