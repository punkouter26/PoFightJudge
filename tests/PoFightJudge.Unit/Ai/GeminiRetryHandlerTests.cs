using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Ai;

public class GeminiRetryHandlerTests
{
    private readonly FakeHttpHandler _inner = new();
    private readonly FakeTimeProvider _clock = new();

    /// <summary>The handler waits on the fake clock, so every delay has to be advanced past for the call to finish.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var sut = new GeminiRetryHandler(_clock, NullLogger<GeminiRetryHandler>.Instance) { InnerHandler = _inner };
#pragma warning disable RS0030 // Banned API — a delegating handler under test needs a client built over it.
        using var client = new HttpClient(sut, disposeHandler: false) { BaseAddress = new Uri("https://gemini.test/") };
#pragma warning restore RS0030
        var send = client.SendAsync(request);

        // Nothing progresses until the clock moves — but the continuation needs a real moment to register its timer.
        for (var spins = 0; !send.IsCompleted && spins < 200; spins++)
        {
            _clock.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(5);
        }

        send.IsCompleted.Should().BeTrue("the handler should not be waiting on anything but the fake clock");
        return await send;
    }

    [Fact]
    public async Task Retries_a_throttled_call_with_its_body_honours_retry_after_and_neither_retries_a_client_error_nor_passes_the_cap()
    {
        _inner.Enqueue(HttpStatusCode.TooManyRequests, "slow down");
        _inner.Enqueue(HttpStatusCode.OK, """{"ok":true}""");
        using var first = new HttpRequestMessage(HttpMethod.Post, "v1beta/x") { Content = new StringContent("body") };

        using var response = await SendAsync(first);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _inner.Requests.Should().HaveCount(2);
        _inner.Requests.Select(r => r.Body).Should().AllBe("body", "the retry must resend the original payload");

        _inner.Enqueue((_, _) =>
        {
            var throttled = FakeHttpHandler.Json(HttpStatusCode.TooManyRequests, "wait");
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return throttled;
        });
        _inner.Enqueue(HttpStatusCode.OK, "{}");
        using var second = new HttpRequestMessage(HttpMethod.Post, "v1beta/x");

        using var honoured = await SendAsync(second);

        honoured.StatusCode.Should().Be(HttpStatusCode.OK);
        _inner.Requests.Should().HaveCount(4);

        // A client error is not retried, and the cap hands back the last failure.
        _inner.Enqueue(HttpStatusCode.BadRequest, "malformed");
        using var bad = new HttpRequestMessage(HttpMethod.Post, "v1beta/x");

        using var rejected = await SendAsync(bad);

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _inner.Requests.Should().HaveCount(4 + 1, "the bad request is sent once and only once");

        for (var i = 0; i < GeminiRetryHandler.MaxAttempts; i++)
        {
            _inner.Enqueue(HttpStatusCode.ServiceUnavailable, "nope");
        }

        using var down = new HttpRequestMessage(HttpMethod.Post, "v1beta/x");
        using var gaveUp = await SendAsync(down);

        gaveUp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _inner.Requests.Should().HaveCount(4 + 1 + GeminiRetryHandler.MaxAttempts);
    }
}
