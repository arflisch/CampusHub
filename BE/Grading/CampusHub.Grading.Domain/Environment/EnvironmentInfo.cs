namespace Dev4u.CampusHub.Grading.Domain;

/// <summary>
/// Provide information on the running environment.
/// </summary>
public class EnvironmentInfo
{
    /// <summary>
    /// Name of the application - service.
    /// </summary>
    public string? Name { get; init; }


    /// <summary>
    /// Server name.
    /// </summary>
    public string? Server { get; init; }

    /// <summary>
    /// Name of the service.
    /// </summary>
    public string? Service { get; init; }
}