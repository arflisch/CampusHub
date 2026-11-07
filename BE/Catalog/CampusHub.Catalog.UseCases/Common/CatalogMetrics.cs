using System.Diagnostics.Metrics;

namespace Dev4u.CampusHub.Catalog.UseCases.Common;

/// <summary>Business metrics of Catalog (meter "CampusHub.Catalog"): the life of the courses.</summary>
public static class CatalogMetrics
{
    public const string MeterName = "CampusHub.Catalog";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Courses = Meter.CreateCounter<long>(
        "campushub.courses", unit: "{course}", description: "Courses created, published and cancelled.");

    public static void RecordCreated() => Record("created");

    public static void RecordPublished() => Record("published");

    public static void RecordCancelled() => Record("cancelled");

    private static void Record(string change) => Courses.Add(1, new KeyValuePair<string, object?>("change", change));
}
