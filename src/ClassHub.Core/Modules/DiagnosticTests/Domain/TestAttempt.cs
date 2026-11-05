using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.DiagnosticTests.Domain;

/// <summary>
/// One visitor's completed attempt at a published test (R1-P7). Anonymous: no name, email, IP or browser data; only the
/// chosen options and the server-side score. The id is random (unguessable) because the visitor uses it to send a
/// contact request for this result. The teacher sees the result only when the visitor chose to share it (D-18).
/// </summary>
public sealed class TestAttempt : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid TestId { get; set; }

    /// <summary>JSON: question id → chosen option ids (validated against the test when submitted).</summary>
    public required string AnswersJson { get; set; }

    public int Points { get; set; }

    public int MaxPoints { get; set; }

    public int CorrectQuestions { get; set; }

    public int TotalQuestions { get; set; }

    public DateTime CompletedAtUtc { get; set; }

    /// <summary>The lead the visitor sent for this result (at most one).</summary>
    public Guid? LeadId { get; set; }

    /// <summary>Set when the visitor agreed to share the result with the teacher (with the contact request).</summary>
    public DateTime? ResultSharedAtUtc { get; set; }
}
