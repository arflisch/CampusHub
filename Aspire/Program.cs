using CommunityToolkit.Aspire.Hosting.Dapr;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// Every project's OTLP export is redirected to the collector, which forwards to the dashboard and Seq.
// The receiver is plain (no dev certificate) and the gRPC port is fixed so the Dapr sidecar, configured
// from a static yaml file, can reach it on localhost:4317.
#pragma warning disable S125 // Kept as an opt-in switch: uncomment to route telemetry through the collector.
//builder.AddOpenTelemetryCollector("otelcollector", settings => settings.ForceNonSecureReceiver = true)
//    .WithConfig("./otelcollector/config.yaml")
//    .WithEndpoint("grpc", endpoint => endpoint.Port = 4317)
//    // The collector runs in a container, so it reaches the Seq instance on the host through the host alias.
//    .WithEnvironment("SEQ_ENDPOINT", "http://host.docker.internal:5341")
//    .WithAppForwarding();
#pragma warning restore S125

// ----- Local infrastructure (containers) -----------------------------------------------------------------------

// Single-node replica set: the transactional outbox needs multi-document transactions, Hangfire uses change streams.
// Port 27018 matches the connection strings of the services' Development configuration.
#pragma warning disable ASPIREMONGODB001 // WithReplicaSet is still flagged experimental in Aspire 13.6.
var mongo = builder.AddMongoDB("mongo", port: 27018)
                   .WithReplicaSet()
                   .WithDataVolume();
#pragma warning restore ASPIREMONGODB001
var catalogDb = mongo.AddDatabase("catalog-db", "CampusHub_Catalog");
var enrollmentDb = mongo.AddDatabase("enrollment-db", "CampusHub_Enrollment");
var enrollmentJobsDb = mongo.AddDatabase("enrollment-jobs-db", "CampusHub_Enrollment_Jobs");
var billingDb = mongo.AddDatabase("billing-db", "CampusHub_Billing");
var gradingDb = mongo.AddDatabase("grading-db", "CampusHub_Grading");
var gradingJobsDb = mongo.AddDatabase("grading-jobs-db", "CampusHub_Grading_Jobs");
var notificationDb = mongo.AddDatabase("notification-db", "CampusHub_Notification");

// Broker of the Dapr pub/sub components (Dapr/Development/*-PubSub.yaml).
var rabbitmq = builder.AddRabbitMQ("rabbitmq", port: 5672)
                      .WithManagementPlugin()
                      .WithDataVolume();

// Actor state store of Dapr Workflow (Dapr/Development/CampusHub-Enrollment-workflowstate-StateStore.yaml).
var redis = builder.AddRedis("redis", port: 6380);

// Shared secret between each service and its sidecar: the sidecar sends it in the dapr-api-token header and
// the subscriber endpoints only accept calls carrying it ("Dapr" authorization policy).
var daprAppToken = builder.AddParameter("dapr-app-api-token",
                                        new GenerateParameterDefault { MinLength = 32, Special = false },
                                        secret: true, persist: true);

// A service with its Dapr sidecar. The sidecar gets the API token and, through the env secret store
// (Dapr/Development/CampusHub-store-secrets-Secret.yaml), the broker connection string and the Redis password.
IResourceBuilder<ProjectResource> WithDapr(IResourceBuilder<ProjectResource> project, string appId, int appPort) =>
    project.WithEnvironment("APP_API_TOKEN", daprAppToken)
           .WithDaprSidecar(sidecar => sidecar
               .WithOptions(new DaprSidecarOptions
               {
                   AppId = appId,
                   ResourcesPaths = [@"./../Dapr/Development"],
                   AppProtocol = "https",
                   AppPort = appPort,
                   LogLevel = "debug",
               })
               .WithAnnotation(new EnvironmentCallbackAnnotation(context =>
               {
                   context.EnvironmentVariables["APP_API_TOKEN"] = daprAppToken.Resource;
                   context.EnvironmentVariables["CAMPUSHUB_RABBITMQ_CONNECTION"] = rabbitmq.Resource.ConnectionStringExpression;
                   context.EnvironmentVariables["CAMPUSHUB_REDIS_PASSWORD"] = redis.Resource.PasswordParameter!;
               }))
               .WaitFor(rabbitmq)
               .WaitFor(redis))
           .WaitFor(mongo);

// ----- Services ------------------------------------------------------------------------------------------------

var catalog = WithDapr(builder.AddProject<Projects.CampusHub_Catalog_Host>("Catalog"), "campushub-catalog", 7107)
    .WithReference(catalogDb, "CampusHub_Catalog");

var enrollment = WithDapr(builder.AddProject<Projects.CampusHub_Enrollment_Host>("Enrollment"), "campushub-enrollment", 7081)
    .WithReference(enrollmentDb, "CampusHub_Enrollment")
    .WithReference(enrollmentJobsDb, "HangfireConnection");

var billing = WithDapr(builder.AddProject<Projects.CampusHub_Billing_Host>("Billing"), "campushub-billing", 7039)
    .WithReference(billingDb, "CampusHub_Billing");

var grading = WithDapr(builder.AddProject<Projects.CampusHub_Grading_Host>("Grading"), "campushub-grading", 7278)
    .WithReference(gradingDb, "CampusHub_Grading")
    .WithReference(gradingJobsDb, "HangfireConnection");

var notification = WithDapr(builder.AddProject<Projects.CampusHub_Notification_Host>("Notification"), "campushub-notification", 7261)
    .WithReference(notificationDb, "CampusHub_Notification");

var gateway = builder.AddProject<Projects.CampusHub_Gtw_Host>("Gtw")
       .WithReference(catalog)
       .WithReference(enrollment)
       .WithReference(billing)
       .WithReference(grading)
       .WithReference(notification);

// Enrollment calls Catalog's service API through the gateway ("https://gtw/catalog/"), resolved by service discovery.
enrollment.WithReference(gateway);

// The Dapr integration starts each sidecar through a "<service>-dapr-cli" executable it creates at startup, and the
// WaitFor declared on the sidecar is not copied onto it. daprd exits when a component cannot connect, so without
// this a sidecar started before RabbitMQ accepts connections dies with "Exception (501) EOF". The orchestrator
// awaits the BeforeResourceStartedEvent handlers before starting a resource: wait for the brokers there.
builder.Eventing.Subscribe<BeforeResourceStartedEvent>(async (@event, cancellationToken) =>
{
    if (!@event.Resource.Name.EndsWith("-dapr-cli", StringComparison.Ordinal))
    {
        return;
    }

    var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
    await notifications.WaitForResourceHealthyAsync(rabbitmq.Resource.Name, cancellationToken).ConfigureAwait(false);
    await notifications.WaitForResourceHealthyAsync(redis.Resource.Name, cancellationToken).ConfigureAwait(false);
});

await builder.Build().RunAsync().ConfigureAwait(false);
