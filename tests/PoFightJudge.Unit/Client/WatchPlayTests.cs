using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The play screen holds the audio bridge, which is asynchronously disposable — so the context has to be torn down
/// asynchronously too, or the container refuses to dispose it at the end of every test.
/// </summary>
public sealed class WatchPlayTests : BunitContext, IAsyncLifetime
{
    private static readonly MatchSide Matthew = MatchSide.Persona("MAH", "Matthew");
    private static readonly MatchSide Kimberly = MatchSide.Persona("KSH", "Kimberly");
    private static readonly MatchSide Person = MatchSide.Human("KD", "Kim");

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly SimulationState _simulation = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<GenerateRoundRequest> _asked = [];

    public WatchPlayTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        Services.AddSingleton(_simulation);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddScoped<AudioInterop>();
        Services.AddScoped<FxInterop>();
        Services.AddScoped<MicInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _api.GenerateRoundAsync(Arg.Any<GenerateRoundRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<GenerateRoundRequest>();
                _asked.Add(request);
                return Task.FromResult(new GenerateRoundResponse($"line {_asked.Count}", "angry", "escalating", true));
            });
        _api.RoundAudioAsync(Arg.Any<RoundAudioRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TtsAudioDto(string.Empty, "pcm")));
        _api.VerdictAsync(Arg.Any<VerdictRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VerdictResponse(
                MatchId.New(), "MAH", "He answered the point; she changed the subject.",
                62, 41, AdvancedStatsDto.Empty, AdvancedStatsDto.Empty, Persisted: true)));
    }

    /// <summary>Plays the argument out by letting each beat elapse until the page stops asking for lines.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    private static async Task RunToTheEndAsync(IRenderedComponent<WatchPlay> cut, FakeTimeProvider clock)
    {
        for (var beat = 0; beat < 24 && cut.FindAll("section.verdict").Count == 0 && cut.FindAll("section.your-turn").Count == 0; beat++)
        {
            await cut.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public void A_visitor_who_never_chose_a_matchup_is_sent_back_to_pick_one()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();

        Render<WatchPlay>();

        nav.Uri.Should().EndWith("watch", "the play screen has nothing to play without a matchup");
    }

    [Fact(Timeout = 60_000)]
    public async Task Two_personas_argue_their_rounds_out_and_the_judge_rules()
    {
        _simulation.Set(Matthew, Kimberly, "the thermostat");

        var cut = Render<WatchPlay>();
        await RunToTheEndAsync(cut, _clock);

        await cut.WaitForAssertionAsync(() => cut.FindAll("section.verdict").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        cut.FindAll("article.line").Should().HaveCount(WatchTurns.RoundsPerSide * 2, "each side gets its three rounds");
        _asked.Select(a => a.Speaker).Should().Equal(
            [WatchTurns.Husband, WatchTurns.Wife, WatchTurns.Husband, WatchTurns.Wife, WatchTurns.Husband, WatchTurns.Wife],
            "the husband opens and the two alternate");
        _asked.Should().OnlyContain(a => a.Topic == "the thermostat");
        cut.Markup.Should().Contain("He answered the point");
        cut.Markup.Should().Contain("Matthew", "the ruling names the winner rather than printing initials alone");
    }

    /// <summary>
    /// A speaker was cut off a few seconds into their line: the browser reported nothing still scheduled — it had
    /// not finished decoding when it was asked — so the argument waited out the minimum beat and the next line's
    /// audio stopped the one in progress. The page knows how long the line is, and waits that long.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task A_line_is_spoken_to_the_end_before_the_next_one_starts()
    {
        // Nine seconds of speech, and a browser that says nothing is pending — the state that caused the bug.
        JSInterop.Setup<double>(AudioInterop.Play, _ => true).SetResult(9.0);
        JSInterop.Setup<double>(AudioInterop.Pending, _ => true).SetResult(0.0);
        _api.RoundAudioAsync(Arg.Any<RoundAudioRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TtsAudioDto("bm90IHNpbGVuY2U=", "mp3")));
        _simulation.Set(Matthew, Kimberly, "the thermostat");

        var cut = Render<WatchPlay>();

        // The next line is fetched while this one is still being spoken, on purpose — so what says whether somebody
        // was cut off is when the next line is spoken, not when it was asked for.
        await cut.WaitForAssertionAsync(() => Spoken().Should().Be(1), TimeSpan.FromSeconds(10));

        await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(5)));
        await Task.Delay(100);
        Spoken().Should().Be(1, "five seconds into a nine-second line, nobody has finished talking");
        cut.FindAll("article.line").Should().HaveCount(1);

        await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(5)));
        await cut.WaitForAssertionAsync(() => Spoken().Should().Be(2), TimeSpan.FromSeconds(10));

        int Spoken() => JSInterop.Invocations[AudioInterop.Play].Count;
    }

    [Fact(Timeout = 60_000)]
    public async Task History_grows_with_every_line_so_each_reply_answers_the_one_before()
    {
        _simulation.Set(Matthew, Kimberly, null);

        var cut = Render<WatchPlay>();
        await RunToTheEndAsync(cut, _clock);

        await cut.WaitForAssertionAsync(() => _asked.Should().HaveCount(6), TimeSpan.FromSeconds(10));
        _asked.Select(a => a.History.Count).Should().Equal([0, 1, 2, 3, 4, 5]);
        _asked.Should().OnlyContain(a => a.Husband == Matthew && a.Wife == Kimberly);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_slap_interrupts_the_beat_lands_once_and_the_slapped_side_answers_it()
    {
        _simulation.Set(Matthew, Kimberly, null);

        var cut = Render<WatchPlay>();
        await cut.WaitForAssertionAsync(() => cut.FindAll("article.line").Should().HaveCount(1), TimeSpan.FromSeconds(10));

        var slap = cut.FindComponent<InterjectionBar>();
        await cut.InvokeAsync(() => slap.Instance.Throw.InvokeAsync(Interjections.All[0]));

        await cut.WaitForAssertionAsync(() => _asked.Should().Contain(a => a.WasSlapped), TimeSpan.FromSeconds(10));
        var reaction = _asked.Single(a => a.WasSlapped);
        reaction.Speaker.Should().Be(WatchTurns.Husband, "the slapped speaker reacts instead of the turn passing over");
        reaction.Interjection.Should().Be(Interjections.SlapKey);

        await cut.WaitForAssertionAsync(() => cut.FindComponent<InterjectionBar>().Instance.Used.Should().BeTrue("a slap is once per match"));

        await RunToTheEndAsync(cut, _clock);
        await cut.WaitForAssertionAsync(() => cut.FindAll("section.verdict").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        cut.FindAll("article.line").Should().HaveCount(WatchTurns.MaxLines, "the extra reaction makes a slapped match seven lines");
        _asked.Count(a => a.WasSlapped).Should().Be(1, "only the reaction itself is thrown by the slap");
    }

    [Fact(Timeout = 60_000)]
    public async Task The_persons_turn_stops_the_loop_until_they_have_said_something()
    {
        _simulation.Set(Matthew, Person, null);

        var cut = Render<WatchPlay>();
        await cut.WaitForAssertionAsync(() => cut.FindAll("article.line").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(5)));

        await cut.WaitForAssertionAsync(() => cut.FindAll("section.your-turn").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        _asked.Should().HaveCount(1, "the page does not write the person's line for them");
        cut.Markup.Should().Contain("Kim", "the panel addresses the person by the name they argue under");

        // Typed, not changed: the line is published on every keystroke so the button answers straight away.
        await cut.Find("textarea[name=line]").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "that is not what I said and you know it" });
        var spoken = cut.FindAll("button").Single(b => b.TextContent.Contains("Say it", StringComparison.Ordinal)).ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("that is not what I said"), TimeSpan.FromSeconds(10));
        cut.FindAll("article.line").Should().HaveCountGreaterThanOrEqualTo(2, "the person's line joins the argument");
        await cut.WaitForAssertionAsync(() => _asked.Should().HaveCount(2, "the loop resumes on the persona's side once the person has spoken"), TimeSpan.FromSeconds(10));
        _asked[1].History.Should().HaveCount(2, "the persona answers the person's line, not the one before it");

        await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(5)));
        await spoken;
        await cut.WaitForAssertionAsync(() => cut.FindAll("section.your-turn").Should().HaveCount(1, "it is the person's turn again"), TimeSpan.FromSeconds(10));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_line_that_cannot_be_generated_is_reported_and_the_argument_can_be_picked_up_again()
    {
        _simulation.Set(Matthew, Kimberly, null);
        _api.GenerateRoundAsync(Arg.Any<GenerateRoundRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<GenerateRoundResponse>(new ApiException(500, "Upstream", null)));

        var cut = Render<WatchPlay>();

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Upstream"), TimeSpan.FromSeconds(10));
        cut.FindAll("button").Should().Contain(b => b.TextContent.Contains("Try again", StringComparison.Ordinal));
        cut.FindAll("section.verdict").Should().BeEmpty("a failed line is not a result");
    }

}
