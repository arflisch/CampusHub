using System.Security.Claims;
using Dev4u.CampusHub.Rights;

namespace Dev4u.CampusHub.ServiceDefaults;

/// <summary>
/// Who is a teacher or an administrator (section <c>Authorization:Roles</c>). Two sources, so the same code works
/// whether or not the identity provider can be configured:
/// <list type="bullet">
/// <item>a permission in the <c>scope</c> claim of the access token: role-based access control done by the identity
/// provider, the target setup;</item>
/// <item>user ids (the token's <c>sub</c>) listed here, comma-separated, or <c>*</c> for every signed-in user: for an
/// identity provider you cannot configure, such as a shared development tenant.</item>
/// </list>
/// </summary>
public sealed class RoleOptions
{
    public const string SectionName = "Authorization:Roles";

    public string TeacherPermission { get; set; } = "campushub:teach";

    public string AdminPermission { get; set; } = "campushub:admin";

    public string Teachers { get; set; } = string.Empty;

    public string Admins { get; set; } = string.Empty;
}

/// <summary>Maps the claims of an access token to the CampusHub roles and operations.</summary>
public static class CampusHubRoles
{
    public const string User = "User";
    public const string Teacher = "Teacher";
    public const string Admin = "Admin";

    public static IReadOnlyList<string> RolesOf(IReadOnlyCollection<Claim> claims, RoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(options);

        if (claims.Count == 0)
        {
            return [];
        }

        var userId = claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var permissions = claims.Where(c => c.Type is "scope" or "scp")
                                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                                .ToHashSet(StringComparer.Ordinal);

        var roles = new List<string> { User };
        if (permissions.Contains(options.TeacherPermission) || IsListed(userId, options.Teachers))
        {
            roles.Add(Teacher);
        }

        if (permissions.Contains(options.AdminPermission) || IsListed(userId, options.Admins))
        {
            roles.Add(Admin);
        }

        return roles;
    }

    public static IReadOnlyList<Access> OperationsOf(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var operations = new SortedSet<Access>();
        foreach (var role in roles)
        {
            switch (role)
            {
                case User:
                    operations.Add(Access.AccessApplication);
                    break;
                case Teacher:
                    operations.Add(Access.CanTeach);
                    break;
                case Admin:
                    operations.Add(Access.CanSeeSwaggerFrontendApi);
                    operations.Add(Access.CanSeeJobs);
                    break;
            }
        }

        return [.. operations];
    }

    private static bool IsListed(string? userId, string list)
    {
        var entries = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Contains("*", StringComparer.Ordinal)
               || (userId is not null && entries.Contains(userId, StringComparer.Ordinal));
    }
}
