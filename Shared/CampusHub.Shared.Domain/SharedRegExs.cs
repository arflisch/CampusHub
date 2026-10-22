using System.Text.RegularExpressions;

namespace Dev4u.CampusHub.Domain;
internal static partial class SharedRegExs
{
    const RegexOptions regexOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex("/(health)", regexOptions)]
    public static partial Regex RegexClientInstrumentationFilter();

    [GeneratedRegex("/(swagger|frontend|interface|dapr)", regexOptions)]
    public static partial Regex RegexCoreInstrumentationFilter();
}
