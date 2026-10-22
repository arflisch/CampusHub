using Dev4u.CampusHub.Enrollment.Domain.Enrollments;

namespace Dev4u.CampusHub.Enrollment.UseCases.Enrollments;

/// <summary>Enrollment as shown to the student, who polls it while the saga runs.</summary>
public sealed record EnrollmentDto(
    Guid Id,
    Guid CourseId,
    string? CourseTitle,
    string Status,
    string? RejectionCode,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt);

public sealed record RequestEnrollmentCommand(Guid RequestId, Guid CourseId);

internal static class EnrollmentMappings
{
    public static EnrollmentDto ToDto(this StudentEnrollment enrollment) =>
        new(enrollment.Id, enrollment.CourseId, enrollment.CourseTitle, enrollment.Status.ToString(),
            enrollment.RejectionCode, enrollment.RequestedAt, enrollment.UpdatedAt);
}
