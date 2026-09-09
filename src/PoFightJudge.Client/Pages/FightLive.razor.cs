using Microsoft.AspNetCore.Components;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Pages;

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
    /// <summary>The element the backdrop is drawn on, and the one the glass above it blurs.</summary>
    public const string StageSelector = ".stage";

    /// <summary>Where the debate clock starts running hot. The backdrop turns over the last half-minute.</summary>
    private const int HeatFromSeconds = 30;

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

    /// <summary>Whether the browser took the backdrop. No is a perfectly good answer; the glass has its own floor.</summary>
    private bool _drawn;

    /// <summary>The phase the last stinger was played for, so one phase is announced once.</summary>
    private SessionPhase? _announced;

    /// <summary>Who was last handed the floor, so the cue is played on the change rather than on every snapshot.</summary>
    private Speaker? _lastFloor;

    [Parameter] public string MatchIdText { get; set; } = string.Empty;

    [Inject] private ILiveAudio Audio { get; set; } = default!;

    [Inject] private SetupMemory Memory { get; set; } = default!;

    [Inject] private LiveConnectionFactory Connections { get; set; } = default!;

    [Inject] private IApiClient Api { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Inject] private SfxInterop Sound { get; set; } = default!;

    [Inject] private GfxInterop Gfx { get; set; } = default!;

    [Inject] private ParticleInterop Particles { get; set; } = default!;

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

        // The click that started the fight is the gesture the effects context needs.
        await Sound.ArmAsync();

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
        // The microphone chosen and tested at setup, so the fight opens the one that was proved to work.
        var started = await Audio.StartCaptureAsync(_connection, await Memory.MicAsync());
        if (!started.Started)
        {
            _fatal = MicMessage(started.Error);
            return;
        }

        _listening = true;
    }

    /// <summary>
    /// Mounts the backdrop once the stage is really in the page. It breathes with the host rather than with the
    /// microphone: the fighters are in the room, and the voice that belongs to the screen is the host's.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _fatal is null)
        {
            _drawn = await Gfx.MountAsync(StageSelector, Shaders.Backdrop, GfxInterop.LiveLevel);
        }
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

        await AnnounceAsync(snapshot);
        await PaintAsync(snapshot);
    });

    /// <summary>
    /// The show's punctuation: a sting on each new phase and a cue when the floor changes hands. Both land over
    /// the host rather than beside them — the host is usually mid-sentence when the phase turns over, and a sound
    /// competing with the words is worse than no sound.
    /// </summary>
    private async Task AnnounceAsync(DebateSnapshotDto snapshot)
    {
        if (_announced != snapshot.Phase)
        {
            _announced = snapshot.Phase;
            if (StingFor(snapshot.Phase) is { } sting)
            {
                await Sound.PlayOverAsync(sting, 0.7);
            }
        }

        // Only a real hand-over, and only while somebody actually has the floor: the host taking it back is their
        // own voice saying so.
        if (_lastFloor != snapshot.Speaking)
        {
            var had = _lastFloor;
            _lastFloor = snapshot.Speaking;
            if (snapshot.Speaking is not null && had is not null)
            {
                await Sound.PlayAsync(Sfx.Cue);
            }
        }
    }

    /// <summary>Which sound belongs to a phase. Intro has none: the host is already talking over it.</summary>
    public static string? StingFor(SessionPhase phase) => phase switch
    {
        SessionPhase.Setup => Sfx.Intro,
        SessionPhase.Debate => Sfx.Bell,
        SessionPhase.Probe => Sfx.Probe,
        SessionPhase.Verdict => Sfx.Ruling,
        SessionPhase.Done => Sfx.BellThree,
        _ => null,
    };

    /// <summary>
    /// Points the backdrop at whoever has the floor and heats it as the clock runs down. Both are one number each,
    /// and the shader does the rest — nothing here is drawn from .NET.
    /// </summary>
    private async Task PaintAsync(DebateSnapshotDto snapshot)
    {
        if (!_drawn)
        {
            return;
        }

        var mix = snapshot.Speaking switch
        {
            Speaker.Player1 => 0.12,
            Speaker.Player2 => 0.88,
            _ => 0.5,
        };

        var heat = HeatFor(snapshot.Phase, snapshot.DebateRemainingSeconds);
        await Gfx.SetAsync(StageSelector, "mix", mix);
        await Gfx.SetAsync(StageSelector, "heat", heat);

        // Embers only while the clock is actually running out. Thirty a second at the death is enough to see and
        // few enough that a phone does not notice them.
        if (snapshot.Phase == SessionPhase.Debate)
        {
            await Particles.EmitAsync(StageSelector, heat * 30);
        }
        else
        {
            await Particles.EmitAsync(StageSelector, 0);
        }
    }

    /// <summary>
    /// How hot the backdrop runs, 0 to 1. It climbs through the last half-minute of arguing and sits halfway up
    /// once the host has taken over — the questions and the ruling are not calm, but they are not a countdown
    /// either. Public because it is the whole decision, and the only part of the backdrop worth asserting.
    /// </summary>
    public static double HeatFor(SessionPhase phase, int remainingSeconds) => phase switch
    {
        SessionPhase.Debate when remainingSeconds is > 0 and <= HeatFromSeconds =>
            (HeatFromSeconds - remainingSeconds) / (double)HeatFromSeconds,
        SessionPhase.Probe or SessionPhase.Verdict => 0.5,
        _ => 0,
    };

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
        // The canvas first: it holds a GL context and a frame loop, and both outlive a page that only half
        // finished tidying itself up.
        await Gfx.UnmountAsync(StageSelector, CancellationToken.None);
        await Particles.ClearAsync(StageSelector, CancellationToken.None);
        await Audio.StopAsync(CancellationToken.None);
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
