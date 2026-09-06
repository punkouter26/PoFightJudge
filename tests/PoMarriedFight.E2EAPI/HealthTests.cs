using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Configuration;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.E2EAPI;

[Collection(ApiCollection.Name)]
public partial class HealthTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_probes_answer_200_without_touching_the_network()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync(ApiRoutes.Health.Url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(ApiRoutes.Health.ProbeUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(ApiRoutes.Health.ReadyProbeUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_details_are_anonymous_masked_and_complete()
    {
        using var client = factory.CreateClient();

        var report = await client.GetFromJsonAsync<HealthReportDto>(ApiRoutes.Health.DetailsUrl);

        report!.Environment.Should().Be(ApiFactory.Environment);
        report.Checks.Should().Contain(c => c.Name == "configuration" && c.Category == HealthCategory.Connection);
        report.Checks.Should().Contain(c => c.Name == "Gemini API key" && c.State == HealthState.Ok && c.Detail == "****", "the test key is short enough to be fully masked");
        report.Checks.Where(c => c.Category == HealthCategory.Feature).Select(c => c.Name).Should().BeEquivalentTo(Flags.All.Select(f => $"Feature: {f}"));
        report.Checks.Should().OnlyContain(c => !c.Detail.Contains("test-key"));
    }

    [Fact]
    public async Task Diag_requires_authentication_and_never_leaks_a_value()
    {
        // Anonymous → 401
        {
            using var client = factory.CreateClient();
            (await client.GetAsync(ApiRoutes.Diag.Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // Signed in (Test environment, no admin requirement) → presence only
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(FakeAuthOptions.UserHeader, "diag-user");
            var response = await client.GetAsync(ApiRoutes.Diag.Url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain("test-key").And.NotContain("AccountKey=");
            SecretLikePattern().IsMatch(body).Should().BeFalse("diag must never include key-like strings");

            var dto = await response.Content.ReadFromJsonAsync<DiagDto>();
            dto!.Status.Should().Be("ok");
            dto.Gemini.Should().Be(DiagDto.Configured);
            dto.FakeAi.Should().BeTrue("the test host keeps a placeholder key but forces the fakes with the flag");
            dto.Ready.Should().BeTrue();
            dto.Models.Live.Should().Be("gemini-3.1-flash-live-preview");
            dto.Models.Round.Should().Be("gemini-3.1-flash-lite");
            dto.Models.Tts.Should().Be("gemini-3.1-flash-tts-preview");
            dto.Flags.Keys.Should().BeEquivalentTo(Flags.All);
        }
    }

    [Fact]
    public async Task Features_are_anonymous_and_apply_the_environment_rules()
    {
        using var client = factory.CreateClient();

        var flags = await client.GetFromJsonAsync<FeatureFlagsDto>(ApiRoutes.Features.Url);

        flags!.UseFakeAi.Should().BeTrue("the test host forces the fakes");
        flags.SelfPlayer.Should().BeTrue("the fake transcriber can finish a spoken turn, so the SELF player is offered");
        flags.SelfPlayer.Should().BeTrue();
        flags.BrowserSpeechRecognition.Should().BeTrue();
    }

    [Fact]
    public async Task Every_response_carries_the_security_envelope_including_a_404()
    {
        using var client = factory.CreateClient();

        var ok = await client.GetAsync(ApiRoutes.Health.Url);
        ok.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");
        ok.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        ok.Headers.GetValues("Permissions-Policy").Single().Should().Contain("microphone=(self)");

        var missing = await client.GetAsync($"{ApiRoutes.ApiPrefix}/does-not-exist");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        missing.Headers.Contains("Content-Security-Policy").Should().BeTrue();

        // A missing static file is a 404, not an auth challenge (the SPA fallback for client routes is covered by E2EUI,
        // where the static web assets exist).
        (await client.GetAsync("/js/does-not-exist.js")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [GeneratedRegex("AIza[0-9A-Za-z_-]{20,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretLikePattern();
}
