using Microsoft.AspNetCore.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Pages;

/// <summary>
/// The round loop. Lines are asked for one at a time, spoken, and answered until both sides have had their rounds;
/// then the judge rules and the match is persisted server-side. The loop lives on the client because the pace of an
/// argument is a viewing decision — a spectator can interrupt it, and a person arguing a side stops it dead until
/// they have spoken.
/// </summary>
public sealed partial class WatchPlay : IAsyncDisposable
{
    /// <summary>
    /// The pause between lines when there is no audio to wait for. Without it the whole argument would appear at
    /// once, and nobody could get a slap in edgeways.
    /// </summary>
    internal static readonly TimeSpan MinimumBeat = TimeSpan.FromSeconds(1.2);

    private readonly List<WatchRoundDto> _rounds = [];
    private readonly CancellationTokenSource _leaving = new();

    private IReadOnlyList<ProfileDto> _cast = [];
    private FeatureFlagsDto? _flags;
    private CancellationTokenSource? _beat;

    /// <summary>
    /// When the line now being spoken finishes. The browser is asked the same question, but this is the answer the
    /// app already has — and if the two disagree the longer one wins, because cutting a speaker off mid-sentence
    /// is far worse than a moment of quiet after they finish.
    /// </summary>
    private DateTimeOffset? _lineEndsAt;
    private Stage _stage = Stage.Arguing;
    private string? _speaking;
    private string? _pendingInterjection;
    private bool _slapUsed;
    private bool _isFake;
    private string _line = string.Empty;
    private string? _error;

    /// <summary>
    /// When a throttled call may be tried again. The AI routes are limited per user, and a Try again that fires
    /// straight back into the same 429 reads as a broken button — so the button waits, visibly, and says how long.
    /// </summary>
    private DateTimeOffset? _retryAt;

    /// <summary>Held so the loop that counts it down is observed rather than dropped on the floor.</summary>
    private Task? _countdown;
    private VerdictResponse? _verdict;

    private enum Stage
    {
        Arguing,
        WaitingForPerson,
        Judging,
        Done,
    }

    [Inject] private IApiClient Api { get; set; } = default!;

    [Inject] private SimulationState Simulation { get; set; } = default!;

    [Inject] private AudioInterop Audio { get; set; } = default!;

    [Inject] private FxInterop Fx { get; set; } = default!;

    [Inject] private MicInterop Mic { get; set; } = default!;

    [Inject] private TimeProvider Clock { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    /// <summary>The side a real person is arguing, when there is one.</summary>
    private MatchSide? Person => Simulation.Human;

    /// <summary>What to wait when a 429 arrives without a header to say. One window of the server's limit.</summary>
    private static readonly TimeSpan DefaultThrottleWait = TimeSpan.FromSeconds(30);

    private string Headline => Simulation.Topic.Length == 0 ? "The argument" : Simulation.Topic;

    /// <summary>Whole seconds left of a throttle, or zero when there is nothing to wait for.</summary>
    private int RetrySeconds =>
        _retryAt is { } at && at > Clock.GetUtcNow() ? (int)Math.Ceiling((at - Clock.GetUtcNow()).TotalSeconds) : 0;

    private string Lead =>
        _stage == Stage.Done ? "The judge has ruled. The match is on the record."
        : Person is not null ? "You are in this one. Answer when it is your turn."
        : "Three rounds each, then the judge rules.";

    private string WinnerLine =>
        _verdict is null ? string.Empty
        : _verdict.Winner.Length == 0 ? "Nobody won that."
        : $"{NameOfSide(_verdict.Winner)} won it.";

    protected override async Task OnInitializedAsync()
    {
        if (!Simulation.IsReady)
        {
            // Nothing to play: the matchup lives in memory, so a reload or a shared link lands here empty.
            Nav.NavigateTo("watch");
            return;
        }

        await LoadCastAsync();
        await RunAsync();
    }

    /// <summary>
    /// Portraits for the personas on the stage, and which ways of speaking a turn are open. A failure here costs a
    /// face and the microphone button, not the match: the line can always be typed.
    /// </summary>
    private async Task LoadCastAsync()
    {
        try
        {
            _flags = await Api.GetFeaturesAsync(_leaving.Token);
            _cast = await Api.GetProfilesAsync(_leaving.Token) ?? [];
        }
        catch (ApiException)
        {
            _cast = [];
        }
        catch (HttpRequestException)
        {
            _cast = [];
        }
    }

    /// <summary>
    /// Plays the argument out from wherever it stands. It returns at three points: the person's turn, the verdict,
    /// and a failure — each of which needs something to happen before there can be another line.
    /// </summary>
    private async Task RunAsync()
    {
        var ct = _leaving.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (WatchTurns.IsComplete(_rounds.Select(r => r.Speaker)) || _rounds.Count >= WatchTurns.MaxLines)
                {
                    await JudgeAsync(ct);
                    return;
                }

                var speaker = WatchTurns.NextSpeaker(_rounds.Count, _slapUsed);
                if (SideOf(speaker).IsHuman)
                {
                    _stage = Stage.WaitingForPerson;
                    _speaking = null;
                    StateHasChanged();

                    // Their turn opens the microphone: pressing a button before you may speak is a step nobody
                    // asked for, and the meter is what says it is working.
                    await BeginListeningAsync();
                    return;
                }

                if (!await SpeakAsync(speaker, ct))
                {
                    return;
                }

                await BeatAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The page is being left; the argument goes no further.
        }
    }

