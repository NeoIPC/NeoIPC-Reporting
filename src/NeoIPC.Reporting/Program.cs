using NeoIPC.Reporting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddRequestTimeouts();

var app = builder.Build();
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("reference-report", ReferenceReport.Get)
    .WithName("GetReferenceReport")
    .WithRequestTimeout(TimeSpan.FromSeconds(360));
app.MapGet("partner-report", PartnerReport.Get)
    .WithName("GetPartnerReport")
    .WithRequestTimeout(TimeSpan.FromSeconds(360));

//app.MapGet("reference-report-snapshots", () => { });
//app.MapPost("reference-report-snapshots", () => { });
//app.MapDelete("reference-report-snapshots", () => { });

app.Run();