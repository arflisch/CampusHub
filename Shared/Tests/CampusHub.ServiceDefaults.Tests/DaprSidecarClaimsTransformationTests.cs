using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dev4u.CampusHub.ServiceDefaults.Tests;

public class DaprSidecarClaimsTransformationTests
{
    private sealed class MarkingTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) =>
            Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity([new Claim("transformed", "yes")], "Arc4u")));
    }

    private static async Task<ClaimsPrincipal> RunTransformationAsync(ClaimsPrincipal principal)
    {
        var services = new ServiceCollection();
        services.AddScoped<IClaimsTransformation>(_ => new MarkingTransformation());
        services.ExcludeDaprSidecarFromClaimsTransformation();
        var provider = services.BuildServiceProvider();
        await using var _ = provider.ConfigureAwait(false);
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(principal).ConfigureAwait(false);
    }

    [Fact]
    public async Task The_sidecar_identity_is_passed_through_untouched()
    {
        var sidecar = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Dapr"));

        var result = await RunTransformationAsync(sidecar);

        result.Should().BeSameAs(sidecar);
    }

    [Fact]
    public async Task Users_still_go_through_the_registered_transformation()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "Bearer"));

        var result = await RunTransformationAsync(user);

        result.HasClaim("transformed", "yes").Should().BeTrue();
    }
}
