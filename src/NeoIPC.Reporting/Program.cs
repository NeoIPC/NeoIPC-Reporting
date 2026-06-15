// NeoIPC.Reporting — minimal-API host wiring.
//
// Service responsibilities:
//   - Render Surveillance-Toolkit reports (Quarto + R) to PDF/HTML, or
//     return the underlying neoipcr data as JSON.
//   - Manage admin-uploaded reference datasets and validation-exception
//     files referenced by report renders.
//   - Authenticate via DHIS2 session cookies; gate admin endpoints with
//     a claims-based authorization policy.
//
// The wiring below is laid out in three blocks: framework primitives →
// DI singletons (configuration, registries, storage, R-subprocess
// helpers, auth) → request pipeline + endpoint mapping. See each
// referenced type for the design rationale.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NeoIPC.Reporting.Authorization;
using NeoIPC.Reporting.Resources;

// Schema-emit short-circuit: writes the source-generator-emitted
// <Report>ApiParameters.Schema arrays to JSON files in <dir>, then
// exits without starting the web host. The neoipc-app workspace
// vendors these snapshots and runs a CI drift check against them; see
// `repos/neoipc-app/scripts/check-schema-drift.mjs`. The file names
// match the form spec keys: `partner-report.json`, `reference-report.json`.
if (args.Length >= 2 && args[0] == "--emit-schemas")
{
    var outDir = args[1];
    Directory.CreateDirectory(outDir);
    var jsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    File.WriteAllText(
        Path.Combine(outDir, "partner-report.json"),
        JsonSerializer.Serialize(
            new { fields = PartnerReportApiParameters.Schema }, jsonOpts));
    File.WriteAllText(
        Path.Combine(outDir, "reference-report.json"),
        JsonSerializer.Serialize(
            new { fields = ReferenceReportApiParameters.Schema }, jsonOpts));
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddRequestTimeouts();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
// API DTOs serialise camelCase; null fields are dropped on the wire so
// optional metadata doesn't clutter the listings.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddOptions<ReportingOptions>()
    .Bind(builder.Configuration.GetSection(ReportingOptions.SectionName))
    // Resolve ReportsSourceDir / NeoIpcrDevPath relative to ContentRoot
    // when configured as a relative path. Container deployments use
    // absolute paths (/toolkit/reports, /neoipcr); workspace IDE
    // launches via appsettings.Development.json use relative paths
    // pointing back into the workspace tree.
    .PostConfigure<IHostEnvironment>((opts, env) =>
    {
        opts.ReportsSourceDir = ResolveAgainstContentRoot(opts.ReportsSourceDir, env.ContentRootPath);
        opts.NeoIpcrDevPath = ResolveAgainstContentRoot(opts.NeoIpcrDevPath, env.ContentRootPath);

        static string ResolveAgainstContentRoot(string value, string contentRoot)
            => Path.IsPathRooted(value) ? value : Path.GetFullPath(value, contentRoot);
    });
builder.Services.AddSingleton<ReportLanguageRegistry>();
builder.Services.AddSingleton(sp =>
    Dhis2Endpoint.Build(sp.GetRequiredService<IOptions<ReportingOptions>>().Value.Dhis2BaseUrl));
builder.Services.AddHostedService<ReportingWarmupHostedService>();

builder.Services.AddSingleton<ReferenceDataStorage>();
builder.Services.AddSingleton<ValidationExceptionStorage>();
builder.Services.AddSingleton<ReferenceDataMetadataExtractor>();

builder.Services.AddSingleton<SessionPrincipalCache>();
// Typed HttpClient for DHIS2 /api/me. SocketsHttpHandler is the
// recommended primary handler on .NET 6+; AllowAutoRedirect=false so a
// 302 to the DHIS2 login page surfaces as auth failure rather than
// being followed; UseCookies=false because we set the JSESSIONID
// header explicitly per request.
builder.Services.AddHttpClient<Dhis2SessionClient>(http =>
    {
        http.Timeout = TimeSpan.FromSeconds(5);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    });

builder.Services
    .AddAuthentication(Dhis2SessionAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<Dhis2SessionAuthenticationOptions, Dhis2SessionAuthenticationHandler>(
        Dhis2SessionAuthenticationDefaults.AuthenticationScheme, _ => { });

// Two NeoIPC authority tiers (DHIS2 superuser ALL satisfies both):
//   - NeoIpcReport (F_NEOIPC_REPORT) gates report viewing — partner
//     reports and reference-report stored-data mode.
//   - NeoIpcAdmin (F_NEOIPC_ADMIN) gates the admin endpoints and
//     reference-report's live ad-hoc preview mode.
// RequireClaim with several values is OR-matched, so each higher tier
// is folded into the lower-tier policy.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("NeoIpcReport", p => p.RequireClaim(
        Dhis2ClaimTypes.Authority,
        Authorities.F.NeoipcReport, Authorities.F.NeoipcAdmin, Authorities.All));
    options.AddPolicy("NeoIpcAdmin", p => p.RequireClaim(
        Dhis2ClaimTypes.Authority,
        Authorities.F.NeoipcAdmin, Authorities.All));
});

var app = builder.Build();
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();
app.UseRequestTimeouts();

// Endpoint mapping lives in ApiEndpoints so the endpoint set is
// unit-testable: EndpointAuthorizationTests asserts every endpoint is
// route-authorized, marked InHandlerAuthorized, or marked PublicEndpoint
// (no endpoint silently public).
ApiEndpoints.Map(app);

app.Run();
