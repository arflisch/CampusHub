namespace Dev4u.CampusHub.Gtw.Host.Infrastructure;

/// <summary>
/// CSRF protection of the BFF. A browser sends the session cookie with any request to the gateway, including a form
/// posted from another site; it can only add a custom header to a same-origin call (cross-origin, the CORS preflight
/// refuses it). So a cookie-authenticated write on a frontend API must carry <c>X-CSRF</c>, which the Blazor front adds
/// on every call. Calls already carrying an <c>Authorization</c> header (API clients) do not use the cookie: they are
/// not exposed to CSRF and are left alone.
/// <para>Must run before <c>UseOpenIdBearerInjector</c>, which adds an <c>Authorization</c> header to cookie requests.</para>
/// </summary>
internal static partial class CsrfProtection
{
    public const string HeaderName = "X-CSRF";

    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (IsUnsafe(request.Method)
                && request.Path.Value?.Contains("/frontend/", StringComparison.OrdinalIgnoreCase) == true
                && request.Headers.Authorization.Count == 0
                && !request.Headers.ContainsKey(HeaderName))
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                       .CreateLogger(typeof(CsrfProtection))
                       .LogCsrfHeaderMissing(request.Method, request.Path);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next(context).ConfigureAwait(false);
        });

    private static bool IsUnsafe(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));

    [LoggerMessage(EventId = 1100, Level = LogLevel.Warning, Message = "Refused {Method} {Path}: cookie-authenticated write without the X-CSRF header.")]
    private static partial void LogCsrfHeaderMissing(this ILogger logger, string method, PathString path);
}
