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
    public async Task Retries_a_throttled_call_resends_the_body_and_honours_retry_after()
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
    }

    [Fact]
    public async Task A_client_error_is_not_retried_and_the_cap_hands_back_the_last_failure()
    {
        _inner.Enqueue(HttpStatusCode.BadRequest, "malformed");
        using var bad = new HttpRequestMessage(HttpMethod.Post, "v1beta/x");

        using var rejected = await SendAsync(bad);

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _inner.Requests.Should().ContainSingle();

        for (var i = 0; i < GeminiRetryHandler.MaxAttempts; i++)
        {
            _inner.Enqueue(HttpStatusCode.ServiceUnavailable, "nope");
        }

        using var down = new HttpRequestMessage(HttpMethod.Post, "v1beta/x");
        using var gaveUp = await SendAsync(down);

        gaveUp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _inner.Requests.Should().HaveCount(1 + GeminiRetryHandler.MaxAttempts);
    }

    [Fact]
    public async Task An_upload_leg_is_left_alone_so_finalize_is_never_replayed()
    {
        _inner.Enqueue(HttpStatusCode.ServiceUnavailable, "nope");
        using var request = new HttpRequestMessage(HttpMethod.Post, "upload/v1beta/files");
        request.Headers.Add("X-Goog-Upload-Command", "upload, finalize");

        using var response = await SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _inner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A_model_under_load_is_given_time_rather_than_hammered()
    {
        // "High demand, try again later" answered by a handler that waits milliseconds loses the analysis. Nobody
        // is waiting on this call in real time, so the wait is measured in seconds.
        _inner.Enqueue(HttpStatusCode.ServiceUnavailable, "high demand");
        _inner.Enqueue(HttpStatusCode.OK, """{"ok":true}""");

        using var sut = new GeminiRetryHandler(_clock, NullLogger<GeminiRetryHandler>.Instance) { InnerHandler = _inner };
#pragma warning disable RS0030 // Banned API — a delegating handler under test needs a client built over it.
        using var client = new HttpClient(sut, disposeHandler: false) { BaseAddress = new Uri("https://gemini.test/") };
#pragma warning restore RS0030
        var request = new HttpRequestMessage(HttpMethod.Post, "v1beta/judge");
        var send = client.SendAsync(request);

        // Advance in small steps and count what the wait actually cost: the delay is the thing under test.
        var advanced = TimeSpan.Zero;
        var step = TimeSpan.FromMilliseconds(250);
        for (var spins = 0; !send.IsCompleted && spins < 400; spins++)
        {
            await Task.Delay(5);
            if (!send.IsCompleted)
            {
                _clock.Advance(step);
                advanced += step;
            }
        }

        (await send).StatusCode.Should().Be(HttpStatusCode.OK);
        _inner.Requests.Should().HaveCount(2);
        advanced.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1),
            "a busy model is given a real pause — jitter spreads the crowd, it does not excuse retrying immediately");
        request.Dispose();
    }
}
