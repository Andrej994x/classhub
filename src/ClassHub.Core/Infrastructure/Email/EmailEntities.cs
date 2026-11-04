using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Infrastructure.Email;

/// <summary>
/// A queued email (transactional outbox). Enqueued in the same unit of work as the change that caused it,
/// delivered by <see cref="EmailOutboxDispatcher"/>. Bodies are cleared after delivery so tokens and
/// personal data do not stay at rest.
/// </summary>
public sealed class EmailOutboxMessage
{
    public const int AddressMaxLength = 320;
    public const int SubjectMaxLength = 400;
    public const int TemplateMaxLength = 100;
    public const int ErrorMaxLength = 1000;

    public Guid Id { get; set; }
    public required string ToAddress { get; set; }
    public required string Subject { get; set; }
    public required string HtmlBody { get; set; }
    public required string TextBody { get; set; }
    public required string Template { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Development/test-only mail sink: delivered messages are stored here instead of being sent.</summary>
public sealed class DevMailboxMessage
{
    public Guid Id { get; set; }
    public required string ToAddress { get; set; }
    public required string Subject { get; set; }
    public required string HtmlBody { get; set; }
    public required string TextBody { get; set; }
    public required string Template { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}

internal sealed class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ToAddress).HasMaxLength(EmailOutboxMessage.AddressMaxLength).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(EmailOutboxMessage.SubjectMaxLength).IsRequired();
        builder.Property(x => x.HtmlBody).IsRequired();
        builder.Property(x => x.TextBody).IsRequired();
        builder.Property(x => x.Template).HasMaxLength(EmailOutboxMessage.TemplateMaxLength).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(EmailOutboxMessage.ErrorMaxLength);
        builder.HasIndex(x => x.NextAttemptAtUtc)
            .HasFilter("[SentAtUtc] IS NULL")
            .HasDatabaseName("IX_EmailOutbox_Pending");
    }
}

internal sealed class DevMailboxMessageConfiguration : IEntityTypeConfiguration<DevMailboxMessage>
{
    public void Configure(EntityTypeBuilder<DevMailboxMessage> builder)
    {
        builder.ToTable("DevMailbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ToAddress).HasMaxLength(EmailOutboxMessage.AddressMaxLength).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(EmailOutboxMessage.SubjectMaxLength).IsRequired();
        builder.Property(x => x.HtmlBody).IsRequired();
        builder.Property(x => x.TextBody).IsRequired();
        builder.Property(x => x.Template).HasMaxLength(EmailOutboxMessage.TemplateMaxLength).IsRequired();
        builder.HasIndex(x => new { x.ToAddress, x.ReceivedAtUtc });
    }
}
