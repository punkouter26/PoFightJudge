using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoMarriedFight.Client.Components;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;
using Radzen;

namespace PoMarriedFight.Unit.Client;

/// <summary>The pieces of the live stage: where the fight is, how long is left, who is talking, and what was said.</summary>
public class FightLiveComponentTests : BunitContext
{
    public FightLiveComponentTests()
    {
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(SessionPhase.Intro, "Getting started")]
    [InlineData(SessionPhase.Debate, "Arguing")]
    [InlineData(SessionPhase.Probe, "Questions")]
    [InlineData(SessionPhase.Verdict, "The ruling")]
    [InlineData(SessionPhase.Done, "Finished")]
    public void Every_phase_is_said_in_words_rather_than_shown_as_an_enum(SessionPhase phase, string expected)
    {
        var cut = Render<PhaseBanner>(p => p.Add(b => b.Phase, phase));

        cut.Find(".phase").TextContent.Should().Contain(expected);
        cut.Find(".phase").GetAttribute("data-phase").Should().Be(phase.ToString().ToLowerInvariant());
    }

    [Fact]
    public void The_agreed_topic_is_named_while_the_room_is_waiting_to_start()
    {
        var cut = Render<PhaseBanner>(p => p
            .Add(b => b.Phase, SessionPhase.Setup)
            .Add(b => b.Topic, "who does the dishes"));

        cut.Markup.Should().Contain("who does the dishes");
    }

    [Fact]
    public void The_timer_counts_down_because_that_is_the_number_that_changes_behaviour()
    {
        var cut = Render<DebateTimer>(p => p
            .Add(t => t.ElapsedSeconds, 60)
            .Add(t => t.RemainingSeconds, 120));

        cut.Find(".left").TextContent.Should().Be("2:00");
        cut.Find(".timer").GetAttribute("aria-label").Should().Contain("120 seconds");
    }

    [Fact]
    public void The_last_seconds_are_shown_as_urgent()
    {
        var cut = Render<DebateTimer>(p => p
            .Add(t => t.ElapsedSeconds, 175)
            .Add(t => t.RemainingSeconds, 5));

        cut.Find(".left").TextContent.Should().Be("0:05");
        cut.FindComponent<Radzen.Blazor.RadzenProgressBar>().Instance.ProgressBarStyle.Should()
            .Be(ProgressBarStyle.Danger, "a bar that stays calm at five seconds tells nobody anything");
    }

    [Fact]
    public void The_host_is_named_and_shows_whether_it_is_talking()
    {
        var listening = Render<HostIndicator>(p => p.Add(h => h.Persona, HostPersonaId.Roastmaster));
        listening.Find(".host").ClassList.Should().NotContain("host--speaking");
        listening.Markup.Should().Contain("Roastmaster").And.Contain("listening");

        var speaking = Render<HostIndicator>(p => p
            .Add(h => h.Persona, HostPersonaId.Referee)
            .Add(h => h.Speaking, true));
        speaking.Find(".host").ClassList.Should().Contain("host--speaking");
        speaking.Markup.Should().Contain("The Referee").And.Contain("speaking");
    }

    [Fact]
    public void An_empty_caption_feed_says_so_rather_than_showing_nothing()
    {
        var cut = Render<CaptionFeed>(p => p.Add(f => f.Log, new CaptionLog()));

        cut.Find("section.captions").GetAttribute("role").Should().Be("log");
        cut.Markup.Should().Contain("Nothing said yet");
    }

    [Fact]
    public void Captions_are_labelled_with_the_tag_that_said_them()
    {
        var log = new CaptionLog();
        log.Add(new CaptionDto(Speaker.Player1, "you never listen", true));
        log.Add(new CaptionDto(Speaker.Host, "one at a time", true));

        var cut = Render<CaptionFeed>(p => p
            .Add(f => f.Log, log)
            .Add(f => f.Player1Name, "AB")
            .Add(f => f.HostName, "The Referee"));

        var lines = cut.FindAll("article.caption");
        lines.Should().HaveCount(2);
        lines[0].TextContent.Should().Contain("AB").And.Contain("you never listen");
        lines[0].ClassList.Should().Contain("p1");
        lines[1].TextContent.Should().Contain("The Referee");
        lines[1].ClassList.Should().Contain("host");
    }

