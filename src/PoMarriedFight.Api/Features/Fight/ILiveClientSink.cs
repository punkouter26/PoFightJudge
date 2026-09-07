using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Fight;

/// <summary>Server to browser channel for one fight. Implemented over SignalR; faked in tests.</summary>
public interface ILiveClientSink
{
    /// <summary>The host's voice, as 24 kHz PCM, for the room to play.</summary>
    Task AudioOutAsync(ReadOnlyMemory<byte> pcm24k, CancellationToken ct);

    /// <summary>The host was cut off: drop whatever is still queued rather than talking over the interruption.</summary>
    Task AudioClearAsync(CancellationToken ct);

    Task CaptionAsync(CaptionDto caption, CancellationToken ct);

    Task SnapshotAsync(DebateSnapshotDto snapshot, CancellationToken ct);

    Task EndedAsync(MatchId matchId, CancellationToken ct);

    Task ErrorAsync(string message, CancellationToken ct);
}

/// <summary>Where a finished fight is handed over for analysis. Implemented by the pipeline; registered as a singleton.</summary>
public interface IAnalysisIntake
{
    ValueTask SubmitAsync(string userId, MatchId matchId, CancellationToken ct);
}
