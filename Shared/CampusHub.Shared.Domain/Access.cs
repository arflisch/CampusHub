using System.Collections.Immutable;
using Arc4u.Security.Principal;

namespace Dev4u.CampusHub.Rights;

/// <summary>
/// Defined the operations available to secure the application.
/// </summary>
public enum Access
{
    /// <summary>Any signed-in user: browse the catalogue, enroll, read their notifications.</summary>
    AccessApplication = 0,

    /// <summary>Administrators: the Swagger pages.</summary>
    CanSeeSwaggerFrontendApi = 1,

    /// <summary>Administrators: the Hangfire dashboards.</summary>
    CanSeeJobs = 2,

    /// <summary>Teachers: create, publish and list the courses they teach.</summary>
    CanTeach = 3,
}

public static class Operations
{
    public static readonly ImmutableArray<Operation> Values = ImmutableArray.CreateRange(Enum.GetValues<Access>().Select(o => new Operation { Name = o.ToString(), ID = (int)o }));

    public static readonly ImmutableArray<string> Scopes = ImmutableArray<string>.Empty;
}

