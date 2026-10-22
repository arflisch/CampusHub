using FluentResults;

namespace Dev4u.CampusHub.Grading.Domain.Certificates;

/// <summary>Port implemented by the MongoDB adapter. Saving is conditional on <see cref="CourseParticipation.Version"/>.</summary>
public interface IParticipationRepository
{
    /// <summary>Stores a new participation; false when it already exists (redelivered event).</summary>
    Task<bool> AddIfAbsentAsync(CourseParticipation participation, CancellationToken cancellationToken);

    Task<CourseParticipation?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken);

    Task<Result> SaveAsync(CourseParticipation participation, CancellationToken cancellationToken);

    /// <summary>Participations whose course ended before <paramref name="now"/> and that have no certificate yet.</summary>
    Task<IReadOnlyList<CourseParticipation>> ListEndedWithoutCertificateAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    /// <summary>Participations whose certificate was issued but not announced yet.</summary>
    Task<IReadOnlyList<CourseParticipation>> ListUnannouncedAsync(int take, CancellationToken cancellationToken);
}

/// <summary>
/// Port telling the other services that a certificate was issued (CertificateIssued integration event). Announcing
/// the same certificate twice publishes the same event id, so subscribers ignore the duplicate.
/// </summary>
public interface ICertificateAnnouncer
{
    Task<Result> AnnounceAsync(CourseParticipation participation, CancellationToken cancellationToken);
}
