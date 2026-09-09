using AngleSharp.Dom;
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
using Radzen.Blazor;
using Toolbelt.Blazor.Extensions.DependencyInjection;

namespace PoFightJudge.Unit.Client;

/// <summary>The person's spoken turn: recorded and transcribed by the server, or dictated by the browser itself.</summary>
public sealed class HumanTurnTests : BunitContext, IAsyncLifetime
{
    private const string Clip = "UklGRiQAAABXQVZFZm10IBAAAAABAAEAgD4AAAB9AAACABAAZGF0YQAAAAA=";

    private static readonly MatchSide Matthew = MatchSide.Persona("MAH", "Matthew");
    private static readonly MatchSide Person = MatchSide.Human("KD", "Kim");

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly SimulationState _simulation = new();
    private readonly FakeTimeProvider _clock = new();

    public HumanTurnTests()
    {
        Services.AddRadzenComponents();
        // The play screen binds S to the slap, so its hot-key context needs a home.
        Services.AddHotKeys2();
        Services.AddSingleton(_api);
        // Program.cs loads the gate before the first render; under bunit the component's own fallback load does it.
        Services.AddSingleton<FeatureGate>();
        Services.AddSingleton(_simulation);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddScoped<AudioInterop>();
        Services.AddScoped<FxInterop>();
        // The stage makes a noise and draws on itself; both are best-effort, and under bunit both are no-ops.
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SfxInterop>();
        Services.AddScoped<GfxInterop>();
        Services.AddScoped<ParticleInterop>();
        Services.AddScoped<MicInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _simulation.Set(Matthew, Person, "the freezer");
        Flags(human: true, browser: true);
        _api.GenerateRoundAsync(Arg.Any<GenerateRoundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GenerateRoundResponse("You left it open all night.", "angry", "escalating", true)));
        _api.RoundAudioAsync(Arg.Any<RoundAudioRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TtsAudioDto(string.Empty, "pcm")));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    private void Flags(bool human, bool browser) =>
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FeatureFlagsDto(true, true, human, browser)));

    /// <summary>Renders the page and plays on until the argument is waiting for the person.</summary>
    private async Task<IRenderedComponent<WatchPlay>> AtTheirTurnAsync()
    {
        var cut = Render<WatchPlay>();
        await cut.WaitForAssertionAsync(() => cut.FindAll("article.line").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(5)));
        await cut.WaitForAssertionAsync(() => cut.FindAll("section.your-turn").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        return cut;
    }

    private static IElement Button(IRenderedComponent<WatchPlay> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains(text, StringComparison.Ordinal));

    private static string Typed(IRenderedComponent<WatchPlay> cut) => cut.Find("textarea[name=line]").GetAttribute("value") ?? string.Empty;

    [Fact(Timeout = 60_000)]
    public async Task The_microphone_is_offered_only_when_the_server_can_finish_the_turn()
    {
        Flags(human: false, browser: false);

        var cut = await AtTheirTurnAsync();

        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Speak it", StringComparison.Ordinal));
        cut.FindComponents<RadzenSpeechToTextButton>().Should().BeEmpty();
        cut.Find("textarea[name=line]").Should().NotBeNull("the line can always be typed");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_recorded_turn_is_transcribed_into_the_box_rather_than_said_for_them()
    {
        JSInterop.Setup<string>(MicInterop.Stop).SetResult(Clip);
        _api.TranscribeAsync(Arg.Any<TranscribeRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TranscribeResponse("I did not touch the freezer.", true)));
        // The turn opens the microphone by itself; the only thing left to do is say when you have finished.
        var cut = await AtTheirTurnAsync();

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Listening"), TimeSpan.FromSeconds(10));
        await Button(cut, "Done").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Typed(cut).Should().Contain("I did not touch the freezer"), TimeSpan.FromSeconds(10));
        await _api.Received(1).TranscribeAsync(Arg.Is<TranscribeRequest>(r => r.WavBase64 == Clip), Arg.Any<CancellationToken>());
        cut.FindAll("article.line").Should().HaveCount(1, "what was heard is offered for checking, not put on the record unread");
        cut.FindAll("section.your-turn").Should().HaveCount(1);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_clip_with_no_words_in_it_says_so_instead_of_sending_silence()
    {
        JSInterop.Setup<string>(MicInterop.Stop).SetResult(Clip);
        _api.TranscribeAsync(Arg.Any<TranscribeRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TranscribeResponse(string.Empty, true)));
        var cut = await AtTheirTurnAsync();

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Listening"), TimeSpan.FromSeconds(10));
        await Button(cut, "Done").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Nothing was made out"), TimeSpan.FromSeconds(10));
        Typed(cut).Should().BeEmpty();
        Button(cut, "Say it").HasAttribute("disabled").Should().BeTrue("there is nothing to say yet");
        Button(cut, "Speak it").Should().NotBeNull("and there is a way to try again");
    }

    [Fact(Timeout = 60_000)]
    public async Task Typing_closes_the_microphone_so_a_typed_line_can_be_sent_straight_away()
    {
        var cut = await AtTheirTurnAsync();
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Listening"), TimeSpan.FromSeconds(10));

        // Somebody who would rather type has said what they came to say. Waiting on a pause that will never come
        // because they are not talking is a trap, so the first keystroke ends the listening.
        await cut.Find("textarea[name=line]").InputAsync(new() { Value = "We are not doing this again." });

        await cut.WaitForAssertionAsync(
            () => Button(cut, "Say it").HasAttribute("disabled").Should().BeFalse("a typed line is ready to send"),
            TimeSpan.FromSeconds(10));
        cut.Markup.Should().NotContain("Listening —", "the microphone let go when they started typing");
        await _api.DidNotReceive().TranscribeAsync(Arg.Any<TranscribeRequest>(), Arg.Any<CancellationToken>());
        Typed(cut).Should().Be("We are not doing this again.", "nothing was transcribed over the top of it");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_blocked_microphone_says_what_to_do_about_it()
    {
        JSInterop.Setup<string>(MicInterop.Start, _ => true).SetResult("NotAllowedError");

        var cut = await AtTheirTurnAsync();

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("The microphone was blocked"), TimeSpan.FromSeconds(10));
        cut.Markup.Should().NotContain("Listening —", "nothing is being recorded");
        await _api.DidNotReceive().TranscribeAsync(Arg.Any<TranscribeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 60_000)]
    public async Task Dictation_adds_to_what_is_already_in_the_box()
    {
        var cut = await AtTheirTurnAsync();

        // The browser's recogniser and the recording want the same microphone, so it is offered once the turn has
        // stopped listening — here by finishing a clip that had nothing in it.
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Listening"), TimeSpan.FromSeconds(10));
        await Button(cut, "Done").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindComponents<RadzenSpeechToTextButton>().Should().ContainSingle(), TimeSpan.FromSeconds(10));

        var dictation = cut.FindComponent<RadzenSpeechToTextButton>();

        await cut.InvokeAsync(() => dictation.Instance.Change.InvokeAsync("You left it open"));
        await cut.InvokeAsync(() => dictation.Instance.Change.InvokeAsync("and you know it"));

        await cut.WaitForAssertionAsync(
            () => Typed(cut).Should().Be("You left it open and you know it", "a recogniser reports a phrase at a time"),
            TimeSpan.FromSeconds(10));
    }
}
