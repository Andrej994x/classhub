using ClassHub.Core.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Identity.Persistence;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.Locale).HasMaxLength(AppUser.LocaleMaxLength).IsRequired();
        builder.Property(u => u.TimeZone).HasMaxLength(AppUser.TimeZoneMaxLength).IsRequired();
        builder.Property(u => u.CreatedAtUtc).IsRequired();

        // M01: an email belongs to exactly one account. Identity's default EmailIndex is not unique, so
        // parallel registrations could otherwise create duplicates; the database enforces it.
        builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
    }
}
