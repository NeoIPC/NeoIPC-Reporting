using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NeoIPC.Reporting.Authorization;
using NUnit.Framework;

namespace NeoIPC.Reporting.Tests;

/// <summary>
/// How <see cref="Dhis2SessionClient"/> answers when DHIS2 does not answer at
/// all: its own timeout is a failed authentication, the caller's cancellation
/// is the caller's.
/// </summary>
/// <remarks>
/// The two look alike on the wire — <see cref="HttpClient"/> reports its timeout
/// as a <see cref="TaskCanceledException"/> — and a handler that treated them
/// alike would either crash the request on a slow DHIS2 or swallow a client that
/// went away. A resolver that holds an unknown host name open for the whole
/// timeout, as a VPN's can, is where the first case comes from in practice.
/// </remarks>
[TestFixture]
[Category("Unit")]
public class Dhis2SessionClientTests
{
    /// <summary>A DHIS2 that never answers: the request waits until it is cancelled.</summary>
    sealed class NeverAnsweringHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }
    }

    /// <summary>A DHIS2 that sends its headers and then goes quiet: the body never arrives.</summary>
    /// <remarks>
    /// A different outage from <see cref="NeverAnsweringHandler"/>, and one that handler
    /// cannot stand in for: it stalls before the headers, which every completion option
    /// already covers. <see cref="HttpClient.Timeout"/> spans a streamed response only
    /// until its headers are in, so a stall after them is bounded by nothing unless the
    /// client buffers the body. <see cref="BodyReadWasCancelled"/> is what records that
    /// the timeout reached the body rather than stopping at the headers.
    /// </remarks>
    sealed class HeadersThenSilenceHandler : HttpMessageHandler
    {
        readonly SilentBody _body = new();

        /// <summary>Whether the read of the body was cancelled, rather than left to run.</summary>
        public bool BodyReadWasCancelled => _body.ReadWasCancelled;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(_body),
            });

        /// <summary>
        /// A readable stream that yields nothing for far longer than the timeout under
        /// test and then ends. It ends rather than stalling forever so that a regression
        /// fails this test instead of hanging it.
        /// </summary>
        sealed class SilentBody : Stream
        {
            public bool ReadWasCancelled { get; private set; }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ReadWasCancelled = true;
                    throw;
                }
                return 0;
            }

            // Stream's byte[] overload otherwise routes through the synchronous Read below.
            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException("the body is read asynchronously");

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
        }
    }

    // An address literal from the documentation range, so building the endpoint
    // validates it without a name lookup; the handler above never connects to it.
    static Dhis2SessionClient ClientOver(HttpClient http) =>
        new(http, Dhis2Endpoint.Build("http://192.0.2.1:8080"),
            NullLogger<Dhis2SessionClient>.Instance);

    [Test]
    public async Task GetUserInfo_WhenDhis2DoesNotAnswerWithinTheTimeout_IsNotAuthenticated()
    {
        using var http = new HttpClient(new NeverAnsweringHandler())
        {
            Timeout = TimeSpan.FromMilliseconds(100),
        };

        var info = await ClientOver(http).GetUserInfoAsync("session", CancellationToken.None);

        Assert.That(info, Is.Null,
            "a DHIS2 that does not answer in time cannot vouch for the session");
    }

    [Test]
    public async Task GetUserInfo_WhenDhis2GoesQuietAfterTheHeaders_IsNotAuthenticated()
    {
        var handler = new HeadersThenSilenceHandler();
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(200),
        };

        var info = await ClientOver(http).GetUserInfoAsync("session", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(info, Is.Null,
                "a DHIS2 that stops after its headers cannot vouch for the session either");
            // The discriminating assertion: a null result alone does not distinguish the two,
            // because a body that ends empty fails to parse and returns null as well — only
            // minutes later, whenever DHIS2 chose to let go.
            Assert.That(handler.BodyReadWasCancelled, Is.True,
                "the client's timeout has to reach the body: streamed, it ends at the headers");
        });
    }

    [Test]
    public void GetUserInfo_WhenTheCallerCancels_Propagates()
    {
        using var http = new HttpClient(new NeverAnsweringHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(
            async () => await ClientOver(http).GetUserInfoAsync("session", cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>(),
            "the caller's own cancellation is not a DHIS2 outcome and must reach the caller");
    }
}
