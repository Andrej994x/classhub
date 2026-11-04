using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Identity.Persistence;

/// <summary>Seeds the platform roles with fixed ids (stable across migrations and environments).</summary>
internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        builder.HasData(
            new AppRole
            {
                Id = PlatformRoles.OwnerRoleId,
                Name = PlatformRoles.Owner,
                NormalizedName = PlatformRoles.Owner.ToUpperInvariant(),
                ConcurrencyStamp = "5b0d1f6e-platform-owner",
            },
            new AppRole
            {
                Id = PlatformRoles.AdminRoleId,
                Name = PlatformRoles.Admin,
                NormalizedName = PlatformRoles.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "5b0d1f6e-platform-admin",
            });
    }
}
