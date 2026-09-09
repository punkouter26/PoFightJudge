using PoFightJudge.Client.Services;
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

    /// <summary>The line currently being written, as far as it has got. Rendered under the rounds, never as one.</summary>
    private readonly System.Text.StringBuilder _arriving = new();

    /// <summary>Whose line that is, so it renders in their colour and on their side.</summary>
    private string? _arrivingSpeaker;

    /// <summary>
    /// The next line: the speculative fetch when it still fits, and otherwise a stream, because a line nobody has
    /// is a line somebody is watching an empty stage for.
    /// </summary>
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
        return await StreamLineAsync(speaker, interjection, ct);
    }

    /// <summary>
    /// Reads the line as it is written, publishing each fragment so the stage fills in rather than sitting empty.
    /// The closing part is what the round is made from: the fragments are the decoded prefix of a JSON value still
    /// being written, and only the final object has been parsed.
    /// </summary>
    private async Task<GenerateRoundResponse> StreamLineAsync(string speaker, string? interjection, CancellationToken ct)
    {
        _arrivingSpeaker = speaker;
        _arriving.Clear();
        GenerateRoundResponse? final = null;

        await foreach (var part in Api.StreamRoundAsync(Request(speaker, interjection), ct))
        {
            if (part.Delta is { Length: > 0 } fragment)
            {
                _arriving.Append(fragment);
                StateHasChanged();
                continue;
            }

            if (part.Final is { } whole)
            {
                final = whole;
            }
        }

        if (final is { } complete)
        {
            return complete;
        }

        // The stream was cut off before the closing object. What did arrive is still the line — losing a sentence
        // the viewer has already read, to a missing wrapper, is worse than a line whose mood had to be guessed.
        // Nothing at all is a different thing entirely, and goes to the error path rather than adding a blank round.
        var partial = _arriving.ToString().Trim();
        return partial.Length > 0
            ? new GenerateRoundResponse(partial, string.Empty, string.Empty, _isFake)
            : throw new ApiException(0, "The line stopped arriving before anybody said anything.");
    }

    /// <summary>The line stops arriving the moment it is a round, or the moment it fails.</summary>
    private void ClearArriving()
    {
        _arriving.Clear();
        _arrivingSpeaker = null;
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
