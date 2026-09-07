using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoMarriedFight.Client.Components;
using PoMarriedFight.Shared.Models;
using Radzen;

namespace PoMarriedFight.Unit.Client;

/// <summary>The bar above a live fight: who is arguing, what they have done before, and whether we can hear them.</summary>
public class FightHudTests : BunitContext
{
    public FightHudTests()
    {
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void A_closed_microphone_says_so_rather_than_looking_the_same_as_an_open_one()
    {
        var off = Render<LevelMeter>();
        off.Find(".meter").ClassList.Should().NotContain("meter--on");
        off.Find(".meter").GetAttribute("aria-label").Should().Contain("off");

        var on = Render<LevelMeter>(p => p.Add(m => m.Listening, true).Add(m => m.Caption, "AL speaking"));
        on.Find(".meter").ClassList.Should().Contain("meter--on");
        on.Find(".meter").GetAttribute("aria-label").Should().Contain("open");
        on.Markup.Should().Contain("AL speaking", "the caption says whose voice it is showing");
    }

    [Fact]
    public void The_scoreboard_names_both_tags_and_says_which_of_them_is_new()
    {
        var known = new FighterDto("AL", "Alex", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(2));

        var cut = Render<ScoreboardBanner>(p => p
            .Add(s => s.Player1, "AL")
            .Add(s => s.Player2, "SM")
            .Add(s => s.Player1Record, known));

        cut.Find(".side[data-tag=AL]").TextContent.Should().Contain("Alex");
        cut.Find(".side[data-tag=SM]").TextContent.Should().Contain("First fight");
    }

    [Fact]
    public void Whoever_has_the_floor_is_the_one_in_focus()
    {
        var cut = Render<ScoreboardBanner>(p => p
            .Add(s => s.Player1, "AL")
            .Add(s => s.Player2, "SM")
            .Add(s => s.Speaking, Speaker.Player2));

        cut.Find(".side[data-tag=SM]").ClassList.Should().Contain("side--speaking");
        cut.Find(".side[data-tag=SM]").TextContent.Should().Contain("Your turn — talk", "the one with the floor is told to talk");
        cut.Find(".side[data-tag=AL]").ClassList.Should().NotContain("side--speaking");
        cut.Find(".side[data-tag=AL]").TextContent.Should().Contain("SM has the floor",
            "the one waiting is told whose turn it is, not merely that it is not theirs");
        cut.FindComponent<LevelMeter>().Instance.Caption.Should().Be("SM speaking");
    }

    [Fact]
    public void While_the_host_is_talking_the_microphone_meter_is_put_away()
    {
        // The host cutting in does not change whose turn it is, so the snapshot still names a speaker; but a meter
        // twitching under a fighter's name while the host talks reads as "keep talking", which is the opposite of
        // what is wanted. So the meter goes, and both sides are told who has the floor.
        var cut = Render<ScoreboardBanner>(p => p
            .Add(s => s.Player1, "AL")
            .Add(s => s.Player2, "SM")
            .Add(s => s.Speaking, Speaker.Player2)
            .Add(s => s.Listening, true)
            .Add(s => s.HostSpeaking, true));

        cut.FindComponents<LevelMeter>().Should().BeEmpty("nobody should be encouraged to talk over the host");
        cut.FindAll(".side--speaking").Should().BeEmpty();
        cut.Find(".side[data-tag=SM]").TextContent.Should().NotContain("Your turn");
        cut.Find(".middle").TextContent.Should().Contain("host has the floor");
    }

    [Fact]
    public void Before_anybody_has_the_floor_neither_of_them_is_shown_as_talking()
    {
        var cut = Render<ScoreboardBanner>(p => p.Add(s => s.Player1, "AL").Add(s => s.Player2, "SM"));

        cut.FindAll(".side--speaking").Should().BeEmpty();
    }
}
