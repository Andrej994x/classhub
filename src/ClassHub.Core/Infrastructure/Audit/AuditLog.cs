using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using ClassHub.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Infrastructure.Audit;

/// <summary>
/// One critical action (who, when, what changed). Append-only: no application code updates or deletes
/// entries. Details hold only the changed fields, never secrets or student content.
/// </summary>
public sealed class AuditLogEntry
{
    public const int ActionMaxLength = 100;
    public const int TargetMaxLength = 100;

    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>The acting user, or null for system/CLI actions.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>For example <c>workspace.settings.updated</c>.</summary>
    public required string Action { get; set; }

    public required string TargetType { get; set; }
    public required string TargetId { get; set; }

    /// <summary>The workspace concerned, when there is one.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>JSON object with <c>before</c>/<c>after</c> values of changed fields.</summary>
    public string? DetailsJson { get; set; }

    public string? RequestId { get; set; }
}

public interface IAuditLog
{
    /// <summary>Adds an entry to the current unit of work (saved with the caller's <c>SaveChangesAsync</c>).</summary>
    void Record(string action, string targetType, string targetId, Guid? workspaceId = null, object? before = null, object? after = null, Guid? actorUserId = null);
}

internal sealed class AuditLog(ClassHubDbContext db, IHttpContextAccessor http, TimeProvider clock) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string targetType, string targetId, Guid? workspaceId = null, object? before = null, object? after = null, Guid? actorUserId = null)
    {
        var user = http.HttpContext?.User;
        var actor = actorUserId ?? (Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null);

        db.Set<AuditLogEntry>().Add(new AuditLogEntry
        {
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
            ActorUserId = actor,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            WorkspaceId = workspaceId,
            DetailsJson = before is null && after is null ? null : JsonSerializer.Serialize(new { before, after }, Json),
            RequestId = Activity.Current?.TraceId.ToString() ?? http.HttpContext?.TraceIdentifier,
        });
    }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLog", t => t.HasCheckConstraint("CK_AuditLog_DetailsJson", "[DetailsJson] IS NULL OR ISJSON([DetailsJson]) = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(AuditLogEntry.ActionMaxLength).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(AuditLogEntry.TargetMaxLength).IsRequired();
        builder.Property(x => x.TargetId).HasMaxLength(AuditLogEntry.TargetMaxLength).IsRequired();
        builder.Property(x => x.RequestId).HasMaxLength(100);
        builder.HasIndex(x => new { x.WorkspaceId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.ActorUserId, x.OccurredAtUtc });
        builder.HasIndex(x => x.OccurredAtUtc);
    }
}
