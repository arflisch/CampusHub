using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi;

namespace Dev4u.CampusHub.ServiceDefaults;

/// <summary>
/// Built-in (<c>Microsoft.AspNetCore.OpenApi</c>) document transformer that rewrites the document's
/// <c>servers</c> to the externally reachable address when the service is fetched through a reverse
/// proxy (YARP).
/// <para>
/// The built-in generator derives the server from the request as seen by <em>this</em> service
/// (e.g. <c>https://localhost:7295</c>), which is not reachable from a Swagger UI hosted on the
/// gateway. The proxy forwards the external scheme/host/mount-prefix through the standard
/// <c>X-Forwarded-Proto</c> / <c>X-Forwarded-Host</c> / <c>X-Forwarded-Prefix</c> headers; this
/// transformer turns them into e.g. <c>https://gateway-host/core</c> so "Try it out" targets the
/// gateway (which then routes back to this service).
/// </para>
/// <para>
/// When no forwarding headers are present (service called directly, or build-time document
/// generation where there is no request), the document is left untouched.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by Microsoft.AspNetCore.OpenApi via AddDocumentTransformer<T>().")]
public sealed class ForwardedServerDocumentTransformer : IOpenApiDocumentTransformer
{
    /// <inheritdoc/>
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var request = context.ApplicationServices
            .GetService<IHttpContextAccessor>()?.HttpContext?.Request;
        if (request is null)
        {
            return Task.CompletedTask;
        }

        var proto = FirstForwardedValue(request.Headers["X-Forwarded-Proto"]);
        var host = FirstForwardedValue(request.Headers["X-Forwarded-Host"]);

        // No proxy in front (direct call) => keep the request-derived server the generator produced.
        if (proto is null || host is null)
        {
            return Task.CompletedTask;
        }

        // The mount prefix under which the gateway exposes this service (e.g. "/core"). Optional.
        var prefix = FirstForwardedValue(request.Headers["X-Forwarded-Prefix"]) ?? string.Empty;

        document.Servers = new List<OpenApiServer>
        {
            new() { Url = $"{proto}://{host}{prefix}" },
        };

        return Task.CompletedTask;
    }

    // A proxy chain can produce a comma-separated list; the first (left-most) entry is the value
    // closest to the original client, i.e. the externally reachable gateway.
    private static string? FirstForwardedValue(StringValues header)
    {
        var value = header.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var comma = value.IndexOf(',', StringComparison.Ordinal);
        return (comma >= 0 ? value[..comma] : value).Trim();
    }
}
