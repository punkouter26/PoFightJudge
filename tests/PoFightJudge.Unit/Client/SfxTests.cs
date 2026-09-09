using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The sound bus. Nothing here listens to what came out of the speakers — what is tested is the decision: whether a
/// sound was asked for, whether the choice was remembered, and that a browser which will not make a noise cannot
/// take a page down with it.
/// </summary>
public class SfxTests : BunitContext
{
    private readonly ILocalStorageService _storage = Substitute.For<ILocalStorageService>();

    public SfxTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddRadzenComponents();
        Services.AddSingleton(_storage);
        Services.AddScoped<SfxInterop>();
    }

    private SfxInterop Interop() => new(JSInterop.JSRuntime, _storage);

    [Fact]
    public async Task A_sound_reaches_the_browser_by_name()
    {
        await Interop().PlayAsync(Sfx.Bell);

        var call = JSInterop.Invocations.Single();
        call.Identifier.Should().Be(SfxInterop.Play);
        call.Arguments.Should().Equal(Sfx.Bell, 0d);
    }

    [Fact]
    public async Task A_counting_blip_carries_the_number_it_is_counting()
    {
        await Interop().PlayAsync(Sfx.Count, 0.42);

        JSInterop.Invocations.Single().Arguments.Should().Equal(Sfx.Count, 0.42);
    }

    /// <summary>An effect over a voice is a duck and a sound, in that order: the dip has to start first.</summary>
    [Fact]
    public async Task An_effect_played_over_a_voice_dips_it_before_it_lands()
    {
        await Interop().PlayOverAsync(Sfx.Slap, 0.5);

        JSInterop.Invocations.Select(i => i.Identifier).Should().Equal(SfxInterop.Duck, SfxInterop.Play);
        JSInterop.Invocations[SfxInterop.Duck].Single().Arguments.Should().Equal(0.5);
    }

    [Fact]
    public async Task Sound_is_on_for_somebody_who_has_never_chosen()
    {
        _storage.GetItemAsStringAsync(SfxInterop.StorageKey, Arg.Any<CancellationToken>()).Returns((string?)null);
        var sfx = Interop();

        await sfx.InitializeAsync();

        sfx.IsOn.Should().BeTrue("this app talks out loud by design");
        JSInterop.Invocations.Single().Arguments.Should().Equal(false);
    }

    [Fact]
    public async Task A_stored_no_is_honoured_and_told_to_the_browser()
    {
        _storage.GetItemAsStringAsync(SfxInterop.StorageKey, Arg.Any<CancellationToken>()).Returns(SfxInterop.Off);
        var sfx = Interop();

        await sfx.InitializeAsync();

        sfx.IsOn.Should().BeFalse();
        var call = JSInterop.Invocations.Single();
        call.Identifier.Should().Be(SfxInterop.SetMuted);
        call.Arguments.Should().Equal(true);
    }

    /// <summary>Blazored writes the bare word, but a value written by hand or by an older build may be quoted.</summary>
    [Fact]
    public async Task A_quoted_choice_reads_the_same_as_a_bare_one()
    {
        _storage.GetItemAsStringAsync(SfxInterop.StorageKey, Arg.Any<CancellationToken>()).Returns("\"off\"");
        var sfx = Interop();

        await sfx.InitializeAsync();

        sfx.IsOn.Should().BeFalse();
    }

    [Fact]
    public async Task Turning_it_off_is_remembered()
    {
        var sfx = Interop();

        await sfx.ToggleAsync();

        sfx.IsOn.Should().BeFalse();
        await _storage.Received().SetItemAsStringAsync(SfxInterop.StorageKey, SfxInterop.Off, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A private window throws on the accessor rather than answering. A page that would not load because somebody
    /// keeps no site data would be an absurd way to lose an argument.
    /// </summary>
    [Fact]
    public async Task A_browser_that_keeps_no_site_data_still_has_sound()
    {
#pragma warning disable CA2012 // NSubstitute configures the call it is handed; the ValueTask is never awaited by design.
        _storage.GetItemAsStringAsync(SfxInterop.StorageKey, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("storage is disabled"));
#pragma warning restore CA2012
        var sfx = Interop();

        await sfx.InitializeAsync();

        sfx.IsOn.Should().BeTrue();
    }

    [Fact]
    public async Task A_browser_that_refuses_to_make_a_noise_is_not_an_error()
    {
        JSInterop.SetupVoid(SfxInterop.Play, _ => true).SetException(new JSException("no audio here"));

        var play = async () => await Interop().PlayAsync(Sfx.Bell);

        await play.Should().NotThrowAsync("a bell that will not ring changes nothing about the match");
    }

    [Fact]
    public void The_toggle_says_what_it_will_do_and_flips_when_it_is_pressed()
    {
        var cut = Render<SoundToggle>();

        cut.Find("button").GetAttribute("aria-pressed").Should().Be("true");

        cut.Find("button").Click();

        cut.Find("button").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("button").GetAttribute("aria-label").Should().Be("Turn the sound effects on");
    }

    /// <summary>Turning it back on arms the context from the click and confirms itself out loud.</summary>
    [Fact]
    public void Turning_the_sound_back_on_makes_a_sound()
    {
        var cut = Render<SoundToggle>();

        cut.Find("button").Click();
        cut.Find("button").Click();

        JSInterop.Invocations[SfxInterop.Arm].Should().ContainSingle("the click that turns it on is the gesture the context needs");
        JSInterop.Invocations[SfxInterop.Play].Single().Arguments.Should().Equal(Sfx.Tick, 0d);
    }

    /// <summary>
    /// The catalogue is spelled twice — once as constants and once as voices in the browser — because a sound has
    /// to be schedulable before .NET can ask for it. Nothing tells you the two have drifted: an unknown name is
    /// silence, and silence is not something anybody investigates.
    /// </summary>
    [Fact]
    public void Every_named_effect_has_a_voice_in_the_browser()
    {
        var source = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", "sfx.js"));

        var missing = Sfx.All.Where(name => !source.Contains($"\"{name}\":", StringComparison.Ordinal)).ToList();

        missing.Should().BeEmpty("js/sfx.js must have a voice for every name a page can ask for");
    }

    [Fact]
    public void The_sound_bus_is_loaded_by_the_page()
    {
        var html = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "index.html"));

        html.Should().Contain("js/sfx.js", "the CSP is script-src 'self', so it is a file and not an inline block");
    }

    /// <summary>Both voice players hand PoSfx a way to dip them; without it an effect fights the line it lands on.</summary>
    [Theory]
    [InlineData("audio.js")]
    [InlineData("live-audio.js")]
    public void Every_voice_bus_can_be_ducked(string file)
    {
        var source = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", file));

        source.Should().Contain("duck", $"PoSfx dips {file} when an effect has to be heard over it");
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
