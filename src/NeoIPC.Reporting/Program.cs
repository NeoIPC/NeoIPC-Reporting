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
using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NeoIPC.Reporting.Authorization;
using NeoIPC.Reporting.Resources;

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
    .Bind(builder.Configuration.GetSection(ReportingOptions.SectionName));
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

// One policy today: require the DHIS2 superuser authority (gate on
// the /admin/* endpoints and conditionally on /reference-report's
// ad-hoc preview mode). Future migration to a NeoIPC-specific
// authority is captured in tasks/replace-neoipc-reportapp-js.md.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequiresAll",
        p => p.RequireClaim(Dhis2ClaimTypes.Authority, Authorities.All));
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

app.MapGet("reference-report", ReferenceReport.Get)
    .WithName("GetReferenceReport")
    .WithRequestTimeout(TimeSpan.FromSeconds(360));
app.MapGet("reference-report/parameters", () =>
        Results.Ok(new { fields = ReferenceReportApiParameters.Schema }))
    .WithName("GetReferenceReportParameters");
app.MapGet("partner-report", PartnerReport.Get)
    .WithName("GetPartnerReport")
    .WithRequestTimeout(TimeSpan.FromSeconds(600));
app.MapPost("partner-report", PartnerReport.Post)
    .WithName("PostPartnerReport")
    .DisableAntiforgery()
    .WithRequestTimeout(TimeSpan.FromSeconds(600));
app.MapGet("partner-report/parameters", () =>
        Results.Ok(new { fields = PartnerReportApiParameters.Schema }))
    .WithName("GetPartnerReportParameters");

// Public-tier listing — any authenticated user, no specific authority.
// Partners pick a referenceDataId from this listing to feed into
// /reference-report (stored-data mode).
app.MapGet("reference-data", ReferenceDataEndpoints.List)
    .WithName("ListReferenceData")
    .RequireAuthorization();

// Everything under /admin/* requires the superuser authority.
var admin = app.MapGroup("admin").RequireAuthorization("RequiresAll");

admin.MapGet("reference-data", ReferenceDataEndpoints.AdminList)
    .WithName("AdminListReferenceData");
admin.MapGet("reference-data/{id}", ReferenceDataEndpoints.AdminDownload)
    .WithName("AdminDownloadReferenceData");
admin.MapPost("reference-data", ReferenceDataEndpoints.AdminUpload)
    .WithName("AdminUploadReferenceData")
    .DisableAntiforgery()
    .WithRequestTimeout(TimeSpan.FromSeconds(120));
admin.MapDelete("reference-data/{id}", ReferenceDataEndpoints.AdminDelete)
    .WithName("AdminDeleteReferenceData");

admin.MapGet("validation-exceptions", ValidationExceptionEndpoints.AdminList)
    .WithName("AdminListValidationExceptions");
admin.MapGet("validation-exceptions/{id}", ValidationExceptionEndpoints.AdminDownload)
    .WithName("AdminDownloadValidationException");
admin.MapPost("validation-exceptions", ValidationExceptionEndpoints.AdminUpload)
    .WithName("AdminUploadValidationException")
    .DisableAntiforgery();
admin.MapDelete("validation-exceptions/{id}", ValidationExceptionEndpoints.AdminDelete)
    .WithName("AdminDeleteValidationException");

app.Run();
