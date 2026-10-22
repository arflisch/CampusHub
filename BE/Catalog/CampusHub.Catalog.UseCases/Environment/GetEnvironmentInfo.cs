using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Catalog.Domain;
using Dev4u.CampusHub.Catalog.UseCases.Common;
using FluentResults;

namespace Dev4u.CampusHub.Catalog.UseCases;

[Export, Scoped]
public sealed class GetEnvironmentInfo(IEnvironmentProvider environment)
{
    public Task<Result<EnvironmentInfo>> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default)
    {
        var info = new EnvironmentInfo
        {
            Name = environment.EnvironmentName,
            Server = environment.ServerName,
            Service = Constants.ServiceName,
        };

        return Task.FromResult(Result.Ok(info));
    }
}
