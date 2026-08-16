using System.Text;
using AngleSharp;

namespace NeoIPC.Reporting.Resources;

/// <summary>
/// Post-processes Quarto's minimal-HTML output into a body-scoped
/// fragment suitable for inline injection into a containing app
/// (the NeoIPC DHIS2 App Platform app, <c>neoipc-app</c>).
/// </summary>
/// <remarks>
/// <para>
/// Applied to HTML output when requested via the <c>fragmentMode</c> query parameter on the
/// HTML-emitting report endpoints. The app sends <c>?fragmentMode=true</c> explicitly to inline
/// the render into its container; the parameter defaults to <c>false</c>, so a consumer that omits
/// it (curl downloads, ad-hoc scripts) gets the full standalone Quarto-minimal document. PDF and
/// JSON are unaffected.
/// </para>
///
/// <para>
/// Two operations:
/// </para>
///
/// <list type="number">
///   <item><description>Strip the <c>&lt;html&gt;</c>/<c>&lt;head&gt;</c>/
///   <c>&lt;body&gt;</c> wrappers. The output is the <c>&lt;body&gt;</c>'s
///   inner HTML, preceded by every <c>&lt;style&gt;</c> block hoisted
///   from anywhere in the document.</description></item>
///   <item><description>Prefix-scope every CSS selector in the hoisted
///   <c>&lt;style&gt;</c> blocks to <see cref="ContainerSelector"/>. Style
///   rules get <c>#neoipc-rendered-report</c> prepended; <c>:root</c> /
///   <c>html</c> / <c>body</c> selectors are rewritten to the container
///   (so custom-property declarations and root-scoped rules land on the
///   fragment's root). <c>@media</c> / <c>@supports</c> / <c>@container</c>
///   / <c>@layer</c> are recursed into; <c>@keyframes</c> /
///   <c>@font-face</c> / <c>@page</c> / <c>@property</c> /
///   <c>@import</c> / <c>@charset</c> / <c>@namespace</c> pass through
///   verbatim.</description></item>
/// </list>
///
/// <para>
/// The container selector is fixed at <see cref="ContainerSelector"/>
/// and surfaced to the client via the <see cref="ContentType"/>
/// response header. The frontend's inline-report component pins the same
/// id on its outer <c>&lt;div&gt;</c>.
/// </para>
/// </remarks>
static class HtmlFragmentTransformer
{
    public const string ContainerId = "neoipc-rendered-report";
    public const string ContainerSelector = "#" + ContainerId;

    /// <summary>
    /// <c>Content-Type</c> value the endpoints emit when fragment mode
    /// is active. The <c>profile</c> + <c>container</c> media-type
    /// parameters are how the frontend confirms it received a fragment
    /// (and where to mount it).
    /// </summary>
    public const string ContentType =
        "text/html; profile=\"neoipc-fragment\"; container=\"" +
        ContainerSelector + "\"";

    /// <summary>
    /// Parse <paramref name="input"/> as an HTML document, hoist its
    /// <c>&lt;style&gt;</c> blocks with selectors prefix-scoped to
    /// <see cref="ContainerSelector"/>, and return a fresh UTF-8
    /// <see cref="MemoryStream"/> containing the body-only fragment.
    /// Caller owns the returned stream; <paramref name="input"/> is
    /// consumed but not disposed.
    /// </summary>
    public static async Task<MemoryStream> TransformAsync(
        Stream input, CancellationToken cancellationToken)
    {
        var config = Configuration.Default;
        using var ctx = BrowsingContext.New(config);

        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var document = await ctx.OpenAsync(req => req.Content(buffer), cancellationToken);

        var sb = new StringBuilder();
        foreach (var style in document.QuerySelectorAll("style"))
        {
            var rewritten = CssScopeRewriter.Rewrite(style.TextContent, ContainerSelector);
            sb.Append("<style>").Append(rewritten).Append("</style>\n");
        }

        if (document.Body is { } body)
            sb.Append(body.InnerHtml);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new MemoryStream(bytes) { Position = 0 };
    }

    /// <summary>
    /// Endpoint-layer hook: when <paramref name="fragmentMode"/> is on
    /// and the generator emitted HTML, replace <see cref="DataResult.Result"/>
    /// with a fragment-mode <see cref="IResult"/>. Otherwise return the
    /// original result unchanged (so PDFs, JSON, and non-fragment HTML
    /// flow through untouched).
    /// </summary>
    public static async Task<IResult> MaybeFragmentize(
        DataResult dataResult, string mediaType, bool fragmentMode,
        CancellationToken cancellationToken)
    {
        if (!fragmentMode || !dataResult.Success ||
            mediaType != "text/html" || dataResult.Data is null)
            return dataResult.Result;

        var fragmentStream = await TransformAsync(dataResult.Data, cancellationToken);
        await dataResult.Data.DisposeAsync();
        return Results.Stream(fragmentStream, ContentType);
    }
}
