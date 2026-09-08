using System.Globalization;
using System.Runtime.CompilerServices;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Watch;

namespace PoFightJudge.Api.Features.Ai.Fakes;

/// <summary>
/// A whole WATCH match with no key and no network. Lines are assembled from the personas' own words so the match
/// still reads as those two characters arguing, and every line is deterministic in (speaker, round, attitude) — a
/// replayed match plays identically, which is what makes the client's speculative prefetch safe to test against.
/// The judge scores on word count and concreteness rather than pretending to reason.
/// </summary>
public sealed class FakeWatchAi(TimeSpan? chunkDelay = null) : IWatchAi
{
    private static readonly string[] Openers =
    [
        "Look, the thing is,", "Okay so,", "Honestly?", "Here's what actually happened:",
        "Can I just say,", "Let me finish —", "Right, so,", "The point is,",
    ];

    private static readonly string[] Cues = ["(snaps)", "(coldly)", "(seething)", "(flat)", "(dry)"];

    private readonly TimeSpan _chunkDelay = chunkDelay ?? TimeSpan.FromMilliseconds(20);

    public bool IsFake => true;

    public Task<ArgumentResult> GenerateArgumentAsync(
        Profile husband,
        Profile wife,
        IReadOnlyList<RoundContext> history,
        string speaker,
        string attitude,
        bool wasSlapped,
        string? interjection = null,
        string? topic = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(husband);
        ArgumentNullException.ThrowIfNull(wife);
        ArgumentNullException.ThrowIfNull(history);

        var speakingHusband = string.Equals(speaker, WatchRound.Husband, StringComparison.OrdinalIgnoreCase);
        var self = speakingHusband ? husband : wife;
        var other = speakingHusband ? wife : husband;
        var round = history.Count;

        // Deterministic in everything that shapes the line, so the same turn of the same matchup reads the same.
        var seed = Hash($"{self.Initials}|{other.Initials}|{round}|{attitude}|{wasSlapped}|{topic}");
        var opener = Openers[seed % Openers.Length];
        var cue = Cues[(seed / 7) % Cues.Length];
        var subject = string.IsNullOrWhiteSpace(topic) ? Pick(self.CommonArguments, "the dishwasher") : topic.Trim();
        var jab = Pick(other.Dislikes, "everything I do");
        var mine = Pick(self.Likes, "a bit of peace");

        var line = wasSlapped
            ? $"{opener} you actually SLAPPED me over {subject}? Fine — let's talk about {jab}, then. {cue}"
            : history.Count == 0
                ? $"{opener} we are doing {subject} again, and I am the only one who cares about {mine}. {cue}"
                : $"{opener} that is exactly the kind of thing you say about {jab}, and it has nothing to do with {subject}. {cue}";

        return Task.FromResult(new ArgumentResult($"[fake] {line}", WatchRules.NormalizeMood(attitude)));
    }

    public async IAsyncEnumerable<ArgumentStreamPart> StreamArgumentAsync(
        Profile husband,
        Profile wife,
        IReadOnlyList<RoundContext> history,
        string speaker,
        string attitude,
        bool wasSlapped,
        string? interjection = null,
        string? topic = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await GenerateArgumentAsync(husband, wife, history, speaker, attitude, wasSlapped, interjection, topic, ct);

        // Word by word, so the play page's streaming path is exercised rather than short-circuited.
        var words = result.Text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (_chunkDelay > TimeSpan.Zero)
            {
                await Task.Delay(_chunkDelay, ct);
            }

            yield return new ArgumentStreamPart(i == 0 ? words[i] : " " + words[i]);
        }

        yield return new ArgumentStreamPart(null, result);
    }

    /// <summary>
    /// Scores the transcript rather than inventing a ruling: more words, more concrete detail and fewer bare
    /// exclamations win. It is not a judge, but it is defensible, deterministic, and it moves when the argument does.
    /// </summary>
    public Task<JudgeVerdictResult> JudgeAsync(Profile husband, Profile wife, IReadOnlyList<RoundContext> rounds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(husband);
        ArgumentNullException.ThrowIfNull(wife);
        ArgumentNullException.ThrowIfNull(rounds);

        var husbandScore = ScoreFor(rounds, WatchRound.Husband);
        var wifeScore = ScoreFor(rounds, WatchRound.Wife);
        var winner = husbandScore == wifeScore ? string.Empty : husbandScore > wifeScore ? husband.Initials : wife.Initials;
        var verdict = winner.Length == 0
            ? "[fake judge] Both sides landed the same weight of argument, so this one is a draw."
            : $"[fake judge] {winner} argued in more concrete terms and stayed closer to the point; the other side leaned on volume. Scored {husbandScore}–{wifeScore}.";

        return Task.FromResult(new JudgeVerdictResult(winner, verdict, husbandScore, wifeScore));
    }

    private static int ScoreFor(IReadOnlyList<RoundContext> rounds, string speaker)
    {
        var lines = rounds.Where(r => string.Equals(r.Speaker, speaker, StringComparison.OrdinalIgnoreCase)).ToList();
        if (lines.Count == 0)
        {
            return 0;
        }

        var words = lines.Sum(l => ArgueScoreCalculator.WordCount(l.Text));
        var exclamations = lines.Sum(l => l.Text.Count(c => c == '!'));
        var specifics = lines.Sum(l => l.Text.Count(c => c == ',')); // clauses stand in for concrete detail
        return Math.Clamp(40 + (words / 4) + (specifics * 3) - (exclamations * 5), 0, 100);
    }

    private static string Pick(string? source, string fallback)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return fallback;
        }

        var first = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? fallback : first.ToLowerInvariant();
    }

    /// <summary>A stable hash: string.GetHashCode is randomized per process, which would make the fake non-deterministic across runs.</summary>
    private static int Hash(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in text)
            {
                hash = (hash * 31) + c;
            }

            return Math.Abs(hash);
        }
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
