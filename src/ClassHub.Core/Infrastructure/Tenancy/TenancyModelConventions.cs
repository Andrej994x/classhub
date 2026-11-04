using System.Linq.Expressions;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Infrastructure.Tenancy;

public static class TenancyModelConventions
{
    /// <summary>
    /// Adds the named <see cref="TenancyFilters.Tenant"/> filter to every <see cref="ITenantOwned"/> entity
    /// (<c>e.WorkspaceId == context.CurrentTenantId</c>, so no tenant ⇒ no rows) and the
    /// <see cref="TenancyFilters.Archived"/> filter to every <see cref="IArchivable"/> entity.
    /// </summary>
    public static void Apply(ModelBuilder modelBuilder, ITenantAwareDbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        // Owned types (for example Identity's passkey data) must not be re-registered as entities.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null && !t.IsOwned()).ToList())
        {
            var clrType = entityType.ClrType;
            var builder = modelBuilder.Entity(clrType);

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                var e = Expression.Parameter(clrType, "e");
                var body = Expression.Equal(
                    Expression.Property(e, nameof(ITenantOwned.WorkspaceId)),
                    Expression.Property(Expression.Constant(context), nameof(ITenantAwareDbContext.CurrentTenantId)));
                builder.HasQueryFilter(TenancyFilters.Tenant, Expression.Lambda(body, e));
                builder.HasIndex(nameof(ITenantOwned.WorkspaceId));
            }

            if (typeof(IArchivable).IsAssignableFrom(clrType))
            {
                var e = Expression.Parameter(clrType, "e");
                var body = Expression.Equal(
                    Expression.Property(e, nameof(IArchivable.ArchivedAtUtc)),
                    Expression.Constant(null, typeof(DateTime?)));
                builder.HasQueryFilter(TenancyFilters.Archived, Expression.Lambda(body, e));
            }
        }
    }

    /// <summary>
    /// Composite tenant foreign key: <c>(WorkspaceId, foreignKey)</c> → parent <c>(WorkspaceId, Id)</c>.
    /// The database then rejects any reference to another workspace's row, even if application code is wrong.
    /// </summary>
    public static ReferenceCollectionBuilder<TParent, TChild> HasTenantReference<TChild, TParent>(
        this EntityTypeBuilder<TChild> child, string foreignKeyProperty)
        where TChild : class, ITenantOwned
        where TParent : class, ITenantOwned
    {
        ArgumentNullException.ThrowIfNull(child);
        return child.HasOne<TParent>()
            .WithMany()
            .HasForeignKey(nameof(ITenantOwned.WorkspaceId), foreignKeyProperty)
            .HasPrincipalKey(nameof(ITenantOwned.WorkspaceId), "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
