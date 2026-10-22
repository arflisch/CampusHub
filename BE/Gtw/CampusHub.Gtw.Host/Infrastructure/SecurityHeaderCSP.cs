using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

namespace Dev4u.CampusHub.Gtw.Host.Infrastructure;

internal static partial class SecurityHeaderCSPExtension
{
    public static void UseSecurityHeaderCSP(this IApplicationBuilder app, IConfiguration configuration)
    {
        app.UseMiddleware<HtmlToNonceMiddleware>();

        app.UseSecurityHeaders(policies =>
        {
            policies.AddDefaultSecurityHeaders();

            void cspBuilder(CspBuilder builder)
            {
                builder.AddUpgradeInsecureRequests(); // upgrade-insecure-requests
                builder.AddBlockAllMixedContent(); // block-all-mixed-content

                var cspDefaultSrc = configuration.GetValue<string>("Csp:DefaultSrc") ?? string.Empty;
                var cspConnectSrc = configuration.GetValue<string>("Csp:ConnectSrc") ?? string.Empty;
                var cspFontSrc = configuration.GetValue<string>("Csp:FontSrc") ?? string.Empty;
                var cspImgSrc = configuration.GetValue<string>("Csp:ImgSrc") ?? string.Empty;
                var authority = configuration.GetValue<string>("Authentication:DefaultAuthority:Url") ?? string.Empty;
                string[] stsFormAction = [
                    authority + "/openid-connect/auth",
                    authority + "/authorize",
                    authority + "/oauth",
                    authority + "/auth",
                    authority + "/authorize/"
                ];
                builder.AddDefaultSrc() // default-src 'self'
                    .Self()
                    .From(cspDefaultSrc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

                builder.AddConnectSrc() // connect-src 'self'
                    .Self()
                    .From(cspConnectSrc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

                builder.AddFontSrc() // font-src 'self'
                    .Self()
                    .From(cspFontSrc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Data();

                builder.AddObjectSrc() // object-src 'none'
                    .None();

                builder.AddFormAction() // form-action 'self'
                    .Self()
                    .From(stsFormAction);

                builder.AddImgSrc() // img-src https:
                    .Self()
                    .Data()
                    .From(cspImgSrc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

                // The inline *scripts* of the Swagger UI and HealthChecks UI are allowed by the nonce that HtmlToNonceMiddleware
                // injects in the served HTML. This works because those <script> tags are present in the HTML we rewrite.
                // Do not add hashes here: their value depends on the rendered content (the Swagger bootstrap script embeds the
                // SwaggerRoutes, for example), so they silently stop matching as soon as the configuration of a UI changes.
                // 'wasm-unsafe-eval' lets the Blazor WebAssembly runtime compile .wasm modules; it does not allow JS eval.
                builder.AddScriptSrc() // script-src 'self' 'wasm-unsafe-eval' 'nonce-...'
                    .Self()
                    .WasmUnsafeEval()
                    .WithNonce();

                // Styles CANNOT use the nonce, unlike scripts. The HealthChecks UI (and Swagger) bundles style the page with
                // webpack's style-loader, which at runtime does document.createElement("style") and injects <style> elements into
                // the DOM. A nonce only authorizes tags present in the HTML we rewrite; these <style> tags are created by JS after
                // the page loads, so there is nothing for HtmlToNonceMiddleware to stamp. style-loader has a nonce hook
                // (__webpack_nonce__), but the prebuilt bundle ships with it unset and it is not settable from outside the bundle.
                // Because a nonce in a style directive makes the browser IGNORE 'unsafe-inline', keeping a style nonce would only
                // guarantee these injected styles stay blocked. So style-src uses 'unsafe-inline' (no nonce). This is low risk:
                // inline *style* injection cannot execute code; the real protection stays on script-src, which remains nonce-locked.
                builder.AddStyleSrc() // style-src 'self' 'unsafe-inline'
                    .Self()
                    .UnsafeInline();

                builder.AddMediaSrc() // media-src https:
                    .None();

                builder.AddFrameAncestors() // frame-ancestors 'none'
                    .None();

                builder.AddBaseUri() // base-uri 'self'
                    .Self();

                builder.AddFrameSrc()
                    .None();
            }

            policies.AddContentSecurityPolicy(cspBuilder);

        });
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by ASP.NET Core via app.UseMiddleware<HtmlToNonceMiddleware>().")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell", "S1144:Unused private types or members should be removed",
        Justification = "The constructor and InvokeAsync are called by ASP.NET Core via app.UseMiddleware<HtmlToNonceMiddleware>().")]
        private sealed partial class HtmlToNonceMiddleware
    {

        #region Constants
        private const string NonceKey = "NETESCAPADES_NONCE";

        /// <summary>
        /// The number of random bytes in a nonce. This is the same size as the one used by NetEscapades, so the generated nonces are indistinguishable from theirs.
        /// </summary>
        private const int NonceByteCount = 32;
        #endregion

        #region Fields

        private readonly RequestDelegate _next;

        /// <summary>
        /// Match the (parts of the) path we want the html content to be adapted.
        /// It is not possible to do this precisely, because some paths can be suffixes depending on the service name(s).
        /// To account for this, we allow the match to start with zero or more non-whitespace characters (\S*). This explains the start of the pattern.
        /// On the other hand, we have either partial paths or stand-alone suffixes:
        /// - For /swagger/ and /ui/resources/, we know that they cannot occur without some suffix. They are partial paths. We therefore check if they are followed by at least one non-whitespace character (\S)
        /// - For /healthchecks-ui and /jobs, they can optionally be followed by a trailing '/', but nothing else. They are stand-alone suffixes.
        /// The only alternative would be to collect them dynamically from the API Gateway configuratio... but this would be very complex.
        /// </summary>
        /// <returns></returns>
        [GeneratedRegex(@"\S*(/swagger/\S+|/healthchecks-ui/?|/ui/resources/\S+|/jobs/?)", RegexOptions.IgnoreCase)]
        public static partial Regex PathsRegex();

        /// <summary>
        /// This is a pragmatic way to identify the html tags we want to replace.
        /// It is pragmatic because it is a simple string matching. If the same strings occur inside HTML literal text, comments or attributes, replacements will be made there as well.
        /// </summary>
        /// <returns></returns>
        [GeneratedRegex(@"(<(script|style|link))(\s*>|\s)", RegexOptions.IgnoreCase)]
        public static partial Regex Tags();

        #endregion

        #region Constructor

        /// <summary>
        /// Default constructor
        /// </summary>
        public HtmlToNonceMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        #endregion

        #region Methods

        /// <summary>
        /// This method is used to inject a nonce attribute in the HTML response to allow the execution of inline scripts & styles for Swagger UI and HealthChecks UI.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.Value is not null && PathsRegex().IsMatch(context.Request.Path.Value))
            {
                // This is an "interesting path" whose content we might manipulate.
                // We should tell the middleware pipeline to not compress the response, because we will modify it. Failure to o this will get decoding errors on the browser side when the site is hosted in IIS.
                context.Request.Headers.Remove(HeaderNames.AcceptEncoding);
                // NetEscapades builds the CSP header - and creates the nonce it advertises - from a Response.OnStarting callback.
                // Because we swap the response body below, nothing "starts" the response while the inner middlewares run, so that callback
                // only fires when we flush the buffered content further down: far too late for us to read the nonce and inject it in the HTML.
                // We therefore create the nonce ourselves and publish it before chaining: NetEscapades reuses the value already present in
                // HttpContext.Items rather than generating its own, so the header and the HTML are guaranteed to advertise the same nonce.
                var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceByteCount));
                context.Items[NonceKey] = nonce;
                // replace the body for the next middleware, so we can read the original response after all middlewares have been executed
                var originalResponseBody = context.Response.Body;
                using var originalResponseStream = new MemoryStream();
                context.Response.Body = originalResponseStream;
                try
                {
                    await _next(context).ConfigureAwait(false);
                }
                finally
                {
                    context.Response.Body = originalResponseBody;
                }
                // rewind the original response stream since we have to read it anyway (to copy it verbatim or to modify it)
                originalResponseStream.Seek(0, SeekOrigin.Begin);
                if (context.Response.ContentType is not null && context.Response.ContentType.Contains(MediaTypeNames.Text.Html, StringComparison.OrdinalIgnoreCase))
                {
                    // if we are on an "interesting" path and it's HTML, we can modify the HTML
                    // read the original content and modify it
                    using var reader = new StreamReader(originalResponseStream, leaveOpen: true /* because the outer 'using' will dispose of the originalStream anyway, no point to do it twice */);
                    var originalHtml = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
                    var modifiedHtml = Tags().Replace(originalHtml, $@"$1 nonce=""{nonce}""$3");
                    // we're assuming here that if the character set is specified in the ContentType, it's UTF-8
                    using var modifiedStream = new MemoryStream(Encoding.UTF8.GetBytes(modifiedHtml));
                    context.Response.ContentLength = modifiedStream.Length;
                    // finally, append our modified content to the response
                    await modifiedStream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
                }
                else
                {
                    await originalResponseStream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
                }
            }
            else
                // This is not an "interesting path", so we just chain to the next handle.
                await _next(context).ConfigureAwait(false);
        }

        #endregion
    }
}
