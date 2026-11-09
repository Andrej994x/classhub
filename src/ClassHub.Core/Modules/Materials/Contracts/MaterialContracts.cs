namespace ClassHub.Core.Modules.Materials.Contracts;

/// <summary>Who a material is assigned to: a student or a group (one of the two), with a display name.</summary>
public sealed record MaterialAssignmentResponse(Guid Id, Guid? StudentId, Guid? GroupId, string Name, DateTime AssignedAtUtc);

public sealed record MaterialResponse(
    Guid Id,
    string Title,
    string? Description,
    Guid? SubjectId,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool Archived,
    IReadOnlyList<MaterialAssignmentResponse> Assignments,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Version);

public sealed record MaterialListItem(Guid Id, string Title, Guid? SubjectId, string FileName, string ContentType, long SizeBytes, int AssignmentCount, bool Archived, DateTime UpdatedAtUtc);

public sealed record MaterialListResponse(IReadOnlyList<MaterialListItem> Items, int Total, int Page, int PageSize, long StorageUsedBytes);

/// <summary>Change a material's details (the file stays); the version is the loaded one.</summary>
public sealed record UpdateMaterialRequest(string? Title, string? Description, Guid? SubjectId, int? Version);

public sealed record MaterialVersionRequest(int? Version);

/// <summary>Assign to a student or a group (exactly one).</summary>
public sealed record AssignMaterialRequest(Guid? StudentId, Guid? GroupId);

/// <summary>A material in the student portal (assigned to the student or to one of their groups).</summary>
public sealed record StudentMaterialResponse(Guid Id, string Title, string? Description, Guid? SubjectId, string FileName, string ContentType, long SizeBytes, DateTime AssignedAtUtc);
