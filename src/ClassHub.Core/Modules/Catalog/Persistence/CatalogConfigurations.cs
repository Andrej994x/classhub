using ClassHub.Core.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Catalog.Persistence;

internal static class CatalogLimits
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;
}

internal sealed class SubjectCategoryConfiguration : IEntityTypeConfiguration<SubjectCategory>
{
    public void Configure(EntityTypeBuilder<SubjectCategory> builder)
    {
        builder.ToTable("SubjectCategories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(CatalogLimits.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.NameMk).HasMaxLength(CatalogLimits.NameMaxLength).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(CatalogLimits.NameMaxLength).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasData(CatalogSeed.CategoryRows());
    }
}

internal sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("Subjects");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(CatalogLimits.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.NameMk).HasMaxLength(CatalogLimits.NameMaxLength).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(CatalogLimits.NameMaxLength).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasOne<SubjectCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        builder.HasData(CatalogSeed.SubjectRows());
    }
}

internal sealed class EducationLevelConfiguration : IEntityTypeConfiguration<EducationLevel>
{
    public void Configure(EntityTypeBuilder<EducationLevel> builder)
    {
        builder.ToTable("EducationLevels");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(CatalogLimits.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.NameMk).HasMaxLength(CatalogLimits.NameMaxLength).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(CatalogLimits.NameMaxLength).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasData(CatalogSeed.LevelRows());
    }
}
