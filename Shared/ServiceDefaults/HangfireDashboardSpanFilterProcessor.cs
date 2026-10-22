using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;

namespace Dev4u.CampusHub.ServiceDefaults;

/// <summary>
/// Drops every span started while a Hangfire dashboard request is handled (for example the
/// <c>Arc4u</c> caching spans produced by the claims and authorization middlewares), so the dashboard
/// and its periodic stats polling leave no trace at all.
/// <para>
/// The ASP.NET Core instrumentation filter only discards the request span itself; it calls
/// <see cref="MarkRequest"/> so the request can be recognised here. The mark is kept on the
/// <see cref="HttpContext"/> rather than on the request activity, so it does not depend on the
/// child spans being parented to that activity.
/// </para>
/// <para>
/// Clearing <see cref="ActivityTraceFlags.Recorded"/> in <see cref="OnStart"/> makes the export
/// processor skip the activity and the parent-based sampler drop its children, so this processor
/// must be added before the OTLP exporter.
/// </para>
/// </summary>
internal sealed class HangfireDashboardSpanFilterProcessor(IHttpContextAccessor httpContextAccessor)
    : BaseProcessor<Activity>
{
    private static readonly object SuppressedRequestKey = new();

    /// <summary>
    /// Hangfire dashboard pages (GET) and its stats polling (POST), but not actions like delete or requeue.
    /// </summary>
    public static bool IsHangfireDashboardRead(string method, string path)
    {
        if (HttpMethods.IsGet(method))
        {
            return SharedRegExs.RegexHangfireDashboard().IsMatch(path);
        }

        return HttpMethods.IsPost(method) && SharedRegExs.RegexHangfireDashboardStats().IsMatch(path);
    }

    /// <summary>
    /// Flags the request so every span started while it is handled is dropped.
    /// </summary>
    public static void MarkRequest(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Items[SuppressedRequestKey] = true;
    }

    /// <inheritdoc/>
    public override void OnStart(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (httpContextAccessor.HttpContext?.Items.ContainsKey(SuppressedRequestKey) == true)
        {
            data.IsAllDataRequested = false;
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }
}
