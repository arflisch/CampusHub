using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Errors;
using Dev4u.CampusHub.Grading.Domain.Certificates;
using Dev4u.CampusHub.Grading.UseCases.Common;
using FluentResults;

namespace Dev4u.CampusHub.Grading.UseCases.Certificates;

public sealed record RecordParticipationCommand(Guid EnrollmentId, Guid CourseId, string? CourseTitle, string? StudentId, DateTimeOffset? CourseEndsAt);

/// <summary>
/// Reacts to EnrollmentConfirmed: the student now takes part in the course. Idempotent (the participation id is the
/// enrollment id), so the at-least-once delivery of Dapr never creates duplicates. Returns false for a duplicate.
/// </summary>
[Export, Scoped]
public sealed class RecordParticipation(IParticipationRepository participations)
{
    public async Task<Result<bool>> ExecuteAsync(RecordParticipationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Event payloads are input like any other: an incomplete event (e.g. published before the course dates were
        // added to the contract) fails validation and is dropped, retrying would not fix it.
        var participation = CourseParticipation.Record(command.EnrollmentId, command.CourseId, command.CourseTitle,
                                                       command.StudentId, command.CourseEndsAt);
        if (participation.IsFailed)
        {
            return participation.ToResult<bool>();
        }

        return Result.Ok(await participations.AddIfAbsentAsync(participation.Value, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>What a run of <see cref="IssueCertificates"/> did, logged by the job.</summary>
public sealed record CertificatesReport(int Issued, int Announced, int Failed);

/// <summary>
/// Issues the certificates of the courses that ended, then announces every issued certificate not announced yet
/// (so a publication that failed last time is retried). Each run handles a bounded batch and never throws for one
/// participation: what fails is counted and picked up again by the next run.
/// </summary>
[Export, Scoped]
public sealed class IssueCertificates(IParticipationRepository participations, ICertificateAnnouncer announcer, TimeProvider timeProvider)
{
    public async Task<CertificatesReport> ExecuteAsync(CancellationToken cancellationToken)
    {
        var (issued, announced, failed) = (0, 0, 0);

        var ended = await participations.ListEndedWithoutCertificateAsync(timeProvider.GetUtcNow(), Constants.MaintenanceBatchSize,
                                                                          cancellationToken).ConfigureAwait(false);
        foreach (var enrollmentId in ended.Select(p => p.EnrollmentId))
        {
            var result = await UpdateAsync(enrollmentId, p => p.IssueCertificate(timeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            (issued, failed) = result.IsSuccess ? (issued + 1, failed) : (issued, failed + 1);
        }

        // Announce after saving: a certificate is never announced unless it is stored.
        var unannounced = await participations.ListUnannouncedAsync(Constants.MaintenanceBatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var participation in unannounced)
        {
            var result = await announcer.AnnounceAsync(participation, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                // If this save fails, the next run announces again with the same event id: subscribers deduplicate.
                result = await UpdateAsync(participation.EnrollmentId, p =>
                {
                    p.MarkCertificateAnnounced(timeProvider.GetUtcNow());
                    return Result.Ok();
                }, cancellationToken).ConfigureAwait(false);
            }

            (announced, failed) = result.IsSuccess ? (announced + 1, failed) : (announced, failed + 1);
        }

        return new CertificatesReport(issued, announced, failed);
    }

    // Load → change → save, again when the save lost an optimistic-concurrency race.
    private async Task<Result> UpdateAsync(Guid enrollmentId, Func<CourseParticipation, Result> change, CancellationToken cancellationToken)
    {
        var result = Result.Fail(GradingErrors.ConcurrencyConflict(enrollmentId));
        for (var attempt = 0; attempt < Constants.ConcurrencyAttempts && result.Errors.HasCode(GradingErrors.ConcurrencyConflictCode); attempt++)
        {
            var participation = await participations.GetAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
            if (participation is null)
            {
                return Result.Fail(GradingErrors.InvalidParticipation($"Participation '{enrollmentId}' no longer exists."));
            }

            result = change(participation);
            if (result.IsSuccess)
            {
                result = await participations.SaveAsync(participation, cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }
}
