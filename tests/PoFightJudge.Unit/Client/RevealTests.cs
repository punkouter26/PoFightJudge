using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The reveal: numbers that arrive rather than appear, and the bits that fly off things when somebody wins. What
/// matters here is the half that has to be right when none of it runs — a number nobody watched count is still a
/// number, and a page whose confetti was refused has still shown who took it.
/// </summary>
public class RevealTests : BunitContext
{
    private readonly FakeTimeProvider _clock = new();

    public RevealTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddScoped<SfxInterop>();
        Services.AddScoped<ParticleInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// The important half. The climb is started by the first frame that actually arrives, so a browser or a test
    /// that never gives it one shows the finished number — never a zero that stays there.
    /// </summary>
    [Fact]
    public void A_number_that_never_gets_to_count_is_shown_in_full()
    {
        var cut = Render<CountUp>(p => p.Add(c => c.Value, 62).Add(c => c.Suffix, "%"));

        cut.Find(".count").TextContent.Should().Be("62%");
    }

    [Fact]
    public void A_number_asked_for_without_a_climb_simply_appears()
    {
        var cut = Render<CountUp>(p => p
            .Add(c => c.Value, 174)
            .Add(c => c.Duration, TimeSpan.Zero));

        cut.Find(".count").TextContent.Should().Be("174");
        JSInterop.Invocations.Should().BeEmpty("nothing counted means nothing to hear");
    }

    [Fact]
    public async Task A_number_climbs_to_where_it_was_going_and_stops_there()
    {
        var cut = Render<CountUp>(p => p
            .Add(c => c.Value, 80)
            .Add(c => c.Duration, TimeSpan.FromMilliseconds(240)));

        var seen = await ClimbAsync(cut);

        seen.Should().Contain(v => v > 0 && v < 80, "it was seen on its way up rather than arriving finished");
        seen[^1].Should().Be(80, "and it stops where it was going, not a step past it");
    }

    /// <summary>The blips climb with the number, which is what makes the two one thing rather than a sound over a picture.</summary>
    [Fact]
    public async Task The_blips_climb_with_the_number()
    {
        var cut = Render<CountUp>(p => p
            .Add(c => c.Value, 80)
            .Add(c => c.Duration, TimeSpan.FromMilliseconds(240)));

        await ClimbAsync(cut);

        var blips = JSInterop.Invocations[SfxInterop.Play];
        blips.Should().NotBeEmpty();
        blips.Should().OnlyContain(i => (string)i.Arguments[0]! == Sfx.Count);
        var values = blips.Select(i => (double)i.Arguments[1]!).ToList();
        values.Should().BeInAscendingOrder("the pitch follows the number it is counting");
        values.Should().OnlyContain(v => v > 0 && v <= 1);
    }

    [Fact]
    public async Task A_number_that_is_meant_to_be_seen_and_not_heard_says_nothing()
    {
        var cut = Render<CountUp>(p => p
            .Add(c => c.Value, 80)
            .Add(c => c.Audible, false)
            .Add(c => c.Duration, TimeSpan.FromMilliseconds(240)));

        await ClimbAsync(cut);

        JSInterop.Invocations[SfxInterop.Play].Should().BeEmpty();
    }

    /// <summary>
    /// The particles are a canvas the browser owns; the only thing worth asserting from here is that a page which
    /// cannot have them is not a page that fails. Every call swallows the refusal.
    /// </summary>
    [Fact]
    public async Task A_browser_that_will_not_draw_particles_is_not_an_error()
    {
        JSInterop.SetupVoid(ParticleInterop.Burst, _ => true).SetException(new InvalidOperationException("no canvas"));
        var particles = new ParticleInterop(JSInterop.JSRuntime);

        var burst = async () => await particles.BurstAsync(".verdict", ParticleInterop.Confetti, ".player");

        await burst.Should().NotThrowAsync();
    }

    /// <summary>Both effects layers refuse to run at all for somebody who asked the system for less motion.</summary>
    [Theory]
    [InlineData("gfx.js")]
    [InlineData("particles.js")]
    public void Nothing_is_drawn_for_somebody_who_asked_for_less_motion(string file)
    {
        var source = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", file));

        source.Should().Contain("prefers-reduced-motion");
        source.Should().Contain("reducedMotion()", $"{file} answers no rather than drawing something still");
    }

    /// <summary>
    /// A frame loop that runs on an empty page is the one way a decoration becomes a battery complaint. Both
    /// layers stop when there is nothing left to draw.
    /// </summary>
    [Theory]
    [InlineData("gfx.js")]
    [InlineData("particles.js")]
    public void The_frame_loop_stops_when_there_is_nothing_left_to_draw(string file)
    {
        var source = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", file));

        source.Should().Contain("cancelAnimationFrame");
        source.Should().Contain("document.hidden", $"{file} does not draw into a tab nobody is looking at");
    }

    /// <summary>
    /// Runs a climb out and reports every number it showed on the way. One tick at a time: a fake clock fires the
    /// delays it can reach, and the step after this one is not registered until this one's continuation has run.
    /// </summary>
    private async Task<List<double>> ClimbAsync(IRenderedComponent<CountUp> cut)
    {
        var seen = new List<double>();
        for (var tick = 0; tick < 80; tick++)
        {
            await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromMilliseconds(10)));
            seen.Add(double.Parse(cut.Find(".count").TextContent, System.Globalization.CultureInfo.CurrentCulture));
        }

        return seen;
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
