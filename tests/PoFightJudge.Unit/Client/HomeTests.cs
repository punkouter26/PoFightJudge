using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Home as the hub: what has been argued, who is winning and how big the cast is, on the page you land on, with the
/// recent list searchable where it stands.
/// </summary>
public sealed class HomeTests : BunitContext
{
    private static readonly DateTimeOffset Night = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public HomeTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddSingleton(_api);
        Services.AddSingleton(TimeProvider.System);
        Services.AddScoped<Viewport>();
        Services.AddScoped<SimulationState>();
        Services.AddScoped<GfxInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _api.GetMatchPageAsync(Arg.Any<MatchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<MatchQuery>().Text is { Length: > 0 }
                ? new MatchPageDto([Match("the bins")], 1, 0, 4)
                : new MatchPageDto([Match("the thermostat"), Match("the bins")], 23, 0, 4));
        _api.GetRosterAsync(Arg.Any<CancellationToken>()).Returns(
        [
            FighterStatsDto.Empty("AB", "Alex") with { Fights = 3, Wins = 2, Losses = 1 },
            FighterStatsDto.Empty("CD", "Casey"),
        ]);
        _api.GetProfilesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Profile("MAH", ProfileRole.Husband), Profile("KSH", ProfileRole.Wife), Profile("GOR", ProfileRole.Husband),
        ]);
        _api.GetLeaderboardAsync(MatchMode.Watch, Arg.Any<CancellationToken>())
            .Returns([new LeaderboardRowDto("KSH", "Karen", 4, 3, 0.75, 70)]);
    }

    private static MatchDto Match(string topic)
    {
        var one = MatchSide.Persona("MAH", "Married Husband");
        var two = MatchSide.Persona("KSH", "Karen");
        return new(MatchId.New(), "u", MatchMode.Watch, Night, Night.AddMinutes(6), topic, one, two, SessionPhase.Done, SessionStatus.Ready, "Karen", "Close.", IsFake: false);
    }

    private static ProfileDto Profile(string initials, ProfileRole role) => new()
    {
        Id = ProfileId.From(initials),
        Persona = new CreateProfileRequest { Initials = initials, Name = initials, Role = role },
    };

    [Fact]
    public async Task The_tally_counts_the_arguments_the_fighters_and_the_cast()
    {
        var cut = Render<Home>();

        await cut.WaitForAssertionAsync(() => cut.Find(".tally").TextContent.Should()
            .Contain("23 arguments").And.Contain("2 fighters").And.Contain("3 in the cast"));
    }

    [Fact]
    public async Task Searching_the_recent_list_asks_the_server_and_shows_its_answer()
    {
        var cut = Render<Home>();
        await cut.WaitForAssertionAsync(() => cut.FindAll(".replay-list li").Should().HaveCount(2));

        await cut.Find(".recent-find input").ChangeAsync(new() { Value = "bins" });

        await cut.WaitForAssertionAsync(() => cut.FindAll(".replay-list li").Should().ContainSingle());
        await _api.Received().GetMatchPageAsync(Arg.Is<MatchQuery>(q => q.Text == "bins"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_standings_switch_between_the_fighters_and_the_cast()
    {
        var cut = Render<Home>();
        await cut.WaitForAssertionAsync(() => cut.Find(".fighter-list").TextContent.Should().Contain("AB"));

        await cut.FindAll(".standings .rz-button").Single(b => b.TextContent.Contains("Cast", StringComparison.Ordinal)).ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Find(".fighter-list").TextContent.Should().Contain("Karen"));
    }

    [Fact]
    public async Task Home_does_not_say_nobody_is_on_the_card_when_people_are_but_have_no_judged_fight()
    {
        _api.GetRosterAsync(Arg.Any<CancellationToken>()).Returns([FighterStatsDto.Empty("AB", "Alex")]);

        var cut = Render<Home>();

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("No judged fights yet"));
        cut.Markup.Should().NotContain("Nobody on the card");
    }
}
