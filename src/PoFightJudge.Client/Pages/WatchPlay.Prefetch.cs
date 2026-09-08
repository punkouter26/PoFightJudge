using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Pages;

/// <summary>
/// Speculative fetch of the next line. A round trip to the model costs a second or two, and asking for it while the
/// current line is still being spoken is what keeps the argument sounding like an argument rather than a queue.
/// </summary>
/// <remarks>
/// A prefetch is only usable when nothing has changed the request it was built from: the same speaker, the same
/// history length, and no interjection thrown in the meantime. A slap invalidates it, which is the whole reason the
/// match is checked rather than assumed.
/// </remarks>
public sealed partial class WatchPlay
{
    private Task<GenerateRoundResponse>? _prefetched;
    private string? _prefetchedSpeaker;
    private int _prefetchedHistory;

    /// <summary>The next line, taken from the speculative fetch when it still fits, and asked for outright when it does not.</summary>
    private async Task<GenerateRoundResponse> NextLineAsync(string speaker, string? interjection, CancellationToken ct)
    {
        if (interjection is null
            && _prefetched is { } waiting
            && string.Equals(_prefetchedSpeaker, speaker, StringComparison.Ordinal)
            && _prefetchedHistory == _rounds.Count)
        {
            _prefetched = null;
            _prefetchedSpeaker = null;
            return await waiting;
        }

        DropPrefetch();
        return await Api.GenerateRoundAsync(Request(speaker, interjection), ct);
    }

    /// <summary>Starts fetching the line after this one, when the next turn is one the client can predict.</summary>
    private void StartPrefetch(CancellationToken ct)
    {
        DropPrefetch();

        if (_rounds.Count >= WatchTurns.MaxLines || WatchTurns.IsComplete(_rounds.Select(r => r.Speaker)))
        {
            return;
        }

        var speaker = WatchTurns.NextSpeaker(_rounds.Count, _slapUsed);
        if (SideOf(speaker).IsHuman)
        {
            // A person's turn cannot be guessed at, and the line after it depends on what they say.
            return;
        }

        _prefetchedSpeaker = speaker;
        _prefetchedHistory = _rounds.Count;
        _prefetched = Api.GenerateRoundAsync(Request(speaker, null), ct);
    }

    /// <summary>Abandons a speculative fetch, observing its failure so a discarded request cannot surface elsewhere.</summary>
    private void DropPrefetch()
    {
        var abandoned = _prefetched;
        _prefetched = null;
        _prefetchedSpeaker = null;
        _prefetchedHistory = 0;

        if (abandoned is not null)
        {
            _ = abandoned.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Builds the request for one line. The history is copied rather than passed by reference: the list keeps
    /// growing while a speculative fetch is in flight, and a request must carry the argument as it stood.
    /// </summary>
    private GenerateRoundRequest Request(string speaker, string? interjection) => new(
        Simulation.Husband!,
        Simulation.Wife!,
        [.. _rounds],
        speaker,
        Simulation.Topic.Length == 0 ? null : Simulation.Topic,
        interjection,
        string.Equals(interjection, Interjections.SlapKey, StringComparison.Ordinal));
}
