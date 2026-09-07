using System.Diagnostics;
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
