using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Teachers.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Teachers.Persistence;

internal sealed class TeacherProfileConfiguration : IEntityTypeConfiguration<TeacherProfile>
{
    public void Configure(EntityTypeBuilder<TeacherProfile> builder)
    {
        builder.ToTable("TeacherProfiles", t =>
            t.HasCheckConstraint("CK_TeacherProfiles_CountryCode", "[CountryCode] IS NULL OR [CountryCode] LIKE '[A-Z][A-Z]'"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        // One profile per workspace.
        builder.HasIndex(x => x.WorkspaceId).IsUnique();
        builder.Property(x => x.FirstName).HasMaxLength(TeacherProfile.NameMaxLength);
        builder.Property(x => x.LastName).HasMaxLength(TeacherProfile.NameMaxLength);
        builder.Property(x => x.Bio).HasMaxLength(TeacherProfile.BioMaxLength);
        builder.Property(x => x.TeachingMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.Version).IsConcurrencyToken();
        // The photo must belong to the same workspace (composite tenant FK).
        builder.HasTenantReference<TeacherProfile, StoredFile>(nameof(TeacherProfile.PhotoFileId));
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(TeacherProfile.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TeacherProfileSubjectConfiguration : IEntityTypeConfiguration<TeacherProfileSubject>
{
    public void Configure(EntityTypeBuilder<TeacherProfileSubject> builder)
    {
        builder.ToTable("TeacherProfileSubjects");
        builder.HasKey(x => new { x.ProfileId, x.SubjectId });
        builder.HasTenantReference<TeacherProfileSubject, TeacherProfile>(nameof(TeacherProfileSubject.ProfileId));
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(TeacherProfileSubject.SubjectId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TeacherProfileLevelConfiguration : IEntityTypeConfiguration<TeacherProfileLevel>
{
    public void Configure(EntityTypeBuilder<TeacherProfileLevel> builder)
    {
        builder.ToTable("TeacherProfileLevels");
        builder.HasKey(x => new { x.ProfileId, x.LevelId });
        builder.HasTenantReference<TeacherProfileLevel, TeacherProfile>(nameof(TeacherProfileLevel.ProfileId));
        builder.HasOne(CatalogForeignKeyTargets.EducationLevel, navigationName: null).WithMany().HasForeignKey(nameof(TeacherProfileLevel.LevelId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TeacherProfileLanguageConfiguration : IEntityTypeConfiguration<TeacherProfileLanguage>
{
    public void Configure(EntityTypeBuilder<TeacherProfileLanguage> builder)
    {
        builder.ToTable("TeacherProfileLanguages");
        builder.HasKey(x => new { x.ProfileId, x.LanguageCode });
        builder.Property(x => x.LanguageCode).HasMaxLength(TeacherProfileLanguage.CodeMaxLength).IsUnicode(false);
        builder.HasTenantReference<TeacherProfileLanguage, TeacherProfile>(nameof(TeacherProfileLanguage.ProfileId));
    }
}
