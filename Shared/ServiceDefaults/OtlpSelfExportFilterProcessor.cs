using System.Diagnostics;
using OpenTelemetry;

namespace Dev4u.CampusHub.ServiceDefaults;

/// <summary>
/// Drops the spans an application produces while shipping its <em>own</em> telemetry to the OTLP
/// endpoint, so the collector (the Aspire dashboard in development) does not show a trace for every
/// export batch.
/// <para>
/// The OpenTelemetry SDK exporters already wrap their exports in a
/// <c>SuppressInstrumentationScope</c>, so traces and metrics never self-report. The Serilog
/// <c>OpenTelemetry</c> sink does not: it ships log batches over its own <c>Grpc.Net.Client</c>
/// channel on a background thread, which <c>AddGrpcClientInstrumentation</c> happily records as
/// <c>opentelemetry.proto.collector.logs.v1.LogsService/Export</c>. Because the suppression scope is
/// async-local it cannot be applied from the calling code, and the gRPC instrumentation exposes no
/// request filter, so the span is discarded here instead.
/// </para>
/// <para>
/// Clearing <see cref="ActivityTraceFlags.Recorded"/> in <see cref="OnEnd"/> makes every export
/// processor registered <em>after</em> this one skip the activity, so this processor must be added
/// before the OTLP exporter.
/// </para>
/// </summary>
internal sealed class OtlpSelfExportFilterProcessor : BaseProcessor<Activity>
{
    // gRPC: "opentelemetry.proto.collector.{logs|trace|metrics}.v1.<Service>" (rpc.service tag).
    private const string OtlpServicePrefix = "opentelemetry.proto.collector.";

    // HTTP/protobuf: POST {endpoint}/v1/{logs|traces|metrics} (url.full tag).
    private static readonly string[] OtlpHttpPaths = ["/v1/logs", "/v1/traces", "/v1/metrics"];

    /// <inheritdoc/>
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (IsOtlpExport(data))
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }

    private static bool IsOtlpExport(Activity activity)
    {
        if (activity.Kind != ActivityKind.Client)
        {
            return false;
        }

        if (activity.GetTagItem("rpc.service") is string rpcService
            && rpcService.StartsWith(OtlpServicePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        // The display name carries the same value when the tag is missing (older instrumentation).
        if (activity.DisplayName.StartsWith(OtlpServicePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        return activity.GetTagItem("url.full") is string url
               && Array.Exists(OtlpHttpPaths, path => url.EndsWith(path, StringComparison.Ordinal));
    }
}
