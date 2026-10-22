using Arc4u.MongoDB;
using Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Tests;

/// <summary>
/// One MongoDB single-node replica set for the whole test class (transactions need a replica set). Each test
/// gets its own database, so tests never see each other's documents.
/// </summary>
public sealed class MongoReplicaSetFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0")
        .WithReplicaSet()
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A client factory over a fresh database, wired exactly like the host does it (Arc4u AddMongoDatabase).</summary>
    public IMongoClientFactory<DatabaseContext> CreateClientFactory()
    {
        var url = new MongoUrlBuilder(_container.GetConnectionString())
        {
            DatabaseName = $"enrollment_{Guid.NewGuid():N}",
            AuthenticationSource = "admin",
            DirectConnection = true,
        }.ToMongoUrl();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:CampusHub_Enrollment"] = url.ToString() })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMongoDatabase<DatabaseContext>(configuration, "CampusHub_Enrollment", settings => settings.DirectConnection = true);
        return services.BuildServiceProvider().GetRequiredService<IMongoClientFactory<DatabaseContext>>();
    }
}
