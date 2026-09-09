using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Client.Components;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// What a caption costs to draw. Gemini Live rewrites the line being spoken several times a second, and every one
/// of those fragments used to redraw the whole live page — the phase banner, the timer, the host indicator, the
/// scoreboard and every caption already in the log — on a phone, mid-fight, with a microphone open.
/// </summary>
public class LiveRenderCostTests : BunitContext
{
    public LiveRenderCostTests()
    {
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void A_caption_that_grows_redraws_the_feed_and_nothing_above_it()
    {
        var log = new CaptionLog();
        var feed = Render<CaptionFeed>(p => p.Add(c => c.Log, log));
        var banner = Render<PhaseBanner>(p => p.Add(b => b.Phase, SessionPhase.Debate));
        var timer = Render<DebateTimer>(p => p.Add(t => t.ElapsedSeconds, 10).Add(t => t.RemainingSeconds, 50));

        var bannerBefore = banner.RenderCount;
        var timerBefore = timer.RenderCount;

        // One sentence arriving the way the live API sends it: the same line, rewritten as it grows.
        foreach (var text in new[] { "you", "you never", "you never load", "you never load the dishwasher" })
        {
            log.Add(new CaptionDto(Speaker.Player1, text, Final: false));
        }

        feed.WaitForAssertion(() => feed.Markup.Should().Contain("you never load the dishwasher"));
        feed.FindAll("article.caption").Should().HaveCount(1, "a line being rewritten is one line, not four");
        banner.RenderCount.Should().Be(bannerBefore, "nothing about the phase changed");
        timer.RenderCount.Should().Be(timerBefore, "nothing about the clock changed");
    }

    [Fact]
    public void The_chrome_redraws_only_when_what_it_shows_has_changed()
    {
        var timer = Render<DebateTimer>(p => p.Add(t => t.ElapsedSeconds, 10).Add(t => t.RemainingSeconds, 50));
        var before = timer.RenderCount;

        // The same second, published again — a snapshot arrives on its own cadence and mostly says nothing new.
        timer.Render(p => p.Add(t => t.ElapsedSeconds, 10).Add(t => t.RemainingSeconds, 50));
        timer.RenderCount.Should().Be(before, "the numbers on screen are the numbers it was already showing");

        timer.Render(p => p.Add(t => t.ElapsedSeconds, 11).Add(t => t.RemainingSeconds, 49));
        timer.RenderCount.Should().BeGreaterThan(before, "a second passed");
        timer.Find(".left").TextContent.Should().Be("0:49");
    }

    [Fact]
    public void A_scoreboard_ignores_a_snapshot_that_says_nothing_it_shows()
    {
        var board = Render<ScoreboardBanner>(p => p
            .Add(b => b.Player1, "AB")
            .Add(b => b.Player2, "CD")
            .Add(b => b.Speaking, Speaker.Player1));
        var before = board.RenderCount;

        board.Render(p => p
            .Add(b => b.Player1, "AB")
            .Add(b => b.Player2, "CD")
            .Add(b => b.Speaking, Speaker.Player1));
        board.RenderCount.Should().Be(before);

        board.Render(p => p
            .Add(b => b.Player1, "AB")
            .Add(b => b.Player2, "CD")
            .Add(b => b.Speaking, Speaker.Player2));
        board.RenderCount.Should().BeGreaterThan(before, "the floor changed hands");
    }
}
