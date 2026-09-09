using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Client;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// In Production the <see cref="HttpClients.Api"/> client carries an MSAL token handler that throws when no one is
/// signed in. The feature flags are read on the sign-in page, before anyone is, so that one call has to travel on the
/// <see cref="HttpClients.Anonymous"/> client instead. Everything else stays on the authorized one.
/// </summary>
public class ApiClientTests
{
    [Fact]
    public async Task Feature_flags_travel_on_the_anonymous_client_and_everything_else_on_the_authorized_one()
    {
        var requests = new List<(string Client, string Path)>();
        var services = new ServiceCollection();
        foreach (var name in new[] { HttpClients.Api, HttpClients.Anonymous })
        {
            services.AddHttpClient(name, c => c.BaseAddress = new Uri("https://unit.test/"))
                .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(name, requests));
        }

        await using var provider = services.BuildServiceProvider();
        var api = new ApiClient(provider.GetRequiredService<IHttpClientFactory>());

        var flags = await api.GetFeaturesAsync();
        await api.GetHealthDetailsAsync();

        flags.UseFakeAi.Should().BeTrue();
        requests.Should().Equal(
            (HttpClients.Anonymous, ApiRoutes.Features.Url),
            (HttpClients.Api, ApiRoutes.Health.DetailsUrl));
    }

    private sealed class RecordingHandler(string name, List<(string, string)> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add((name, path));
            var body = string.Equals(path, ApiRoutes.Features.Url, StringComparison.Ordinal)
                ? JsonContent.Create(new FeatureFlagsDto(true, false, false, DevEntraEnabled: false))
                : JsonContent.Create(new HealthReportDto("Test", HealthState.Ok, DateTimeOffset.UnixEpoch, []));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }

    /// <summary>
    /// The AI routes are limited per user, and SPEC §11 promises the client turns that into a wait rather than a
    /// dead button. Nothing read the header before this, so a throttle surfaced as "The API answered 429".
    /// </summary>
    [Fact]
    public async Task A_throttle_is_read_as_a_wait_rather_than_as_a_fault()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));

        var thrown = await ApiException.FromAsync(response, CancellationToken.None);

        thrown.Status.Should().Be(429);
        thrown.IsThrottled.Should().BeTrue();
        thrown.RetryAfter.Should().Be(TimeSpan.FromSeconds(42));
        thrown.Summary.Should().NotContain("429", "a status line is not something to show anybody");
    }

    [Fact]
    public async Task A_throttle_with_nothing_to_say_still_reads_as_a_throttle()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var thrown = await ApiException.FromAsync(response, CancellationToken.None);

        thrown.IsThrottled.Should().BeTrue();
        thrown.RetryAfter.Should().BeNull("the page falls back to a window of its own rather than inventing one here");
    }

    [Fact]
    public async Task Every_other_failure_carries_no_wait()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var thrown = await ApiException.FromAsync(response, CancellationToken.None);

        thrown.IsThrottled.Should().BeFalse();
        thrown.RetryAfter.Should().BeNull();
    }
}
