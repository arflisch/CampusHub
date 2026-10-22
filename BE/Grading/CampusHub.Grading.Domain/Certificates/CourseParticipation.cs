using FluentResults;

namespace Dev4u.CampusHub.Grading.Domain.Certificates;

/// <summary>A certificate of completion.</summary>
/// <param name="Number">Human-readable number, derived from the enrollment: issuing twice yields the same number.</param>
/// <param name="IssuedAt">When it was issued.</param>
public sealed record Certificate(string Number, DateTimeOffset IssuedAt);

/// <summary>
/// A student's place in a course, as Grading sees it: recorded when the enrollment is confirmed, it earns a
/// certificate once the course has ended.
/// <para>
/// The id is the enrollment id, so a redelivered EnrollmentConfirmed event maps to the same participation (inbox).
/// Announcing the certificate to the other services is tracked on the aggregate itself
/// (<see cref="CertificateAnnouncedAt"/>): a certificate issued but not announced yet is announced again by the next
/// run, with the same event id, until it succeeds.
/// </para>
/// </summary>
public sealed class CourseParticipation
{
    private CourseParticipation(Guid enrollmentId, Guid courseId, string courseTitle, string studentId, DateTimeOffset courseEndsAt,
                                Certificate? certificate, DateTimeOffset? certificateAnnouncedAt, long version)
    {
        EnrollmentId = enrollmentId;
        CourseId = courseId;
        CourseTitle = courseTitle;
        StudentId = studentId;
        CourseEndsAt = courseEndsAt;
        Certificate = certificate;
        CertificateAnnouncedAt = certificateAnnouncedAt;
        Version = version;
    }

    public Guid EnrollmentId { get; }

    public Guid CourseId { get; }

    public string CourseTitle { get; }

    /// <summary>Identity provider subject of the student.</summary>
    public string StudentId { get; }

    public DateTimeOffset CourseEndsAt { get; }

    public Certificate? Certificate { get; private set; }

    public DateTimeOffset? CertificateAnnouncedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented by the repository on every successful save.</summary>
    public long Version { get; private set; }

    public bool NeedsAnnouncement => Certificate is not null && CertificateAnnouncedAt is null;

    public static Result<CourseParticipation> Record(Guid enrollmentId, Guid courseId, string? courseTitle, string? studentId,
                                                     DateTimeOffset? courseEndsAt)
    {
        if (enrollmentId == Guid.Empty || courseId == Guid.Empty)
        {
            return Result.Fail<CourseParticipation>(GradingErrors.InvalidParticipation("The enrollment and the course are required."));
        }

        if (string.IsNullOrWhiteSpace(courseTitle) || string.IsNullOrWhiteSpace(studentId))
        {
            return Result.Fail<CourseParticipation>(GradingErrors.InvalidParticipation("The course title and the student are required."));
        }

        return courseEndsAt is { } endsAt
            ? Result.Ok(new CourseParticipation(enrollmentId, courseId, courseTitle, studentId, endsAt, certificate: null,
                                                certificateAnnouncedAt: null, version: 0))
            : Result.Fail<CourseParticipation>(GradingErrors.InvalidParticipation("The end of the course is required."));
    }

    /// <summary>Rebuilds a participation from storage. Persistence only: no rule is checked.</summary>
    public static CourseParticipation Rehydrate(Guid enrollmentId, Guid courseId, string courseTitle, string studentId,
                                                DateTimeOffset courseEndsAt, Certificate? certificate,
                                                DateTimeOffset? certificateAnnouncedAt, long version) =>
        new(enrollmentId, courseId, courseTitle, studentId, courseEndsAt, certificate, certificateAnnouncedAt, version);

    public static string NumberFor(Guid enrollmentId, DateTimeOffset courseEndsAt) =>
        $"CH-{courseEndsAt:yyyy}-{enrollmentId.ToString("N")[..10].ToUpperInvariant()}";

    /// <summary>Issues the certificate once the course has ended. Issuing twice keeps the first certificate.</summary>
    public Result IssueCertificate(DateTimeOffset now)
    {
        if (Certificate is not null)
        {
            return Result.Ok();
        }

        if (now < CourseEndsAt)
        {
            return Result.Fail(GradingErrors.CourseNotEnded(EnrollmentId));
        }

        Certificate = new Certificate(NumberFor(EnrollmentId, CourseEndsAt), now);
        return Result.Ok();
    }

    /// <summary>Records that the other services were told about the certificate.</summary>
    public void MarkCertificateAnnounced(DateTimeOffset now)
    {
        if (NeedsAnnouncement)
        {
            CertificateAnnouncedAt = now;
        }
    }

    /// <summary>Called by the repository once the new version is stored. Persistence only.</summary>
    public void MarkPersisted() => Version++;
}
