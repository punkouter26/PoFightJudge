using System.Threading.Channels;
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
/// The play loop reads the streams rather than waiting out two whole round trips per line.
/// </summary>
/// <remarks>
/// Both stream endpoints and <c>PoAudio.enqueue</c> were built in phase D and never called: the loop asked for a
/// finished line, then for its finished audio, and stood still for both. CP4 measured the line's first token at
/// 1.06 s and its first audio at 0.91 s, so that was about two seconds of nothing per turn that had already been
/// paid for. A line the prefetch already has is not streamed — it is finished before it is needed, and re-asking
/// for it as a stream would be a second call for a line the page is holding.
/// </remarks>
public sealed class WatchStreamingTests : BunitContext, IAsyncLifetime
{
    private static readonly MatchSide Matthew = MatchSide.Persona("MAH", "Matthew");
    private static readonly MatchSide Kimberly = MatchSide.Persona("KSH", "Kimberly");

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly SimulationState _simulation = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<GenerateRoundRequest> _streamed = [];
    private readonly List<GenerateRoundRequest> _whole = [];
    private readonly List<RoundAudioRequest> _audioStreamed = [];

    /// <summary>Held open by the opening line's test so the page can be inspected mid-stream.</summary>
    private readonly Channel<RoundStreamPart> _openingLine = Channel.CreateUnbounded<RoundStreamPart>();

