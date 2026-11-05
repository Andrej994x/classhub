using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Subscriptions.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Subscriptions.Persistence;

/// <summary>Seeded plans with the provisional D-29 limits (final values block Beta; the owner can change them).</summary>
internal static class PlanSeed
{
    public static readonly Guid FreeId = new("8a5c1e2f-3b4d-4e6f-9a0b-000000000001");
    public static readonly Guid ProId = new("8a5c1e2f-3b4d-4e6f-9a0b-000000000002");
    public static readonly DateTime SeededAtUtc = new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyDictionary<string, int> FreeLimits { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [PlanLimits.ActiveStudents] = 10,
        [PlanLimits.PublishedTests] = 3,
        [PlanLimits.Activities] = 10,
        [PlanLimits.StorageMb] = 500,
        [PlanLimits.AnalyticsLevel] = 1,
    };

    public static IReadOnlyDictionary<string, int> ProLimits { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [PlanLimits.ActiveStudents] = 150,
        [PlanLimits.PublishedTests] = 50,
        [PlanLimits.Activities] = 500,
        [PlanLimits.StorageMb] = 10_240,
        [PlanLimits.AnalyticsLevel] = 2,
    };
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(Plan.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.NameMk).HasMaxLength(Plan.NameMaxLength).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(Plan.NameMaxLength).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.Code).IsUnique();
        // Exactly one default plan.
        builder.HasIndex(x => x.IsDefault).IsUnique().HasFilter("[IsDefault] = 1");
        builder.HasData(
            new Plan { Id = PlanSeed.FreeId, Code = PlanCodes.Free, NameMk = "Бесплатен", NameEn = "Free", IsDefault = true, SortOrder = 1, UpdatedAtUtc = PlanSeed.SeededAtUtc },
            new Plan { Id = PlanSeed.ProId, Code = PlanCodes.Pro, NameMk = "Про", NameEn = "Pro", IsDefault = false, SortOrder = 2, UpdatedAtUtc = PlanSeed.SeededAtUtc });
    }
}

internal sealed class PlanLimitConfiguration : IEntityTypeConfiguration<PlanLimit>
{
    public void Configure(EntityTypeBuilder<PlanLimit> builder)
    {
        builder.ToTable("PlanLimits", t => t.HasCheckConstraint("CK_PlanLimits_Value", $"[Value] BETWEEN 0 AND {PlanLimit.MaxValue}"));
        builder.HasKey(x => new { x.PlanId, x.Key });
        builder.Property(x => x.Key).HasMaxLength(PlanLimit.KeyMaxLength).IsUnicode(false);
        builder.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.NoAction);
        builder.HasData([
            .. PlanSeed.FreeLimits.Select(l => new PlanLimit { PlanId = PlanSeed.FreeId, Key = l.Key, Value = l.Value }),
            .. PlanSeed.ProLimits.Select(l => new PlanLimit { PlanId = PlanSeed.ProId, Key = l.Key, Value = l.Value }),
        ]);
    }
}

internal sealed class WorkspaceSubscriptionConfiguration : IEntityTypeConfiguration<WorkspaceSubscription>
{
    public void Configure(EntityTypeBuilder<WorkspaceSubscription> builder)
    {
        builder.ToTable("WorkspaceSubscriptions", t =>
            t.HasCheckConstraint("CK_WorkspaceSubscriptions_Period", "[EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Version).IsConcurrencyToken();
        // One row per workspace: a parallel first activation fails instead of creating a second row.
        builder.HasIndex(x => x.WorkspaceId).IsUnique();
        builder.HasIndex(x => new { x.PlanId, x.EndsAtUtc });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(WorkspaceSubscription.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SubscriptionEventConfiguration : IEntityTypeConfiguration<SubscriptionEvent>
{
    public void Configure(EntityTypeBuilder<SubscriptionEvent> builder)
    {
        builder.ToTable("SubscriptionEvents", t =>
        {
            t.HasCheckConstraint("CK_SubscriptionEvents_Kind", "[Kind] IN (1, 2, 3)");
            // Activations and extensions carry the manual payment record (D-30); expiries carry none.
            t.HasCheckConstraint("CK_SubscriptionEvents_Payment",
                "([Kind] IN (1, 2) AND [Months] IS NOT NULL AND [Months] BETWEEN 1 AND 36 AND [AmountMinor] IS NOT NULL AND [Currency] IS NOT NULL AND [PaymentMethod] IS NOT NULL AND [PaidOn] IS NOT NULL)" +
                " OR ([Kind] = 3 AND [AmountMinor] IS NULL AND [Months] IS NULL)");
            t.HasCheckConstraint("CK_SubscriptionEvents_Amount", $"[AmountMinor] IS NULL OR [AmountMinor] BETWEEN 0 AND {SubscriptionEvent.MaxAmountMinor}");
            t.HasCheckConstraint("CK_SubscriptionEvents_Currency", "[Currency] IS NULL OR [Currency] LIKE '[A-Z][A-Z][A-Z]'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(x => x.PaymentMethod).HasMaxLength(SubscriptionEvent.MethodMaxLength).IsUnicode(false);
        builder.Property(x => x.PaymentReference).HasMaxLength(SubscriptionEvent.ReferenceMaxLength);
        builder.Property(x => x.Note).HasMaxLength(SubscriptionEvent.NoteMaxLength);
        builder.HasIndex(x => new { x.WorkspaceId, x.OccurredAtUtc });
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(SubscriptionEvent.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.NoAction);
    }
}
