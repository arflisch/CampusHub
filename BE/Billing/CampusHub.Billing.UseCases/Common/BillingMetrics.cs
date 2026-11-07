using System.Diagnostics.Metrics;

namespace Dev4u.CampusHub.Billing.UseCases.Common;

/// <summary>Business metrics of Billing (meter "CampusHub.Billing"): what was charged, declined and refunded.</summary>
public static class BillingMetrics
{
    public const string MeterName = "CampusHub.Billing";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Payments = Meter.CreateCounter<long>(
        "campushub.payments", unit: "{payment}", description: "Charges, by outcome (captured, declined, refunded).");

    private static readonly Counter<long> Amount = Meter.CreateCounter<long>(
        "campushub.payments.amount", unit: "EUR-cents", description: "Money captured and refunded, by outcome.");

    public static void RecordCaptured(int amountInCents) => Record("captured", amountInCents);

    public static void RecordDeclined(string reason) =>
        Payments.Add(1, new KeyValuePair<string, object?>("outcome", "declined"), new KeyValuePair<string, object?>("reason", reason));

    public static void RecordRefunded(int amountInCents) => Record("refunded", amountInCents);

    private static void Record(string outcome, int amountInCents)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        Payments.Add(1, tag);
        Amount.Add(amountInCents, tag);
    }
}
