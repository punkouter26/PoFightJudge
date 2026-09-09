using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

public class HealthPageTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public HealthPageTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        // Under bunit JS is loose, so the viewport reports the wide layout — the one that declares every column.
        Services.AddScoped<Viewport>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task Health_page_groups_checks_by_category_and_colours_their_state()
    {
        _api.GetHealthDetailsAsync(Arg.Any<CancellationToken>()).Returns(new HealthReportDto("Development", HealthState.Failed, DateTimeOffset.UnixEpoch,
        [
            new HealthCheckDto("storage", HealthState.Failed, "Storage unreachable. Locally: ./SCRIPTS/azurite.ps1", HealthCategory.Connection),
            new HealthCheckDto("Gemini API key", HealthState.NotConfigured, "not set", HealthCategory.Configuration),
            new HealthCheckDto("Feature: UseFakeAi", HealthState.Ok, "on", HealthCategory.Feature),
        ]));

        var cut = Render<Health>();

        await cut.WaitForAssertionAsync(() => cut.FindAll("h2").Should().HaveCount(3));
        cut.FindAll("h2").Select(h => h.TextContent).Should().Equal("Connection", "Configuration", "Feature");
        cut.Markup.Should().Contain("azurite.ps1").And.Contain("not set");
        cut.FindAll(".rz-badge-danger").Should().HaveCountGreaterThanOrEqualTo(2, "the overall badge and the failed storage row");
    }

    [Fact]
    public async Task Health_page_says_when_the_api_is_down()
    {
        _api.GetHealthDetailsAsync(Arg.Any<CancellationToken>()).Returns<HealthReportDto?>(_ => throw new HttpRequestException("connection refused"));

        var cut = Render<Health>();

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("The API did not answer"));
    }
}
