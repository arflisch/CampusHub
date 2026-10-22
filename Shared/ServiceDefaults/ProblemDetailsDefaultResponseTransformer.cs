using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Dev4u.CampusHub.ServiceDefaults;

/// <summary>
/// Built-in (<c>Microsoft.AspNetCore.OpenApi</c>) document transformer that documents a "default"
/// <see cref="ProblemDetails"/> response on every operation. It registers a single named
/// <c>ProblemDetails</c> component schema and references it, so the generated SDK exposes a typed
/// <c>RestException&lt;ProblemDetails&gt;</c> for otherwise-undeclared error status codes.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by Microsoft.AspNetCore.OpenApi via AddDocumentTransformer<T>().")]
public sealed class ProblemDetailsDefaultResponseTransformer : IOpenApiDocumentTransformer
{
    private const string ProblemDetailsSchemaId = nameof(ProblemDetails);

    /// <inheritdoc/>
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        if (document.Paths is null || document.Paths.Count == 0)
        {
            return;
        }

        // Register the ProblemDetails schema once as a named component. NSwag names the generated SDK
        // type after the component key, keeping the client's typed error as ProblemDetails.
        var schema = await context
            .GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas[ProblemDetailsSchemaId] = schema;

        foreach (var operations in document.Paths.Values.Select(pathItem => pathItem.Operations).OfType<IDictionary<HttpMethod, OpenApiOperation>>())
        {
            foreach (var operation in operations.Values)
            {
                operation.Responses ??= new OpenApiResponses();

                if (operation.Responses.ContainsKey("default"))
                {
                    continue;
                }

                operation.Responses["default"] = new OpenApiResponse
                {
                    Description = "An error occured and is RFC 7807 (problem details).",
                    Content = new Dictionary<string, IOpenApiMediaType>
                    {
                        ["application/problem+json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchemaReference(ProblemDetailsSchemaId, document),
                        },
                    },
                };
            }
        }
    }
}