    /// <summary>Asks for one line, shows it, and speaks it. False when it could not be generated.</summary>
    private async Task<bool> SpeakAsync(string speaker, CancellationToken ct)
    {
        _stage = Stage.Arguing;
        _error = null;
        _speaking = speaker;
        StateHasChanged();

        var thrown = _pendingInterjection;
        _pendingInterjection = null;

        GenerateRoundResponse response;
        try
        {
            response = await NextLineAsync(speaker, thrown, ct);
        }
        catch (ApiException ex)
        {
            return Failed(ex);
        }
        catch (HttpRequestException ex)
        {
            return Failed($"The line could not be fetched ({ex.Message}).");
        }

        var round = new WatchRoundDto(speaker, response.Text, WatchTurns.NormalizeMood(response.Mood));
        _rounds.Add(round);
        _isFake |= response.IsFake;
        StateHasChanged();

        // The next line is asked for while this one is still being spoken, so the pause between them is the pause
        // a viewer expects rather than the round trip.
        StartPrefetch(ct);
        await PlayAsync(round, SideOf(speaker), ct);
        return true;
    }

    /// <summary>Fetches and plays a line's audio. A voiceless line is still a line, so a failure here is swallowed.</summary>
    private async Task PlayAsync(WatchRoundDto round, MatchSide side, CancellationToken ct)
    {
        if (side.IsHuman)
        {
            return;
        }

        try
        {
            var audio = await Api.RoundAudioAsync(new RoundAudioRequest(ProfileId.From(side.Id), round.Text), ct);
            if (!audio.IsEmpty)
            {
                var speaking = await Audio.PlayAsync(audio, ct);
                _lineEndsAt = speaking > TimeSpan.Zero ? Clock.GetUtcNow() + speaking : null;
            }
        }
        catch (ApiException)
        {
            // The voice chain is down; the line stays on screen and the argument carries on.
        }
        catch (HttpRequestException)
        {
        }
    }

    /// <summary>Holds the argument for as long as the line takes to speak, or a readable beat when it is silent.</summary>
    private async Task BeatAsync(CancellationToken ct)
    {
        using var beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _beat = beat;
        try
        {
            var pending = await Audio.PendingAsync(beat.Token);
            var remaining = _lineEndsAt is { } ends ? ends - Clock.GetUtcNow() : TimeSpan.Zero;
            var wait = Max(Max(pending, remaining), MinimumBeat);
            await Task.Delay(wait, Clock, beat.Token);

            static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // A slap landed mid-sentence: the reaction starts now rather than after the line finishes.
        }
        finally
        {
            _beat = null;
            _lineEndsAt = null;
        }
    }

    /// <summary>Takes the interjection, cuts the speaker off, and lets the loop pick the reaction up.</summary>
    private async Task ThrowAsync(Interjection heckle)
    {
        if (_slapUsed || _stage != Stage.Arguing)
        {
            return;
        }

        _slapUsed = true;
        _pendingInterjection = heckle.Key;
        DropPrefetch();
        await Fx.BurstAsync(heckle.Key, _leaving.Token);
        await Audio.StopAsync(_leaving.Token);
        if (_beat is { } interrupted)
        {
            await interrupted.CancelAsync();
        }
    }

    private async Task OnLineTypedAsync(ChangeEventArgs e)
    {
        _line = e.Value?.ToString() ?? string.Empty;
        await StopListeningAsync();
    }

