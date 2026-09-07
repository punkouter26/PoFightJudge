using Microsoft.AspNetCore.Components;
using PoMarriedFight.Client.Components;
using PoMarriedFight.Client.Services;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Client.Pages;

/// <summary>
/// One fight, as it happens. The page joins the fight, opens the microphone, plays the host back and shows what is
/// being said; when the ruling is in it hands over to the verdict.
/// </summary>
/// <remarks>
/// Everything the fight sends arrives on a background connection, so every handler hops back onto the renderer
/// before touching state. Without that the page would update from the wrong thread and render half-written state.
/// </remarks>
public sealed partial class FightLive : ComponentBase, ILiveFightListener, IAsyncDisposable
{
    private readonly CaptionLog _captions = new();
    private LiveConnection? _connection;
    private FighterDto? _oneRecord;
    private FighterDto? _twoRecord;
    private DebateSnapshotDto? _snapshot;
    private bool _listening;
    private bool _hostSpeaking;
    private bool _reconnecting;
    private bool _ending;
    private string? _problem;
    private string? _fatal;

    [Parameter] public string MatchIdText { get; set; } = string.Empty;

    [Inject] private ILiveAudio Audio { get; set; } = default!;

    [Inject] private LiveConnectionFactory Connections { get; set; } = default!;

    [Inject] private IApiClient Api { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    private SessionPhase Phase => _snapshot?.Phase ?? SessionPhase.Intro;

    private string Player1 => _snapshot?.Player1Name is { Length: > 0 } name ? name : "Fighter one";

    private string Player2 => _snapshot?.Player2Name is { Length: > 0 } name ? name : "Fighter two";

    private string HostName => HostPersonaCatalogue.For(_snapshot?.Persona ?? HostPersonaId.Referee).Name;

    private string Title => _snapshot?.Topic is { Length: > 0 } topic ? topic : "The fight";

    private string Lead => _listening
        ? "The host moderates the turns. Speak when it hands you the floor."
        : "Connecting you to the host.";

    protected override async Task OnInitializedAsync()
    {
        if (!MatchId.TryParse(MatchIdText, null, out var id))
        {
            _fatal = "That is not a fight.";
            return;
        }

        _connection = Connections.Create();
        try
        {
            await _connection.ConnectAsync(id, this);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _fatal = $"This fight could not be joined ({ex.Message}). It may have already ended.";
            return;
        }

        // The microphone opens only once the fight is joined, so nothing is recorded into nowhere.
        var started = await Audio.StartCaptureAsync(_connection);
        if (!started.Started)
        {
            _fatal = MicMessage(started.Error);
            return;
        }

        _listening = true;
    }

    public void OnHostAudio(byte[] pcm24k) => Dispatch(async () =>
    {
        _hostSpeaking = true;
        await Audio.PlayAsync(pcm24k);
    });

    public void OnHostInterrupted() => Dispatch(async () =>
    {
        _hostSpeaking = false;
        await Audio.ClearPlaybackAsync();
    });

    public void OnCaption(CaptionDto caption) => Dispatch(() =>
    {
        _captions.Add(caption);

        // The host has finished a sentence; anything still queued is the tail of it.
        if (caption is { Speaker: Speaker.Host, Final: true })
        {
            _hostSpeaking = false;
        }

        return Task.CompletedTask;
    });

    public void OnSnapshot(DebateSnapshotDto snapshot) => Dispatch(async () =>
    {
        var first = _snapshot is null;
        _snapshot = snapshot;

        // The tags are only known once the first snapshot lands, so their records are read then and not before.
        if (first)
        {
            await LoadRecordsAsync(snapshot);
        }
    });

    /// <summary>What each of them has done before. Missing records are simply absent; the fight does not need them.</summary>
    private async Task LoadRecordsAsync(DebateSnapshotDto snapshot)
    {
        if (FighterId.TryParse(snapshot.Player1Name, null, out var one))
        {
            _oneRecord = await Api.GetFighterAsync(one);
        }

        if (FighterId.TryParse(snapshot.Player2Name, null, out var two))
        {
            _twoRecord = await Api.GetFighterAsync(two);
        }
    }

    public void OnEnded(string matchId) => Dispatch(async () =>
    {
        _listening = false;
        await Audio.StopAsync();
        Nav.NavigateTo($"verdict/{matchId}");
    });

    public void OnProblem(string message) => Dispatch(() =>
    {
        _problem = message;
        return Task.CompletedTask;
    });

    public void OnConnectionLost(string? reason) => Dispatch(() =>
    {
        _reconnecting = true;
        return Task.CompletedTask;
    });

    public void OnConnectionAbandoned(string reason) => Dispatch(async () =>
    {
        _reconnecting = false;
        _listening = false;
        _fatal = reason;
        await Audio.StopAsync();
    });

    private async Task StopAsync()
    {
        if (_ending || _connection is null)
        {
            return;
        }

        _ending = true;
        try
        {
            await _connection.EndFightAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _problem = $"The fight could not be stopped cleanly ({ex.Message}).";
            _ending = false;
        }
    }

    /// <summary>Runs a handler on the renderer and redraws. Everything the fight sends arrives off it.</summary>
    private void Dispatch(Func<Task> work) => _ = InvokeAsync(async () =>
    {
        // A reconnected fight is a connected fight, whatever else the update was about.
        _reconnecting = false;
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _problem = ex.Message;
        }

        StateHasChanged();
    });

    private static string MicMessage(string error) => error switch
    {
        "NotAllowedError" or "SecurityError" => "The microphone was blocked, and a fight is nothing without it. Allow it for this site and start again.",
        "NotFoundError" or "DevicesNotFoundError" => "No microphone was found. A fight needs one.",
        "NotReadableError" or "TrackStartError" => "The microphone is in use by something else.",
        _ => $"The microphone could not be started ({error}).",
    };

    public async ValueTask DisposeAsync()
    {
        await Audio.StopAsync(CancellationToken.None);
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
