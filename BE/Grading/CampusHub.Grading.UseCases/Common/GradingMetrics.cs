using System.Diagnostics.Metrics;

namespace Dev4u.CampusHub.Grading.UseCases.Common;

/// <summary>Business metrics of Grading (meter "CampusHub.Grading").</summary>
public static class GradingMetrics
{
    public const string MeterName = "CampusHub.Grading";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Certificates = Meter.CreateCounter<long>(
        "campushub.certificates.issued", unit: "{certificate}", description: "Certificates issued at the end of a course.");

    public static void RecordIssued() => Certificates.Add(1);
}
