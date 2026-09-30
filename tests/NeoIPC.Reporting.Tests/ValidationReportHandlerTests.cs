using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The handler of <c>GET /validation-report</c>, called in process over a
/// fixture rule catalogue: it refuses a request it cannot serve before it
/// asks for the caller's authority, and refuses a caller without the
/// authority. No language is registered for the report, so no request here
/// can reach a render.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ValidationReportHandlerTests
{
    string _root = null!;
    RecordingAuthorizationService _authorization = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "neoipc-validation-handler-" + Guid.NewGuid().ToString("N"));
        var strings = Path.Combine(_root, "reports", "Validation-Report", "content", "_sR.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(strings)!);
        Directory.CreateDirectory(Path.Combine(_root, "exceptions"));
        File.WriteAllText(strings, "problems:\n  \"3\":\n    summary: The admission date differs.\n");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    static DefaultHttpContext Request(string? acceptLanguage = "en")
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "JSESSIONID=test-session";
        context.Request.Headers.Accept = "application/pdf";
        if (acceptLanguage is not null) context.Request.Headers.AcceptLanguage = acceptLanguage;
        return context;
    }

    Task<IResult> Get(HttpContext context, bool granted,
        string[]? departmentFilter = null, int[]? rules = null)
    {
        _authorization = new RecordingAuthorizationService(granted);
        var options = Options.Create(new ReportingOptions
        {
            ReportsSourceDir = Path.Combine(_root, "reports"),
            ReportsTempDir = Path.Combine(_root, "renders"),
            ValidationExceptionsDir = Path.Combine(_root, "exceptions"),
        });
        return ValidationReport.Get(
            locale: null, departmentFilter ?? [], rules ?? [], includeTestData: null, fragmentMode: null,
            options, new ReportLanguageRegistry(), new ValidationRuleCatalogue(options),
            new ValidationExceptionStorage(options),
            // An IPv4 address is checked for loopback without a DNS lookup.
            Dhis2Endpoint.Build("http://192.0.2.1:8080"),
            _authorization, new ProductionEnvironment(), NullLoggerFactory.Instance,
            context.Request, context, CancellationToken.None);
    }

    [Test]
    public async Task ACallerWithoutTheAuthority_Is403()
    {
        var result = await Get(Request(), granted: false, rules: [3]);

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.InsufficientAuthority));
            Assert.That(_authorization.Policies, Is.EqualTo(new[] { "NeoIpcReport" }));
        });
    }

    [Test]
    public async Task AnUnknownRule_Is400WithItsCode_BeforeTheAuthorityIsAskedFor()
    {
        var result = await Get(Request(), granted: true, rules: [3, 9999]);

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.UnknownValidationRule));
            Assert.That(problem.ProblemDetails.Detail, Does.Contain("9999").And.Not.Contain("rule 3,"));
            Assert.That(_authorization.Policies, Is.Empty);
        });
    }

    [Test]
    public async Task NoLocale_Is406_BeforeTheAuthorityIsAskedFor()
    {
        var result = await Get(Request(acceptLanguage: null), granted: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<StatusCodeHttpResult>());
            Assert.That(((IStatusCodeHttpResult)result).StatusCode, Is.EqualTo(StatusCodes.Status406NotAcceptable));
            Assert.That(_authorization.Policies, Is.Empty);
        });
    }

    [Test]
    public async Task AControlCharacterInADepartment_Is400WithItsCode_BeforeTheAuthorityIsAskedFor()
    {
        var result = await Get(Request(), granted: true, departmentFilter: ["AT_TEST\nTEST"]);

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.InvalidParameterValue));
            Assert.That(_authorization.Policies, Is.Empty);
        });
    }

    /// <summary>Answers every authorization with one fixed result and records the policies it is asked for.</summary>
    sealed class RecordingAuthorizationService(bool granted) : IAuthorizationService
    {
        public List<string> Policies { get; } = [];

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            Policies.Add("(requirements)");
            return Task.FromResult(Result);
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Policies.Add(policyName);
            return Task.FromResult(Result);
        }

        AuthorizationResult Result => granted ? AuthorizationResult.Success() : AuthorizationResult.Failed();
    }

    sealed class ProductionEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "NeoIPC.Reporting.Tests";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
