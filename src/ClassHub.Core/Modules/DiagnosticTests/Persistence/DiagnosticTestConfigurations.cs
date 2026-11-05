using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.DiagnosticTests.Persistence;

internal sealed class DiagnosticTestConfiguration : IEntityTypeConfiguration<DiagnosticTest>
{
    public void Configure(EntityTypeBuilder<DiagnosticTest> builder)
    {
        builder.ToTable("DiagnosticTests", t =>
        {
            t.HasCheckConstraint("CK_DiagnosticTests_Status", "[Status] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_DiagnosticTests_Difficulty", "[Difficulty] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_DiagnosticTests_Slug",
                $"LEN([Slug]) BETWEEN {DiagnosticTest.SlugMinLength} AND {DiagnosticTest.SlugMaxLength} AND [Slug] NOT LIKE '%[^a-z0-9-]%'");
            t.HasCheckConstraint("CK_DiagnosticTests_AttemptCount", "[AttemptCount] >= 0");
            t.HasCheckConstraint("CK_DiagnosticTests_Hidden", "([HiddenAtUtc] IS NULL AND [HiddenReason] IS NULL) OR ([HiddenAtUtc] IS NOT NULL AND [HiddenReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_DiagnosticTests_Published", "[Status] = 1 OR [PublishedAtUtc] IS NOT NULL");
        });
        builder.Property(x => x.HiddenReason).HasMaxLength(500);
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Title).HasMaxLength(DiagnosticTest.TitleMaxLength).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(DiagnosticTest.SlugMaxLength).IsUnicode(false).UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(DiagnosticTest.DescriptionMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        // Unique within the workspace (archived tests keep their address, so it is never reused for something else).
        builder.HasIndex(x => new { x.WorkspaceId, x.Slug }).IsUnique();
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(DiagnosticTest.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(DiagnosticTest.SubjectId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TestQuestionConfiguration : IEntityTypeConfiguration<TestQuestion>
{
    public void Configure(EntityTypeBuilder<TestQuestion> builder)
    {
        builder.ToTable("TestQuestions", t =>
        {
            t.HasCheckConstraint("CK_TestQuestions_Points", $"[Points] BETWEEN 0 AND {TestQuestion.MaxPoints}");
            t.HasCheckConstraint("CK_TestQuestions_Text", "LEN([Text]) > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Text).HasMaxLength(TestQuestion.TextMaxLength).IsRequired();
        builder.HasIndex(x => new { x.TestId, x.Position });
        builder.HasTenantReference<TestQuestion, DiagnosticTest>(nameof(TestQuestion.TestId));
    }
}

internal sealed class TestOptionConfiguration : IEntityTypeConfiguration<TestOption>
{
    public void Configure(EntityTypeBuilder<TestOption> builder)
    {
        builder.ToTable("TestOptions", t => t.HasCheckConstraint("CK_TestOptions_Text", "LEN([Text]) > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Text).HasMaxLength(TestOption.TextMaxLength).IsRequired();
        builder.HasIndex(x => new { x.QuestionId, x.Position });
        builder.HasTenantReference<TestOption, TestQuestion>(nameof(TestOption.QuestionId));
    }
}
