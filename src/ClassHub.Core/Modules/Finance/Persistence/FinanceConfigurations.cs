using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Finance.Domain;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Finance.Persistence;

internal static class FinanceKeys
{
    /// <summary>The student belongs to the row's workspace (composite tenant FK).</summary>
    public static void HasStudent<T>(this EntityTypeBuilder<T> builder, string studentProperty)
        where T : class =>
        builder.HasOne(StudentForeignKeyTargets.Student, navigationName: null).WithMany()
            .HasForeignKey("WorkspaceId", studentProperty)
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
}

internal sealed class StudentPackageConfiguration : IEntityTypeConfiguration<StudentPackage>
{
    public void Configure(EntityTypeBuilder<StudentPackage> builder)
    {
        builder.ToTable("StudentPackages", t =>
        {
            t.HasCheckConstraint("CK_StudentPackages_Lessons", $"[LessonCount] BETWEEN 1 AND {StudentPackage.MaxLessons}");
            t.HasCheckConstraint("CK_StudentPackages_Price", $"[PriceMinor] BETWEEN 0 AND {StudentPackage.MaxPriceMinor}");
            t.HasCheckConstraint("CK_StudentPackages_Title", "LEN([Title]) >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Title).HasMaxLength(StudentPackage.TitleMaxLength).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId, x.CreatedAtUtc });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(StudentPackage.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasStudent(nameof(StudentPackage.StudentId));
        builder.HasOne(OfferForeignKeyTargets.Offer, navigationName: null).WithMany()
            .HasForeignKey(nameof(StudentPackage.WorkspaceId), nameof(StudentPackage.OfferId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LessonLedgerEntryConfiguration : IEntityTypeConfiguration<LessonLedgerEntry>
{
    public void Configure(EntityTypeBuilder<LessonLedgerEntry> builder)
    {
        builder.ToTable("LessonLedger", t =>
        {
            t.HasCheckConstraint("CK_LessonLedger_Kind", "[Kind] BETWEEN 1 AND 3");
            // A lesson entry uses one lesson of a lesson (numbered per pair); a reversal names its entry; a correction has a
            // reason and a package; nothing is zero.
            t.HasCheckConstraint("CK_LessonLedger_Shape",
                "([Kind] = 1 AND [Quantity] = 1 AND [LessonId] IS NOT NULL AND [Occurrence] IS NOT NULL AND [ReversesId] IS NULL) OR " +
                "([Kind] = 2 AND [Quantity] <> 0 AND [PackageId] IS NOT NULL AND [Reason] IS NOT NULL AND [Occurrence] IS NULL AND [ReversesId] IS NULL) OR " +
                "([Kind] = 3 AND [Quantity] <> 0 AND [ReversesId] IS NOT NULL AND [Occurrence] IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Reason).HasMaxLength(LessonLedgerEntry.ReasonMaxLength);
        // One consumption per lesson, student and occurrence: parallel processing of the same lesson consumes once.
        builder.HasIndex(x => new { x.LessonId, x.StudentId, x.Occurrence }).IsUnique().HasFilter("[Kind] = 1");
        // An entry is taken back at most once.
        builder.HasIndex(x => x.ReversesId).IsUnique().HasFilter("[ReversesId] IS NOT NULL");
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.WorkspaceId, x.PackageId });
        builder.HasStudent(nameof(LessonLedgerEntry.StudentId));
        builder.HasTenantReference<LessonLedgerEntry, StudentPackage>(nameof(LessonLedgerEntry.PackageId));
        builder.HasOne(LessonForeignKeyTargets.Lesson, navigationName: null).WithMany()
            .HasForeignKey(nameof(LessonLedgerEntry.WorkspaceId), nameof(LessonLedgerEntry.LessonId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<LessonLedgerEntry>().WithMany()
            .HasForeignKey(nameof(LessonLedgerEntry.WorkspaceId), nameof(LessonLedgerEntry.ReversesId))
            .HasPrincipalKey(nameof(LessonLedgerEntry.WorkspaceId), nameof(LessonLedgerEntry.Id))
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", t =>
        {
            t.HasCheckConstraint("CK_Payments_Method", "[Method] BETWEEN 1 AND 4");
            // No negative amounts except an explicit reversal of a payment.
            t.HasCheckConstraint("CK_Payments_Amount", "([AmountMinor] > 0 AND [ReversesPaymentId] IS NULL) OR ([AmountMinor] < 0 AND [ReversesPaymentId] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(Payment.NoteMaxLength);
        builder.HasIndex(x => x.ReversesPaymentId).IsUnique().HasFilter("[ReversesPaymentId] IS NOT NULL");
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId, x.PaidOn });
        builder.HasIndex(x => new { x.WorkspaceId, x.PaidOn });
        builder.HasStudent(nameof(Payment.StudentId));
        builder.HasTenantReference<Payment, StudentPackage>(nameof(Payment.PackageId));
        builder.HasOne<Payment>().WithMany()
            .HasForeignKey(nameof(Payment.WorkspaceId), nameof(Payment.ReversesPaymentId))
            .HasPrincipalKey(nameof(Payment.WorkspaceId), nameof(Payment.Id))
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BalanceAdjustmentConfiguration : IEntityTypeConfiguration<BalanceAdjustment>
{
    public void Configure(EntityTypeBuilder<BalanceAdjustment> builder)
    {
        builder.ToTable("BalanceAdjustments", t =>
        {
            t.HasCheckConstraint("CK_BalanceAdjustments_Amount", "[AmountMinor] <> 0");
            t.HasCheckConstraint("CK_BalanceAdjustments_Reason", "LEN([Reason]) >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(BalanceAdjustment.ReasonMaxLength).IsRequired();
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId, x.CreatedAtUtc });
        builder.HasStudent(nameof(BalanceAdjustment.StudentId));
    }
}