    /// <summary>The person's line, typed. Their spoken turn arrives with the microphone.</summary>
    private async Task SayAsync()
    {
        // Sending is the end of the turn however the words got there, so an open microphone is let go rather than
        // standing in the way: whoever typed this is finished, and the pause that ends a spoken turn is not coming.
        await StopListeningAsync();

        var said = _line.Trim();
        if (said.Length == 0 || _stage != Stage.WaitingForPerson)
        {
            return;
        }

        // What register they said it in is read from their words when the match is analysed, not guessed here; the
        // default is the middle of the scale, exactly as the server treats a line whose mood it does not know.
        var speaker = WatchTurns.NextSpeaker(_rounds.Count, _slapUsed);
        _rounds.Add(new WatchRoundDto(speaker, said, WatchTurns.NormalizeMood(null)));
        _line = string.Empty;
        _stage = Stage.Arguing;
        await RunAsync();
    }

    private async Task JudgeAsync(CancellationToken ct)
    {
        _stage = Stage.Judging;
        _speaking = null;
        StateHasChanged();

        try
        {
            var topic = Simulation.Topic.Length == 0 ? null : Simulation.Topic;
            _verdict = await Api.VerdictAsync(new VerdictRequest(Simulation.Husband!, Simulation.Wife!, _rounds, topic), ct);
            _stage = Stage.Done;
            _error = null;
        }
        catch (ApiException ex)
        {
            Failed(ex);
        }
        catch (HttpRequestException ex)
        {
            Failed($"The judge could not be reached ({ex.Message}).");
        }

        StateHasChanged();
    }

    private async Task RetryAsync()
    {
        _error = null;
        _stage = _rounds.Count == 0 ? Stage.Arguing : _stage;
        StateHasChanged();
        await RunAsync();
    }

    private async Task RematchAsync()
    {
        await Audio.StopAsync(_leaving.Token);
        _rounds.Clear();
        _verdict = null;
        _error = null;
        _slapUsed = false;
        _pendingInterjection = null;
        _isFake = false;
        _stage = Stage.Arguing;
        DropPrefetch();
        await RunAsync();
    }

    private void Leave() => Nav.NavigateTo("watch");

    /// <summary>Records a failure and stops the loop. Always false, so a caller can return it directly.</summary>
    private bool Failed(string message)
    {
        _error = message;
        _speaking = null;
        StateHasChanged();
        return false;
    }

    /// <summary>
    /// The same, for an answer from the API. A throttle is held apart from a fault: it carries a wait, and until that
    /// wait is up there is nothing to try again.
    /// </summary>
    private bool Failed(ApiException ex)
    {
        if (ex.IsThrottled)
        {
            StartCountdown(ex.RetryAfter ?? DefaultThrottleWait);
        }

        return Failed(ex.Summary);
    }

    /// <summary>Ticks once a second so the wait counts down on screen, and clears itself when the window is up.</summary>
    private void StartCountdown(TimeSpan wait)
    {
        _retryAt = Clock.GetUtcNow() + wait;
        _countdown = CountDownAsync();
    }

    private async Task CountDownAsync()
    {
        while (!_leaving.IsCancellationRequested && RetrySeconds > 0)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), Clock, _leaving.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            StateHasChanged();
        }

        _retryAt = null;
        StateHasChanged();
    }

    private MatchSide SideOf(string speaker) =>
        WatchTurns.IsHusband(speaker) ? Simulation.Husband! : Simulation.Wife!;

    private bool IsSpeaking(string speaker) => string.Equals(_speaking, speaker, StringComparison.Ordinal);

    private string NameOf(string speaker) => SideOf(speaker).DisplayName;

    /// <summary>The display name behind a side id, for the ruling.</summary>
    private string NameOfSide(string id) =>
        string.Equals(Simulation.Husband?.Id, id, StringComparison.OrdinalIgnoreCase) ? Simulation.Husband!.DisplayName
        : string.Equals(Simulation.Wife?.Id, id, StringComparison.OrdinalIgnoreCase) ? Simulation.Wife!.DisplayName
        : id;

    private ProfileDto? ProfileFor(MatchSide? side) =>
        side is null || side.IsHuman
            ? null
            : _cast.FirstOrDefault(p => string.Equals(p.Persona.Initials, side.Id, StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        await _leaving.CancelAsync();
        DropPrefetch();

        // The microphone goes first: a recording light left on after the page is gone is not something to explain.
        await Mic.CancelAsync(CancellationToken.None);

        // The beat owns its own lifetime through a using in BeatAsync; disposing it again here is free and is what
        // proves the field cannot outlive the page.
        _beat?.Dispose();
        _beat = null;
        _countdown = null;
        StopLevelWatch();
        _leaving.Dispose();
        await Audio.StopAsync(CancellationToken.None);
    }
}
