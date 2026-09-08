using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Reading a watch back. Every round's audio was written at the time; until this page there was nothing that read
/// any of it, so what matters here is that the lines appear and that a stored round is asked for by its own index.
/// </summary>
public sealed class WatchReplayTests : BunitContext, IAsyncLifetime
{
    private static readonly DateTimeOffset Night = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly MatchId _id = MatchId.New();

    public WatchReplayTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        Services.AddScoped<AudioInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _api.GetMatchAsync(_id, Arg.Any<CancellationToken>()).Returns(Match());
        _api.GetMatchTurnsAsync(_id, Arg.Any<CancellationToken>()).Returns(Turns());
        _api.GetRoundAudioAsync(_id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TtsAudioDto("AAAA", "mp3"));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// The page owns an <see cref="AudioInterop"/>, which disposes asynchronously; bunit tears its container down
    /// synchronously, so the disposal has to be awaited here or it races the end of the test.
    /// </summary>
    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    private MatchDto Match() =>
        new(_id, "u", MatchMode.Watch, Night, Night.AddMinutes(6), "the thermostat",
            MatchSide.Persona("MAH", "Married Husband"), MatchSide.Persona("KSH", "Karen"),
            SessionPhase.Done, SessionStatus.Ready, "Karen", "She had the receipts.", IsFake: false);

    private IReadOnlyList<TurnDto> Turns() =>
    [
        new(_id, 0, Speaker.Player1, TurnKind.Round, "You never load it properly.") { Mood = "clipped", AudioBlobName = "a", AudioFormat = "mp3" },
        new(_id, 1, Speaker.Player2, TurnKind.Round, "I load it exactly as the manual says.") { Mood = "flat", AudioBlobName = "b", AudioFormat = "mp3" },
    ];

    private IRenderedComponent<WatchReplay> RenderReplay()
    {
        var cut = Render<WatchReplay>(p => p.Add(x => x.MatchIdText, _id.Value.ToString()));
        cut.WaitForAssertion(() => cut.FindAll(".line").Should().HaveCount(2));
        return cut;
    }

    [Fact]
    public void Every_line_of_the_argument_is_read_back_with_who_said_it()
    {
        var cut = RenderReplay();

        var lines = cut.FindAll(".line");
        lines[0].TextContent.Should().Contain("Married Husband").And.Contain("You never load it properly.");
        lines[1].TextContent.Should().Contain("Karen").And.Contain("I load it exactly as the manual says.");
        cut.Markup.Should().Contain("She had the receipts.", "the ruling is part of what a replay shows");
    }

    [Fact]
    public async Task Playing_one_line_asks_for_that_rounds_own_audio()
    {
        var cut = RenderReplay();

        await cut.FindAll(".line button")[1].ClickAsync(new());

        await _api.Received(1).GetRoundAudioAsync(_id, 1, Arg.Any<CancellationToken>());
        await _api.DidNotReceive().GetRoundAudioAsync(_id, 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_watch_that_kept_no_audio_says_so_rather_than_offering_silence()
    {
        _api.GetMatchTurnsAsync(_id, Arg.Any<CancellationToken>()).Returns([]);

        var cut = Render<WatchReplay>(p => p.Add(x => x.MatchIdText, _id.Value.ToString()));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Nothing was kept"));
        cut.FindAll(".line").Should().BeEmpty();
    }

    [Fact]
    public void Somebody_elses_watch_is_not_found_rather_than_blank()
    {
        _api.GetMatchAsync(_id, Arg.Any<CancellationToken>()).Returns((MatchDto?)null);

        var cut = Render<WatchReplay>(p => p.Add(x => x.MatchIdText, _id.Value.ToString()));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Not here"));
    }
}
