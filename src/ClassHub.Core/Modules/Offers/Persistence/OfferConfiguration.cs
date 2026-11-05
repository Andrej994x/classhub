using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Offers.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Offers.Persistence;

internal sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        // The business rules are enforced by the database as well (ROADMAP R1-P2): even wrong application code
        // cannot store a negative price, a package without lessons or a group without seats.
        builder.ToTable("Offers", t =>
        {
            t.HasCheckConstraint("CK_Offers_PriceMinor", $"[PriceMinor] >= 0 AND [PriceMinor] <= {Offer.MaxPriceMinor}");
            t.HasCheckConstraint("CK_Offers_Duration", $"[DurationMinutes] BETWEEN {Offer.MinDurationMinutes} AND {Offer.MaxDurationMinutes}");
            t.HasCheckConstraint("CK_Offers_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]'");
            t.HasCheckConstraint("CK_Offers_LessonCount",
                $"([Kind] = 'Package' AND [LessonCount] BETWEEN 1 AND {Offer.MaxLessonCount}) OR ([Kind] <> 'Package' AND [LessonCount] IS NULL)");
            t.HasCheckConstraint("CK_Offers_Seats",
                $"([Kind] = 'GroupProgram' AND [Seats] BETWEEN 1 AND {Offer.MaxSeats}) OR ([Kind] <> 'GroupProgram' AND [Seats] IS NULL)");
        });
        builder.HasKey(x => x.Id);
        // Principal key for composite tenant foreign keys (requests, packages, enrollments in later phases).
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(x => x.Title).HasMaxLength(Offer.TitleMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Offer.DescriptionMaxLength);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.DisplayOrder });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Offer.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(Offer.SubjectId)).OnDelete(DeleteBehavior.NoAction);
    }
}
