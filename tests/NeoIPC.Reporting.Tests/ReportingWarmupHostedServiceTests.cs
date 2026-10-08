using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The startup service's log line naming the address the reports' links to
/// DHIS2 take, which the service reads only at startup: the line after a
/// recreation shows which address the container took.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ReportingWarmupHostedServiceTests
{
    // An IPv4 address is checked for loopback without a DNS lookup.
    const string ServiceAddress = "http://192.0.2.1:8080";

    string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "neoipc-warmup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "reports"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    async Task<List<(LogLevel Level, string Message)>> Start(Dhis2Endpoint endpoint)
    {
        var entries = new List<(LogLevel Level, string Message)>();
        var options = Options.Create(new ReportingOptions
        {
            ReportsSourceDir = Path.Combine(_root, "reports"),
            ReportsTempDir = Path.Combine(_root, "renders"),
            ReferenceDataDir = Path.Combine(_root, "reference-data"),
            ValidationExceptionsDir = Path.Combine(_root, "exceptions"),
        });
        var service = new ReportingWarmupHostedService(
            options, new ReportLanguageRegistry(), endpoint, new CapturingLogger(entries));
        await service.StartAsync(CancellationToken.None);
        return entries;
    }

    [Test]
    public async Task TheStart_LogsTheConfiguredPublicBaseUrl()
    {
        var entries = await Start(Dhis2Endpoint.Build(ServiceAddress, "https://neoipc.example.org/dhis"));

        Assert.That(entries, Has.One.Matches<(LogLevel Level, string Message)>(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains("https://neoipc.example.org/dhis")
            && e.Message.Contains("from Reporting:Dhis2PublicBaseUrl")));
    }

    [Test]
    public async Task TheStart_LogsTheServicesOwnAddress_SayingThePublicBaseUrlIsUnset()
    {
        var entries = await Start(Dhis2Endpoint.Build(ServiceAddress));

        Assert.That(entries, Has.One.Matches<(LogLevel Level, string Message)>(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains(ServiceAddress + "/")
            && e.Message.Contains("Reporting:Dhis2PublicBaseUrl is unset")));
    }

    /// <summary>Records every entry it is given, at whatever level.</summary>
    sealed class CapturingLogger(List<(LogLevel Level, string Message)> sink) : ILogger<ReportingWarmupHostedService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => sink.Add((logLevel, formatter(state, exception)));
    }
}
