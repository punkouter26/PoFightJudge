using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;
using Toolbelt.Blazor.Extensions.DependencyInjection;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// What the watch stage does besides show words: the bell that opens a round, the crack the slap lands with, and
/// the aura that follows whoever is speaking. None of it is looked at — under bunit there are no pixels and no
/// speakers — so what is asserted is the decision to ask for each of them, at the moment it should have been made.
/// </summary>
public sealed class WatchStageTests : BunitContext, IAsyncLifetime
{
    private static readonly MatchSide Matthew = MatchSide.Persona("MAH", "Matthew");
    private static readonly MatchSide Kimberly = MatchSide.Persona("KSH", "Kimberly");

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly SimulationState _simulation = new();
    private readonly FakeTimeProvider _clock = new();

    public WatchStageTests()
    {
        Services.AddRadzenComponents();
        Services.AddHotKeys2();
        Services.AddSingleton(_api);
        Services.AddSingleton(_simulation);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<AudioInterop>();
        Services.AddScoped<FxInterop>();
        Services.AddScoped<MicInterop>();
        Services.AddScoped<SfxInterop>();
        Services.AddScoped<GfxInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _api.GenerateRoundAsync(Arg.Any<GenerateRoundRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new GenerateRoundResponse("a line", "angry", "escalating", true)));
        _api.RoundAudioAsync(Arg.Any<RoundAudioRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TtsAudioDto(string.Empty, "pcm")));
        _api.VerdictAsync(Arg.Any<VerdictRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VerdictResponse(
                MatchId.New(), "MAH", "He answered the point.", 62, 41,
                AdvancedStatsDto.Empty, AdvancedStatsDto.Empty, Persisted: true)));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    /// <summary>Every sound this page asked for, in the order it asked.</summary>
    private List<string> Sounds() =>
    [
        .. JSInterop.Invocations
            .Where(i => string.Equals(i.Identifier, SfxInterop.Play, StringComparison.Ordinal))
            .Select(i => i.Arguments[0] as string ?? string.Empty),
    ];

    private async Task<IRenderedComponent<WatchPlay>> ArgueToTheEndAsync()
    {
        _simulation.Set(Matthew, Kimberly, "the thermostat");
        var cut = Render<WatchPlay>();
        for (var beat = 0; beat < 24 && cut.FindAll("section.verdict").Count == 0; beat++)
        {
            await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(2)));
        }

        await cut.WaitForAssertionAsync(() => cut.FindAll("section.verdict").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        return cut;
    }

    /// <summary>
    /// One bell per round, and three of them at the end. A round is the husband opening one — counted rather than
    /// derived from the line count, because a slap adds a line without advancing anything.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task A_bell_opens_every_round_and_three_of_them_end_the_argument()
    {
        await ArgueToTheEndAsync();

        var sounds = Sounds();
        sounds.Count(s => string.Equals(s, Sfx.Bell, StringComparison.Ordinal))
            .Should().Be(WatchTurns.RoundsPerSide, "each of the three rounds is rung in");
        sounds.Should().ContainInOrder(Sfx.Bell, Sfx.BellThree, Sfx.GavelThree);
        sounds.Should().EndWith(Sfx.GavelThree, "the gavel is the last thing that happens");
    }

    /// <summary>The context is woken from the click that got here, or the first bell is swallowed by the browser.</summary>
    [Fact(Timeout = 60_000)]
    public async Task The_audio_context_is_armed_before_anything_is_played()
    {
        _simulation.Set(Matthew, Kimberly, "the thermostat");

        Render<WatchPlay>();

        await Task.Yield();
        JSInterop.Invocations.Select(i => i.Identifier).First(i =>
            string.Equals(i, SfxInterop.Arm, StringComparison.Ordinal) || string.Equals(i, SfxInterop.Play, StringComparison.Ordinal))
            .Should().Be(SfxInterop.Arm);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_slap_cracks_over_the_line_it_interrupts_and_rings_the_stage()
    {
        _simulation.Set(Matthew, Kimberly, "the thermostat");
        var cut = Render<WatchPlay>();
        await cut.WaitForAssertionAsync(() => cut.FindAll("article.line").Should().NotBeEmpty(), TimeSpan.FromSeconds(10));

        await cut.InvokeAsync(() => cut.Find("button.heckle").Click());

        await cut.WaitForAssertionAsync(
            () => Sounds().Should().Contain(Sfx.Slap),
            TimeSpan.FromSeconds(10));
        JSInterop.Invocations[SfxInterop.Duck].Should().NotBeEmpty("the crack lands over the top of the line");
        JSInterop.Invocations[GfxInterop.Shock].Single().Arguments
            .Should().Equal([WatchPlay.StageSelector], "the ring goes out from the stage itself");
    }

    /// <summary>
    /// The aura is mounted once and aimed at the card of whoever has the floor, by selector: the browser measures
    /// it, because the two corners sit side by side on a laptop and stacked on a phone.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task The_stage_is_drawn_on_and_the_aura_follows_the_speaker()
    {
        JSInterop.Setup<bool>(GfxInterop.Mount, _ => true).SetResult(true);
        _simulation.Set(Matthew, Kimberly, "the thermostat");

        var cut = Render<WatchPlay>();
        await cut.WaitForAssertionAsync(() => cut.FindAll("article.line").Should().NotBeEmpty(), TimeSpan.FromSeconds(10));

        var mount = JSInterop.Invocations[GfxInterop.Mount].Single();
        mount.Arguments[0].Should().Be(WatchPlay.StageSelector);
        mount.Arguments[1].Should().Be(Shaders.Stage);

        await cut.WaitForAssertionAsync(
            () => JSInterop.Invocations[GfxInterop.Focus].Should().NotBeEmpty(),
            TimeSpan.FromSeconds(10));
        var aimed = JSInterop.Invocations[GfxInterop.Focus];
        aimed[^1].Arguments.Should().Equal(WatchPlay.StageSelector, ".corner.speaking");
        JSInterop.Invocations[GfxInterop.Set].Should().NotBeEmpty("the aura is tinted by the side that has the floor");
    }

    /// <summary>Nothing is left drawing itself on a page nobody is looking at any more.</summary>
    [Fact(Timeout = 60_000)]
    public async Task Leaving_the_page_takes_the_canvas_down()
    {
        JSInterop.Setup<bool>(GfxInterop.Mount, _ => true).SetResult(true);
        _simulation.Set(Matthew, Kimberly, "the thermostat");
        var cut = Render<WatchPlay>();
        await cut.WaitForAssertionAsync(() => cut.FindAll("article.line").Should().NotBeEmpty(), TimeSpan.FromSeconds(10));

        await DisposeComponentsAsync();

        JSInterop.Invocations[GfxInterop.Unmount].Single().Arguments.Should().Equal([WatchPlay.StageSelector]);
    }

    /// <summary>
    /// The shaders are named in C# and written in JS, and nothing connects the two but the string. A page asking
    /// for a shader the browser does not have draws nothing at all, silently, which is not a failure anybody sees.
    /// </summary>
    [Fact]
    public void Every_named_shader_exists_in_the_browser()
    {
        var source = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", "gfx.js"));

        var missing = Shaders.All.Where(name => !source.Contains($"\"{name}\":", StringComparison.Ordinal)).ToList();

        missing.Should().BeEmpty("js/gfx.js must have a shader for every name a page can mount");
    }

    /// <summary>
    /// The one motion switch covers the loudest thing on the page. A stage that keeps swirling for somebody who
    /// asked the whole system for less of it would be the app ignoring them where it matters most.
    /// </summary>
    [Fact]
    public void The_drawn_layer_answers_to_the_reduced_motion_switch()
    {
        var source = File.ReadAllText(Path.Combine(ClientRoot(), "wwwroot", "js", "gfx.js"));

        source.Should().Contain("prefers-reduced-motion");
        source.Should().Contain("if (reducedMotion() || mounts.has(selector))", "it refuses to mount rather than mounting something still");
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
