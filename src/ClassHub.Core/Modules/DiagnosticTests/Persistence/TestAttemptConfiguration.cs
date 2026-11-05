using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.DiagnosticTests.Persistence;

internal sealed class TestAttemptConfiguration : IEntityTypeConfiguration<TestAttempt>
{
    public void Configure(EntityTypeBuilder<TestAttempt> builder)
    {
        builder.ToTable("TestAttempts", t =>
        {
            t.HasCheckConstraint("CK_TestAttempts_AnswersJson", "ISJSON([AnswersJson]) = 1");
            t.HasCheckConstraint("CK_TestAttempts_Score",
                "[Points] >= 0 AND [Points] <= [MaxPoints] AND [CorrectQuestions] >= 0 AND [CorrectQuestions] <= [TotalQuestions]");
            t.HasCheckConstraint("CK_TestAttempts_Shared", "[ResultSharedAtUtc] IS NULL OR [LeadId] IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        // Random ids (not the sequential default): the visitor's handle for this result must not be guessable.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.AnswersJson).IsRequired();
        builder.HasIndex(x => new { x.WorkspaceId, x.TestId, x.CompletedAtUtc });
        builder.HasIndex(x => x.LeadId).IsUnique().HasFilter("[LeadId] IS NOT NULL");
        builder.HasTenantReference<TestAttempt, DiagnosticTest>(nameof(TestAttempt.TestId));
    }
}