    public WatchStreamingTests()
    {
        Services.AddRadzenComponents();
        Services.AddHotKeys2();
        Services.AddSingleton(_api);
        Services.AddSingleton<FeatureGate>();
        Services.AddSingleton(_simulation);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddScoped<AudioInterop>();
        Services.AddScoped<FxInterop>();
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SfxInterop>();
        Services.AddScoped<GfxInterop>();
        Services.AddScoped<ParticleInterop>();
        Services.AddScoped<MicInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _api.StreamRoundAsync(Arg.Any<GenerateRoundRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _streamed.Add(call.Arg<GenerateRoundRequest>());
                return _streamed.Count == 1 ? _openingLine.Reader.ReadAllAsync(call.Arg<CancellationToken>()) : Scripted($"streamed {_streamed.Count}");
            });
        _api.GenerateRoundAsync(Arg.Any<GenerateRoundRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _whole.Add(call.Arg<GenerateRoundRequest>());
                return Task.FromResult(new GenerateRoundResponse($"whole {_whole.Count}", "angry", "escalating", true));
            });
        _api.StreamRoundAudioAsync(Arg.Any<RoundAudioRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _audioStreamed.Add(call.Arg<RoundAudioRequest>());
                return Clauses();
            });
        _api.RoundAudioAsync(Arg.Any<RoundAudioRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TtsAudioDto(Convert.ToBase64String([7, 7]), "pcm")));
        _api.VerdictAsync(Arg.Any<VerdictRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VerdictResponse(
                MatchId.New(), "MAH", "He answered the point.", 62, 41,
                AdvancedStatsDto.Empty, AdvancedStatsDto.Empty, Persisted: true)));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        _openingLine.Writer.TryComplete();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<RoundStreamPart> Scripted(string line)
    {
        await Task.Yield();
        yield return new RoundStreamPart(line, null);
        yield return new RoundStreamPart(null, new GenerateRoundResponse(line, "angry", "escalating", true));
    }

    /// <summary>Two clauses in one format, which is the shape the routing service produces for an ordinary line.</summary>
    private static async IAsyncEnumerable<RoundAudioChunkDto> Clauses()
    {
        await Task.Yield();
        yield return new RoundAudioChunkDto(0, Convert.ToBase64String([1, 2]), "pcm", IsLast: false);
        yield return new RoundAudioChunkDto(1, Convert.ToBase64String([3, 4]), "pcm", IsLast: true);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_opening_line_is_read_as_it_is_written_rather_than_waited_out()
    {
        _simulation.Set(Matthew, Kimberly, "the thermostat");

        var cut = Render<WatchPlay>();
        await cut.WaitForAssertionAsync(() => _streamed.Should().HaveCount(1), TimeSpan.FromSeconds(10));

        await _openingLine.Writer.WriteAsync(new RoundStreamPart("You always ", null));
        await cut.WaitForAssertionAsync(
            () => cut.Markup.Should().Contain("You always"),
            TimeSpan.FromSeconds(10));
        cut.FindAll("article.line").Should().BeEmpty("a line still being written is not yet one of the rounds");

        await _openingLine.Writer.WriteAsync(new RoundStreamPart("do this.", null));
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("You always do this."), TimeSpan.FromSeconds(10));

        await _openingLine.Writer.WriteAsync(
            new RoundStreamPart(null, new GenerateRoundResponse("You always do this.", "furious", "escalating", true)));
        _openingLine.Writer.TryComplete();

        await cut.WaitForAssertionAsync(() => cut.FindAll("article.line").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        cut.Find("article.line").GetAttribute("data-mood").Should().Be("furious", "the closing object is what the round is made from");
    }

    /// <summary>
    /// The prefetch is the whole point of the loop's pacing: the line after this one is asked for while this one is
    /// still being spoken. Streaming a line that is already in hand would ask for it twice.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task A_line_the_prefetch_already_holds_is_not_asked_for_again_as_a_stream()
    {
        _simulation.Set(Matthew, Kimberly, null);
        _openingLine.Writer.TryWrite(new RoundStreamPart(null, new GenerateRoundResponse("opening", "angry", "escalating", true)));
        _openingLine.Writer.TryComplete();

        var cut = Render<WatchPlay>();
        for (var beat = 0; beat < 24 && cut.FindAll("section.verdict").Count == 0; beat++)
        {
            await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(2)));
        }

        await cut.WaitForAssertionAsync(() => cut.FindAll("section.verdict").Should().HaveCount(1), TimeSpan.FromSeconds(10));
        _streamed.Should().ContainSingle("only the opening line has nothing prefetched behind it");
        _whole.Should().HaveCount(WatchTurns.RoundsPerSide * 2 - 1, "every later line is prefetched whole while the one before it plays");
    }

    [Fact(Timeout = 60_000)]
    public async Task Audio_is_scheduled_clause_by_clause_instead_of_played_as_one_blob()
    {
        _simulation.Set(Matthew, Kimberly, null);
        _openingLine.Writer.TryWrite(new RoundStreamPart(null, new GenerateRoundResponse("opening", "angry", "escalating", true)));
        _openingLine.Writer.TryComplete();

        var cut = Render<WatchPlay>();
        await cut.WaitForAssertionAsync(() => _audioStreamed.Should().NotBeEmpty(), TimeSpan.FromSeconds(10));
        await cut.WaitForAssertionAsync(
            () => JSInterop.Invocations[AudioInterop.Enqueue].Should().HaveCountGreaterThanOrEqualTo(2),
            TimeSpan.FromSeconds(10));

        JSInterop.Invocations[AudioInterop.Play]
            .Should().BeEmpty("a streamed line is scheduled, never restarted mid-clause");
        _ = _api.DidNotReceive().RoundAudioAsync(Arg.Any<RoundAudioRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A replay plays a line back, so the clauses are archived as one clip rather than as the first phrase.</summary>
    [Fact(Timeout = 60_000)]
    public async Task The_clauses_are_archived_as_one_clip()
    {
        _simulation.Set(Matthew, Kimberly, null);
        _openingLine.Writer.TryWrite(new RoundStreamPart(null, new GenerateRoundResponse("opening", "angry", "escalating", true)));
        _openingLine.Writer.TryComplete();
        VerdictRequest? judged = null;
        _api.VerdictAsync(Arg.Any<VerdictRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                judged = call.Arg<VerdictRequest>();
                return Task.FromResult(new VerdictResponse(
                    MatchId.New(), "MAH", "He answered the point.", 62, 41,
                    AdvancedStatsDto.Empty, AdvancedStatsDto.Empty, Persisted: true));
            });

        var cut = Render<WatchPlay>();
        for (var beat = 0; beat < 24 && cut.FindAll("section.verdict").Count == 0; beat++)
        {
            await cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(2)));
        }

        await cut.WaitForAssertionAsync(() => judged.Should().NotBeNull(), TimeSpan.FromSeconds(10));
        var archived = judged!.Rounds.Where(r => r.Audio is not null).ToList();
        archived.Should().NotBeEmpty("every persona line was spoken");
        Convert.FromBase64String(archived[0].Audio!.Base64)
            .Should().Equal([1, 2, 3, 4], "the whole line is archived, not its first clause");
    }
}
