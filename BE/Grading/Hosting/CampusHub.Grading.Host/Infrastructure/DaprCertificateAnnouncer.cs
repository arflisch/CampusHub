using System.Security.Cryptography;
using System.Text;
using Arc4u.Dependency.Attribute;
using Dapr;
using Dapr.Client;
using Dev4u.CampusHub.Errors;
using Dev4u.CampusHub.Grading.Domain.Certificates;
using Dev4u.CampusHub.Grading.IntegrationEvents;
using FluentResults;
using Grpc.Core;

namespace Dev4u.CampusHub.Grading.Host.Infrastructure;

/// <summary>
/// Publishes CertificateIssued on the Grading pub/sub component (RabbitMQ) through the Dapr sidecar. The event id is
/// derived from the enrollment id, so announcing the same certificate again publishes the same event and
/// subscribers ignore it.
/// </summary>
[Export(typeof(ICertificateAnnouncer)), Scoped]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the Arc4u dependency injection ([Export]).")]
internal sealed class DaprCertificateAnnouncer(DaprClient dapr, ILogger<DaprCertificateAnnouncer> logger) : ICertificateAnnouncer
{
    /// <summary>Name of the Dapr pub/sub component scoped to this service (Dapr/*/CampusHub-Grading-pubsub-PubSub.yaml).</summary>
    public const string PubSubName = "campushub-grading-pubsub-pubsub";

    public async Task<Result> AnnounceAsync(CourseParticipation participation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participation);
        if (participation.Certificate is not { } certificate)
        {
            return Result.Ok(); // nothing to announce
        }

        var integrationEvent = new CertificateIssuedV1(
            EventId: DeterministicId(participation.EnrollmentId, GradingEvents.CertificateIssuedV1Type),
            OccurredAt: certificate.IssuedAt,
            CertificateNumber: certificate.Number,
            EnrollmentId: participation.EnrollmentId,
            CourseId: participation.CourseId,
            CourseTitle: participation.CourseTitle,
            StudentId: participation.StudentId);
        try
        {
            await dapr.PublishEventAsync(PubSubName, GradingEvents.Topic, integrationEvent, new Dictionary<string, string>
            {
                // Stable id: subscribers deduplicate on it. Type: matched by the subscriptions' routing rules.
                ["cloudevent.id"] = integrationEvent.EventId.ToString(),
                ["cloudevent.type"] = GradingEvents.CertificateIssuedV1Type,
            }, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is DaprException or RpcException or HttpRequestException)
        {
            logger.LogCertificateAnnouncementFailed(ex, participation.EnrollmentId);
            return Result.Fail(AppErrors.Unavailable("certificate.announcement_failed", "The certificate could not be announced; it will be retried."));
        }
    }

    private static Guid DeterministicId(Guid aggregateId, string eventType)
    {
        Span<byte> bytes = stackalloc byte[16];
        aggregateId.TryWriteBytes(bytes);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(eventType));
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= digest[i];
        }

        return new Guid(bytes);
    }
}
