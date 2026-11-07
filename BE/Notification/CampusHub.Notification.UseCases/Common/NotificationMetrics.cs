using System.Diagnostics.Metrics;

namespace Dev4u.CampusHub.Notification.UseCases.Common;

/// <summary>Business metrics of Notification (meter "CampusHub.Notification").</summary>
public static class NotificationMetrics
{
    public const string MeterName = "CampusHub.Notification";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Created = Meter.CreateCounter<long>(
        "campushub.notifications.created", unit: "{notification}", description: "Notifications written, by title.");

    public static void RecordCreated(string title) => Created.Add(1, new KeyValuePair<string, object?>("title", title));
}
