using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence.Technical;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context of the modular monolith: ASP.NET Core Identity stores, Data Protection keys,
/// and every module's entities (configured from this assembly). One migration history for the database.
/// Tenant-owned entities are filtered by the request's workspace (<see cref="ITenantContext"/>) and guarded
/// on save by <see cref="TenantSaveChangesInterceptor"/>. Without a tenant context they are not visible at all.
/// </summary>
public class ClassHubDbContext : IdentityDbContext<AppUser, AppRole, Guid>, IDataProtectionKeyContext, ITenantAwareDbContext
{
    public ClassHubDbContext(DbContextOptions<ClassHubDbContext> options)
        : this(options, null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public ClassHubDbContext(DbContextOptions<ClassHubDbContext> options, ITenantContext? tenantContext)
        : base(options)
    {
        TenantContext = tenantContext;
    }

    public ITenantContext? TenantContext { get; }

    public Guid CurrentTenantId => TenantContext?.WorkspaceId ?? Guid.Empty;

    public DbSet<SchemaInfo> SchemaInfo => Set<SchemaInfo>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();

    public DbSet<DevMailboxMessage> DevMailbox => Set<DevMailboxMessage>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.AddInterceptors(TenantSaveChangesInterceptor.Instance);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ClassHubModelConventions.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ClassHubDbContext).Assembly);
        TenancyModelConventions.Apply(builder, this);
        ClassHubModelConventions.ApplyModelConventions(builder);
    }
}
