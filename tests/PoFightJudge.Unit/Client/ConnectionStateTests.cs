using System.Net;
using PoFightJudge.Client.Services;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Whether the API is answering. Nothing polls for it: every call the app already makes reports what happened, so
/// the banner is right on the first failure rather than up to a heartbeat either side of the truth.
/// </summary>
public class ConnectionStateTests
{
    [Fact]
    public void It_starts_out_believing_the_server_is_there()
    {
        var state = new ConnectionState();

        state.IsReachable.Should().BeTrue("a banner on the first paint, before anything has been asked, would be a lie");
        state.IsDegraded.Should().BeFalse();
    }

    /// <summary>One request lost to a flaky connection is not worth a banner; the retry after it usually works.</summary>
    [Fact]
    public void One_failure_is_not_enough_to_say_the_server_is_down()
    {
        var state = new ConnectionState();

        state.ReportFailure();

        state.IsReachable.Should().BeTrue();
    }

    [Fact]
    public void Enough_failures_in_a_row_are()
    {
        var state = new ConnectionState();
        var changes = 0;
        state.Changed += (_, _) => changes++;

        for (var i = 0; i < ConnectionState.FailuresBeforeSaying; i++)
        {
            state.ReportFailure();
        }

        state.IsReachable.Should().BeFalse();
        changes.Should().Be(1, "the answer changed once, however many failures it took");
    }

    [Fact]
    public void One_success_is_enough_to_take_it_back()
    {
        var state = new ConnectionState();
        for (var i = 0; i < ConnectionState.FailuresBeforeSaying; i++)
        {
            state.ReportFailure();
        }

        state.ReportSuccess();

        state.IsReachable.Should().BeTrue();
    }

    [Fact]
    public void A_run_of_failures_broken_by_a_success_starts_counting_again()
    {
        var state = new ConnectionState();

        state.ReportFailure();
        state.ReportSuccess();
        state.ReportFailure();

        state.IsReachable.Should().BeTrue("that is one failure since the last success, not two");
    }

    [Fact]
    public void Nothing_is_raised_when_the_answer_has_not_changed()
    {
        var state = new ConnectionState();
        var changes = 0;
        state.Changed += (_, _) => changes++;

        state.ReportSuccess();
        state.ReportSuccess();

        changes.Should().Be(0);
    }

    /// <summary>
    /// A 503 is the degraded gate in Program.cs — the server is up and refusing to work. Everything else that came
    /// back at all is the server working: a 404, a 429 and a validation problem are all answers.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task What_came_back_decides_whether_the_server_is_degraded(HttpStatusCode status, bool degraded)
    {
        var state = new ConnectionState();
        using var inner = new FakeHttpHandler { Fallback = (_, _) => new HttpResponseMessage(status) };
        using var watch = new ConnectionWatchHandler(state) { InnerHandler = inner };
#pragma warning disable RS0030 // Banned API — a delegating handler under test needs a client built over it.
        using var client = new HttpClient(watch, disposeHandler: false) { BaseAddress = new Uri("https://unit.test/") };
#pragma warning restore RS0030

        using var response = await client.GetAsync(new Uri("api/health", UriKind.Relative));

        state.IsDegraded.Should().Be(degraded);
        state.IsReachable.Should().BeTrue("something answered");
    }

    [Fact]
    public async Task Nothing_answering_at_all_is_what_the_banner_is_for()
    {
        var state = new ConnectionState();
        using var inner = new FakeHttpHandler { Fallback = (_, _) => throw new HttpRequestException("no route to host") };
        using var watch = new ConnectionWatchHandler(state) { InnerHandler = inner };
#pragma warning disable RS0030 // Banned API — a delegating handler under test needs a client built over it.
        using var client = new HttpClient(watch, disposeHandler: false) { BaseAddress = new Uri("https://unit.test/") };
#pragma warning restore RS0030

        for (var i = 0; i < ConnectionState.FailuresBeforeSaying; i++)
        {
            await client.Invoking(c => c.GetAsync(new Uri("api/health", UriKind.Relative)))
                .Should().ThrowAsync<HttpRequestException>();
        }

        state.IsReachable.Should().BeFalse();
    }
}
