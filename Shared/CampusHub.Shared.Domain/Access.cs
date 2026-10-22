using System.Collections.Immutable;
using Arc4u.Security.Principal;

namespace Dev4u.CampusHub.Rights;

/// <summary>
/// Defined the operations available to secure the application.
/// </summary>
public enum Access
{
    AccessApplication = 0,
    CanSeeSwaggerFrontendApi = 1,
    CanSeeJobs = 2,
}

public static class Operations
{
    public static readonly ImmutableArray<Operation> Values = ImmutableArray.CreateRange(Enum.GetValues<Access>().Select(o => new Operation { Name = o.ToString(), ID = (int)o }));

    public static readonly ImmutableArray<string> Scopes = ImmutableArray<string>.Empty;
}

