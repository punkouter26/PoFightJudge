using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The broadcast layer of a live fight: what each phase sounds like, how hot the backdrop runs as the clock goes,
/// and the chrome that sits over it. The shader itself is a browser's business — what is decided here is the two
/// numbers it is driven by, and those are worth pinning down.
/// </summary>
public class LiveStageTests : BunitContext
{
    public LiveStageTests()
    {
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// One sound per phase, and deliberately none for the intro: the host is already talking over it, and a sting
    /// under somebody's opening line is the app interrupting itself.
    /// </summary>
    [Theory]
    [InlineData(SessionPhase.Intro, null)]
    [InlineData(SessionPhase.Setup, Sfx.Intro)]
    [InlineData(SessionPhase.Debate, Sfx.Bell)]
    [InlineData(SessionPhase.Probe, Sfx.Probe)]
    [InlineData(SessionPhase.Verdict, Sfx.Ruling)]
    [InlineData(SessionPhase.Done, Sfx.BellThree)]
    public void Every_phase_of_a_fight_has_its_own_sound(SessionPhase phase, string? expected) =>
        FightLive.StingFor(phase).Should().Be(expected);

    [Fact]
    public void The_backdrop_is_cool_while_there_is_still_time_and_climbs_through_the_last_half_minute()
    {
        FightLive.HeatFor(SessionPhase.Debate, 120).Should().Be(0, "two minutes left is not urgent");
        FightLive.HeatFor(SessionPhase.Debate, 30).Should().Be(0, "the climb starts here");
        FightLive.HeatFor(SessionPhase.Debate, 15).Should().BeApproximately(0.5, 0.001);
        FightLive.HeatFor(SessionPhase.Debate, 1).Should().BeGreaterThan(0.9, "the clock is about to run out");
    }

    /// <summary>Time ran out rather than running down: the debate is over, so the countdown stops driving anything.</summary>
    [Fact]
    public void A_clock_that_has_run_out_stops_heating_the_backdrop()
    {
        FightLive.HeatFor(SessionPhase.Debate, 0).Should().Be(0);
        FightLive.HeatFor(SessionPhase.Intro, 0).Should().Be(0);
    }

    [Theory]
    [InlineData(SessionPhase.Probe)]
    [InlineData(SessionPhase.Verdict)]
    public void Being_questioned_and_being_judged_are_warm_but_not_a_countdown(SessionPhase phase) =>
        FightLive.HeatFor(phase, 0).Should().Be(0.5);

    /// <summary>
    /// The chrome of a live fight is glass over the shader. The class carries an opaque floor as well as the blur,
    /// so a browser without backdrop-filter loses the effect and keeps the contrast.
    /// </summary>
    [Fact]
    public void The_live_chrome_is_glass()
    {
        Render<PhaseBanner>(p => p.Add(b => b.Phase, SessionPhase.Debate))
            .Find(".phase").ClassList.Should().Contain("po-glass");

        Render<HostIndicator>(p => p.Add(h => h.Persona, HostPersonaId.Referee))
            .Find(".host").ClassList.Should().Contain("po-glass");

        var scoreboard = Render<ScoreboardBanner>(p => p.Add(s => s.Player1, "AB").Add(s => s.Player2, "CD"));
        scoreboard.FindAll(".side.po-glass").Should().HaveCount(2, "both fighters sit on the same glass");
    }

    /// <summary>
    /// Eight bars, and the same eight whichever recorder is running: a level says how loud, a shape says it is a
    /// voice rather than the fan. They are painted from custom properties, so there is nothing here to push.
    /// </summary>
    [Fact]
    public void The_meter_draws_a_spectrum_as_well_as_a_level()
    {
        var cut = Render<LevelMeter>(p => p.Add(m => m.Listening, true));

        cut.FindAll(".band").Should().HaveCount(8);
        cut.FindAll(".fill").Should().ContainSingle("the level bar is still there under the bars");
    }

    /// <summary>Both recorders publish the same eight properties; one meter has to read either.</summary>
    [Theory]
    [InlineData("mic.js")]
    [InlineData("live-audio.js")]
    public void Every_recorder_publishes_the_bands_the_meter_reads(string file)
    {
        var source = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", file));

        source.Should().Contain("--po-band-", $"{file} feeds the spectrum");
        source.Should().Contain("getByteFrequencyData", $"{file} has to look at the frequencies to have any to publish");
    }

    /// <summary>The glass is one place, not three: the fallback matters more than the effect.</summary>
    [Fact]
    public void The_glass_keeps_an_opaque_floor_for_a_browser_that_cannot_blur()
    {
        var css = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "css", "app.css"));

        css.Should().Contain("--po-glass:");
        css.Should().Contain("@supports (backdrop-filter", "the blur is applied only where it exists");
        css.Should().MatchRegex(@"\.po-glass \{[^}]*background: var\(--po-glass\)", "the floor is unconditional");
    }

    private static string ClientRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PoFightJudge.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run from inside the repository");
        return Path.Combine(directory!.FullName, "src", "PoFightJudge.Client");
    }
}
