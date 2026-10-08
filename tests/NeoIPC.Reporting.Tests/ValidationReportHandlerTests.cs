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
/// asks for the caller's authority, refuses a caller without the authority,
/// and asks for the admin authority only for the values an administrator
/// alone may send. No language is registered for the report, so no request
/// here can reach a render.
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

    static DefaultHttpContext Request(string? acceptLanguage = "en", string? cookie = "JSESSIONID=test-session")
    {
        var context = new DefaultHttpContext();
        if (cookie is not null) context.Request.Headers.Cookie = cookie;
        context.Request.Headers.Accept = "application/pdf";
        if (acceptLanguage is not null) context.Request.Headers.AcceptLanguage = acceptLanguage;
        return context;
    }

    // The policies a caller holds; a caller with F_NEOIPC_ADMIN holds both.
    static readonly string[] Viewer = ["NeoIpcReport"];
    static readonly string[] Administrator = ["NeoIpcReport", "NeoIpcAdmin"];

    Task<IResult> Get(HttpContext context, string[] granted,
        string[]? departmentFilter = null, int[]? rules = null,
        bool? applyValidationExceptions = null, bool? includeUnusedValidationExceptions = null)
    {
        _authorization = new RecordingAuthorizationService(granted);
        var options = Options.Create(new ReportingOptions
        {
            ReportsSourceDir = Path.Combine(_root, "reports"),
            ReportsTempDir = Path.Combine(_root, "renders"),
            ValidationExceptionsDir = Path.Combine(_root, "exceptions"),
        });
        return ValidationReport.Get(
            locale: null, departmentFilter ?? [], rules ?? [], includeTestData: null,
            applyValidationExceptions, includeUnusedValidationExceptions, fragmentMode: null,
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
        var result = await Get(Request(), granted: [], rules: [3]);

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.InsufficientAuthority));
            Assert.That(_authorization.Policies, Is.EqualTo(new[] { "NeoIpcReport" }));
        });
    }

    [TestCase(false, null, TestName = "RenderingWithoutTheExceptions_NeedsTheAdminAuthority")]
    [TestCase(null, true, TestName = "TheAppendixOfUnusedExceptions_NeedsTheAdminAuthority")]
    [TestCase(false, true, TestName = "BothAdministratorValues_NeedTheAdminAuthority")]
    public async Task AnAdministratorsValue_FromAViewer_Is403(
        bool? applyValidationExceptions, bool? includeUnusedValidationExceptions)
    {
        var result = await Get(Request(), Viewer, rules: [3],
            applyValidationExceptions: applyValidationExceptions,
            includeUnusedValidationExceptions: includeUnusedValidationExceptions);

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.InsufficientAuthority));
            Assert.That(_authorization.Policies, Is.EqualTo(new[] { "NeoIpcReport", "NeoIpcAdmin" }));
        });
    }

    // The app sends every boolean on every request, so its viewer's request
    // carries the defaults explicitly: the exceptions applied, no appendix.
    // Another caller may leave them out, which means the same.
    [TestCase(true, false, TestName = "TheDefaultsTheAppSends_NeedOnlyTheReportAuthority")]
    [TestCase(null, null, TestName = "TheDefaultsLeftOut_NeedOnlyTheReportAuthority")]
    public async Task TheDefaults_NeedOnlyTheReportAuthority(
        bool? applyValidationExceptions, bool? includeUnusedValidationExceptions)
    {
        var result = await Get(Request(), Viewer, rules: [3],
            applyValidationExceptions: applyValidationExceptions,
            includeUnusedValidationExceptions: includeUnusedValidationExceptions);

        Assert.Multiple(() =>
        {
            Assert.That(((IStatusCodeHttpResult)result).StatusCode, Is.Not.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(_authorization.Policies, Is.EqualTo(new[] { "NeoIpcReport" }));
        });
    }

    [Test]
    public async Task AnAdministrator_MayRenderWithoutTheExceptionsAndWithTheAppendix()
    {
        var result = await Get(Request(), Administrator, rules: [3],
            applyValidationExceptions: false, includeUnusedValidationExceptions: true);

        Assert.Multiple(() =>
        {
            Assert.That(((IStatusCodeHttpResult)result).StatusCode, Is.Not.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(_authorization.Policies, Is.EqualTo(new[] { "NeoIpcReport", "NeoIpcAdmin" }));
        });
    }

    // An empty JSESSIONID is no session, as the authentication handler reads it.
    [TestCase(null, TestName = "ARequestWithoutASession_Is401WithItsCode_BeforeTheAuthorityIsAskedFor")]
    [TestCase("JSESSIONID=", TestName = "ARequestWithAnEmptySession_Is401WithItsCode_BeforeTheAuthorityIsAskedFor")]
    public async Task ARequestWithoutASession_Is401WithItsCode(string? cookie)
    {
        var result = await Get(Request(cookie: cookie), Administrator, rules: [3]);

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.MissingDhis2Session));
            Assert.That(_authorization.Policies, Is.Empty);
        });
    }

    [Test]
    public async Task AnUnknownRule_Is400WithItsCode_BeforeTheAuthorityIsAskedFor()
    {
        var result = await Get(Request(), Administrator, rules: [3, 9999]);

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
        var result = await Get(Request(acceptLanguage: null), Administrator);

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
        var result = await Get(Request(), Administrator, departmentFilter: ["AT_TEST\nTEST"]);

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.InvalidParameterValue));
            Assert.That(_authorization.Policies, Is.Empty);
        });
    }

    /// <summary>Grants the policies it is given, refuses every other, and records the policies it is asked for.</summary>
    sealed class RecordingAuthorizationService(string[] granted) : IAuthorizationService
    {
        public List<string> Policies { get; } = [];

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            Policies.Add("(requirements)");
            return Task.FromResult(AuthorizationResult.Failed());
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Policies.Add(policyName);
            return Task.FromResult(granted.Contains(policyName)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
        }
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