    [Fact]
    public void A_line_still_being_spoken_is_marked_as_such()
    {
        var log = new CaptionLog();
        log.Add(new CaptionDto(Speaker.Player2, "I did not", false));

        var cut = Render<CaptionFeed>(p => p.Add(f => f.Log, log));

        cut.Find("article.caption").ClassList.Should().Contain("caption--live");
    }
}

/// <summary>
/// A caption arrives in pieces and is rewritten as it grows, so the log has to replace rather than append — or a
/// sentence would appear a fragment at a time, each one a line of its own.
/// </summary>
public class CaptionLogTests
{
    [Fact]
    public void A_growing_sentence_replaces_itself_rather_than_stacking_up()
    {
        var log = new CaptionLog();

        log.Add(new CaptionDto(Speaker.Player1, "you", false));
        log.Add(new CaptionDto(Speaker.Player1, "you never", false));
        log.Add(new CaptionDto(Speaker.Player1, "you never listen", true));

        log.Lines.Should().ContainSingle().Which.Text.Should().Be("you never listen");
        log.Lines[0].Final.Should().BeTrue();
    }

    [Fact]
    public void A_finished_line_stays_and_the_next_one_starts_fresh()
    {
        var log = new CaptionLog();

        log.Add(new CaptionDto(Speaker.Player1, "you never listen", true));
        log.Add(new CaptionDto(Speaker.Player1, "and", false));

        log.Lines.Should().HaveCount(2, "the first sentence was finished, so the second is a new line");
    }

    [Fact]
    public void Somebody_cutting_in_starts_their_own_line()
    {
        var log = new CaptionLog();

        log.Add(new CaptionDto(Speaker.Player1, "you never", false));
        log.Add(new CaptionDto(Speaker.Host, "one at a time", false));

        log.Lines.Should().HaveCount(2);
        log.Lines[0].Speaker.Should().Be(Speaker.Player1, "the interrupted line is left where it was");
    }

    /// <summary>
    /// A real fight on 2026-09-07 printed the host's greeting twice and one player's line twice. Live sessions
    /// interleave: the host's transcript grows while a player is still being transcribed, so the line to extend is
    /// not always the last one in the log.
    /// </summary>
    [Fact]
    public void A_line_that_grows_while_somebody_else_is_talking_still_grows_rather_than_repeating()
    {
        var log = new CaptionLog();

        log.Add(new CaptionDto(Speaker.Host, "Al and SM,", false));
        log.Add(new CaptionDto(Speaker.Player1, "Hi Puck.", false));
        log.Add(new CaptionDto(Speaker.Host, "Al and SM, ready to settle this?", false));
        log.Add(new CaptionDto(Speaker.Player1, "Hi Puck. I am Alex.", true));

        log.Lines.Should().HaveCount(2, "two people spoke, so there are two lines");
        log.Lines[0].Text.Should().Be("Al and SM, ready to settle this?");
        log.Lines[1].Text.Should().Be("Hi Puck. I am Alex.");
    }

    [Fact]
    public void A_finished_line_is_never_reopened_by_what_somebody_says_next()
    {
        var log = new CaptionLog();

        log.Add(new CaptionDto(Speaker.Player1, "you never listen", true));
        log.Add(new CaptionDto(Speaker.Host, "one at a time", false));
        log.Add(new CaptionDto(Speaker.Player1, "and another thing", false));

        log.Lines.Should().HaveCount(3);
        log.Lines[0].Text.Should().Be("you never listen");
    }

    [Fact]
    public void Empty_captions_are_not_lines()
    {
        var log = new CaptionLog();

        log.Add(new CaptionDto(Speaker.Player1, "   ", true));
        log.Add(new CaptionDto(Speaker.Player1, string.Empty, false));

        log.Lines.Should().BeEmpty();
    }

    [Fact]
    public void A_long_fight_does_not_grow_the_page_without_limit()
    {
        var log = new CaptionLog();

        for (var i = 0; i < CaptionLog.MaxLines + 40; i++)
        {
            log.Add(new CaptionDto(Speaker.Player1, $"line {i}", true));
        }

        log.Lines.Should().HaveCount(CaptionLog.MaxLines);
        log.Lines[^1].Text.Should().Be($"line {CaptionLog.MaxLines + 39}", "it is the oldest lines that fall off");
    }
}
