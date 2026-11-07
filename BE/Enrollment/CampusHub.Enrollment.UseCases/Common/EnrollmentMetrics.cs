using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Dev4u.CampusHub.Enrollment.UseCases.Common;

/// <summary>
/// Business metrics of the enrollment saga (meter "CampusHub.Enrollment", exported over OTLP like the rest of the
/// telemetry). Each one is recorded where the state actually changes, never on a replayed saga step.
/// </summary>
public static class EnrollmentMetrics
{
    public const string MeterName = "CampusHub.Enrollment";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Requested = Meter.CreateCounter<long>(
        "campushub.enrollments.requested", unit: "{enrollment}", description: "Enrollment requests received.");

    private static readonly Counter<long> Settled = Meter.CreateCounter<long>(
        "campushub.enrollments.settled", unit: "{enrollment}", description: "Sagas finished, by outcome (confirmed, rejected) and reason.");

    private static readonly Counter<long> Cancelled = Meter.CreateCounter<long>(
        "campushub.enrollments.cancelled", unit: "{enrollment}", description: "Confirmed enrollments cancelled, by reason.");

    private static readonly Histogram<double> SagaDuration = Meter.CreateHistogram<double>(
        "campushub.enrollment.saga.duration", unit: "s", description: "From the request to the final answer of the saga.");

    public static void RecordRequested() => Requested.Add(1);

    public static void RecordConfirmed(TimeSpan sagaDuration) => RecordSettled("confirmed", reason: null, sagaDuration);

    public static void RecordRejected(string reason, TimeSpan sagaDuration) => RecordSettled("rejected", reason, sagaDuration);

    public static void RecordCancelled(string reason) => Cancelled.Add(1, new KeyValuePair<string, object?>("reason", reason));

    private static void RecordSettled(string outcome, string? reason, TimeSpan sagaDuration)
    {
        var tags = new TagList { { "outcome", outcome } };
        if (reason is not null)
        {
            tags.Add("reason", reason);
        }

        Settled.Add(1, tags);
        SagaDuration.Record(sagaDuration.TotalSeconds, new KeyValuePair<string, object?>("outcome", outcome));
    }
}
