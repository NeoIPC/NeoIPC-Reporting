using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NeoIPC.Reporting;
using NeoIPC.Reporting.Resources;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// The handlers of <c>/admin/validation-exceptions</c>, called in process:
/// an upload is stored only once the checker accepts it, a refusal carries
/// the checker's reason, a refusal, a failed check, and a body that fails
/// mid-copy all leave the stored file and the staging area as they were, and
/// the listing reports a stored file whose sidecar cannot be read, which the
/// renders apply all the same.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ValidationExceptionEndpointsTests
{
    string _dir = null!;
    ValidationExceptionStorage _storage = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "neoipc-validation-exceptions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _storage = new ValidationExceptionStorage(
            Options.Create(new ReportingOptions { ValidationExceptionsDir = _dir }));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    static HttpRequest Upload(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "text/csv";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }

    static readonly ClaimsPrincipal Administrator = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "admin-user")], "test"));

    [Test]
    public async Task ARefusedUpload_Is400WithTheCheckersReason_AndStoresNothing()
    {
        var checker = new FakeChecker("The validation exception file \"List\" does not hold exception records.");

        var result = await ValidationExceptionEndpoints.AdminUpload(
            "List", Upload("not,a,list\n"), _storage, checker, Administrator, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(problem.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(problem.ProblemDetails.Extensions["code"], Is.EqualTo(ProblemCodes.InvalidValidationExceptions));
            Assert.That(problem.ProblemDetails.Detail, Is.EqualTo(checker.Reason));
            // The checker was given the display name to name the file by.
            Assert.That(checker.DisplayNames, Is.EqualTo(new[] { "List" }));
            Assert.That(_storage.Exists(), Is.False);
            // The staged file was discarded: the directory holds nothing.
            Assert.That(Directory.EnumerateFileSystemEntries(_dir), Is.Empty);
        });
    }

    [Test]
    public async Task ARefusedUpload_LeavesTheStoredFileAsItWas()
    {
        await ValidationExceptionEndpoints.AdminUpload(
            "First", Upload("first\n"), _storage, new FakeChecker(null), Administrator, CancellationToken.None);

        await ValidationExceptionEndpoints.AdminUpload(
            "Second", Upload("second\n"), _storage, new FakeChecker("refused"), Administrator, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(_storage.DataPath()), Is.EqualTo("first\n"));
            Assert.That(_storage.ReadMetadata()!.DisplayName, Is.EqualTo("First"));
        });
    }

    [Test]
    public async Task ACheckThatFails_PropagatesItsError_AndLeavesTheStoredFileAndTheStagingAreaAsTheyWere()
    {
        await ValidationExceptionEndpoints.AdminUpload(
            "First", Upload("first\n"), _storage, new FakeChecker(null), Administrator, CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(() => ValidationExceptionEndpoints.AdminUpload(
            "Second", Upload("second\n"), _storage, new FailingChecker(), Administrator, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(_storage.DataPath()), Is.EqualTo("first\n"));
            Assert.That(_storage.ReadMetadata()!.DisplayName, Is.EqualTo("First"));
            Assert.That(Directory.EnumerateFiles(_dir, "staging-*"), Is.Empty);
        });
    }

    [Test]
    public void AnUploadWhoseBodyFailsMidCopy_LeavesNoStagingFile()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new AbortedBody();

        Assert.ThrowsAsync<IOException>(() => ValidationExceptionEndpoints.AdminUpload(
            "List", context.Request, _storage, new FakeChecker(null), Administrator, CancellationToken.None));

        Assert.That(Directory.EnumerateFileSystemEntries(_dir), Is.Empty);
    }

    [Test]
    public async Task AnAcceptedUpload_IsStoredWithItsSidecar()
    {
        var checker = new FakeChecker(null);

        var result = await ValidationExceptionEndpoints.AdminUpload(
            null, Upload("RULE_ID\n"), _storage, checker, Administrator, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<Ok<AdminValidationExceptionMetadata>>());
        var metadata = ((Ok<AdminValidationExceptionMetadata>)result).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(_storage.Exists(), Is.True);
            Assert.That(File.ReadAllText(_storage.DataPath()), Is.EqualTo("RULE_ID\n"));
            Assert.That(metadata.SizeBytes, Is.EqualTo(8));
            Assert.That(metadata.UploaderUserId, Is.EqualTo("admin-user"));
            // Without a display name the checker names the file by the default one.
            Assert.That(checker.DisplayNames, Is.EqualTo(new[] { metadata.DisplayName }));
            Assert.That(metadata.DisplayName, Does.StartWith("Validation exceptions "));
        });
    }

    [Test]
    public void TheListing_ReportsAFileWhoseSidecarCannotBeRead_FromTheDataFile()
    {
        File.WriteAllText(_storage.MetaPath(), "not json");
        File.WriteAllText(_storage.DataPath(), "RULE_ID\n");
        File.SetLastWriteTimeUtc(_storage.DataPath(), new DateTime(2026, 9, 1, 6, 30, 0, DateTimeKind.Utc));

        var result = ValidationExceptionEndpoints.AdminGet(_storage);

        Assert.That(result, Is.InstanceOf<Ok<AdminValidationExceptionMetadata>>());
        var metadata = ((Ok<AdminValidationExceptionMetadata>)result).Value!;
        Assert.Multiple(() =>
        {
            Assert.That(metadata.SizeBytes, Is.EqualTo(8));
            Assert.That(metadata.CreatedAt, Is.EqualTo(new DateTimeOffset(2026, 9, 1, 6, 30, 0, TimeSpan.Zero)));
            Assert.That(metadata.UploaderUserId, Is.Null);
        });
    }

    [Test]
    public void TheListing_WithoutAFile_Is404()
    {
        var result = ValidationExceptionEndpoints.AdminGet(_storage);

        Assert.That(((ProblemHttpResult)result).StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    /// <summary>Answers every check with one fixed reason (<c>null</c> accepts) and records the names it is given.</summary>
    sealed class FakeChecker(string? reason) : IValidationExceptionChecker
    {
        public string? Reason { get; } = reason;
        public List<string> DisplayNames { get; } = [];

        public Task<string?> CheckAsync(string path, string displayName, CancellationToken ct)
        {
            DisplayNames.Add(displayName);
            return Task.FromResult(Reason);
        }
    }

    /// <summary>Fails every check, as the checker does when the script itself fails.</summary>
    sealed class FailingChecker : IValidationExceptionChecker
    {
        public Task<string?> CheckAsync(string path, string displayName, CancellationToken ct) =>
            throw new InvalidOperationException("The validation-exception check failed with exit status 1.");
    }

    /// <summary>A request body that yields its first bytes and then fails, as an aborted upload does.</summary>
    sealed class AbortedBody : Stream
    {
        bool _sent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_sent) throw new IOException("The client aborted the upload.");
            _sent = true;
            var head = "RULE_ID,"u8;
            head.CopyTo(buffer);
            return head.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
