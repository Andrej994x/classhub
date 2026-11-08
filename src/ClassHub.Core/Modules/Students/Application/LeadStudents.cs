using ClassHub.Core.Modules.Students.Contracts;

namespace ClassHub.Core.Modules.Students.Application;

/// <summary>The student of a request, converting it when needed (the same idempotent D-12 conversion as the API).</summary>
internal sealed class LeadStudents(StudentService students) : ILeadStudents
{
    public async Task<Guid> EnsureStudentAsync(Guid leadId, CancellationToken cancellationToken) =>
        (await students.ConvertAsync(new ConvertLeadRequest(leadId), cancellationToken)).Student.Id;
}
