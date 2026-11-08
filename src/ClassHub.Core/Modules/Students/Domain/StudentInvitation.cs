using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Students.Domain;

/// <summary>
/// An invitation for a student to get their own account (R2-P3). Only the SHA-256 hash of the link token is stored;
/// the token is single use, expires after <see cref="ValidFor"/>, and a new invitation revokes the pending one.
/// </summary>
public sealed class StudentInvitation : ITenantOwned
{
    public const int TokenBytes = 32;
    public static readonly TimeSpan ValidFor = TimeSpan.FromDays(7);

    /// <summary>At most this many invitations per student within <see cref="SendWindow"/> (mailbox abuse).</summary>
    public const int MaxPerWindow = 5;
    public static readonly TimeSpan SendWindow = TimeSpan.FromDays(1);

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Where the link was sent (lowercase); a new account gets this address.</summary>
    public required string Email { get; set; }

    public required byte[] TokenHash { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }

    public Guid? AcceptedByUserId { get; set; }

    /// <summary>For a minor: when the person accepting confirmed the parent or guardian agreed (D-18, provisional).</summary>
    public DateTime? GuardianConsentAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public bool IsPending(DateTime nowUtc) => AcceptedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}
