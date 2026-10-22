using System.Text.RegularExpressions;

namespace Dev4u.CampusHub.ServiceDefaults;

internal static partial class SharedRegExs
{
    const RegexOptions regexOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex("/(health|alive|healthz)", regexOptions)]
    public static partial Regex RegexHealthCheckEndpoints();

    [GeneratedRegex("/(swagger)", regexOptions)]
    public static partial Regex RegexCoreInstrumentationFilter();

    // Hangfire dashboard of any service ("/{service}/jobs..."), served by the service and proxied by the gateway.
    [GeneratedRegex("^/[^/]+/jobs(/|$)", regexOptions)]
    public static partial Regex RegexHangfireDashboard();

    // Periodic stats polling of the Hangfire dashboard (POST "/{service}/jobs/stats").
    [GeneratedRegex("^/[^/]+/jobs/stats/?$", regexOptions)]
    public static partial Regex RegexHangfireDashboardStats();
}
