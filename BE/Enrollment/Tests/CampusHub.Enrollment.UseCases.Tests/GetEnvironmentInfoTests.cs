using AutoFixture;
using AutoFixture.AutoMoq;
using AwesomeAssertions;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.UseCases.Tests;

public class GetEnvironmentInfoTests
{
    public GetEnvironmentInfoTests()
    {
        _fixture = new Fixture();
        _fixture.Customize(new AutoMoqCustomization());
    }

    private readonly Fixture _fixture;

    [Fact]
    public async Task GetEnvironmentShouldReturnProviderValuesAndServiceName()
    {
        // arrange
        var environmentName = _fixture.Create<string>();
        var serverName = _fixture.Create<string>();

        var provider = _fixture.Freeze<Mock<IEnvironmentProvider>>();
        provider.SetupGet(p => p.EnvironmentName).Returns(environmentName);
        provider.SetupGet(p => p.ServerName).Returns(serverName);

        var sut = _fixture.Create<GetEnvironmentInfo>();

        // act
        var envInfo = await sut.GetEnvironmentInfoAsync();

        // assert
        envInfo.IsSuccess.Should().BeTrue();
        envInfo.Value.Name.Should().Be(environmentName);
        envInfo.Value.Server.Should().Be(serverName);
        envInfo.Value.Service.Should().Be("Enrollment");
    }
}
