using System.Globalization;
using System.Text;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Fighters;

/// <summary>
/// How somebody argues, gathered from the snapshot each of their debates left behind. It is a read over rows, so
/// it costs nothing to ask for and it changes the moment a debate is re-read or deleted.
/// </summary>
/// <remarks>
/// It is careful about how much it claims. One debate is an anecdote; a phrase said once is a phrase, not a habit.
/// The digest that goes to the host says how many debates it is drawn from, so the host can weigh it accordingly.
/// </remarks>
public static class StyleProfileBuilder
{
    /// <summary>A phrase has to turn up in more than one debate before it is a habit rather than a sentence.</summary>
    public const int MinimumDebatesForPhrase = 2;

    public const int MaxPhrases = 3;
    public const int MaxFallacies = 3;
    public const int MaxEmotions = 3;
    public const int MaxTips = 3;

    /// <summary>The digest rides in a prompt, so it stays short enough not to crowd out the show format.</summary>
    public const int MaxDigestLength = 160;

    public static StyleProfileDto Build(string tag, IReadOnlyList<FighterResultDto> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var spoken = results.Where(r => r.Style != StyleSnapshot.Empty).OrderBy(r => r.At).ToList();
        if (spoken.Count == 0)
        {
            return StyleProfileDto.Empty(tag);
        }

        var snapshots = spoken.Select(r => r.Style).ToList();
        var opener = MostCommon(snapshots.Select(s => s.Opener));
        var best = spoken.OrderByDescending(r => r.Score).ThenByDescending(r => r.At).First();

        var profile = new StyleProfileDto(
            tag,
            spoken.Count,
            MostCommon(snapshots.SelectMany(Tones)).Value,
            Repeated(snapshots.Select(s => s.Phrases), MinimumDebatesForPhrase, MaxPhrases),
            Repeated(snapshots.Select(s => s.Fallacies), 1, MaxFallacies),
            opener.Value,
            opener.Count,

            // The most recent level, not an average: a level is a judgement about them now.
            snapshots.LastOrDefault(s => !string.IsNullOrWhiteSpace(s.Cefr))?.Cefr ?? string.Empty,
            Repeated(snapshots.Select(s => s.Emotions), 1, MaxEmotions),
            best.Style.BestQuote,
            [.. snapshots[^1].Tips.Take(MaxTips)],
            string.Empty);

        return profile with { Digest = Digest(profile) };
    }

    /// <summary>
    /// The line the host reads before the fight. It says how much it is based on, because a host told somebody
    /// "always opens with a question" after one debate would be inventing a person.
    /// </summary>
    public static string Digest(StyleProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Debates == 0)
        {
            return "First fight.";
        }

        var sentence = new StringBuilder();
        sentence.Append(CultureInfo.InvariantCulture, $"{profile.Debates} {(profile.Debates == 1 ? "fight" : "fights")}");
        if (profile.IsEarly)
        {
            sentence.Append(" so far, so this is an early read");
        }

        sentence.Append('.');

        if (!string.IsNullOrWhiteSpace(profile.Tone))
        {
            sentence.Append(CultureInfo.InvariantCulture, $" Sounds {profile.Tone}.");
        }

        if (profile.OpenerRepeats >= MinimumDebatesForPhrase && !string.IsNullOrWhiteSpace(profile.Opener))
        {
            sentence.Append(CultureInfo.InvariantCulture, $" Opens with \"{profile.Opener}\".");
        }

        if (profile.Phrases.Count > 0)
        {
            sentence.Append(CultureInfo.InvariantCulture, $" Keeps saying: {string.Join("; ", profile.Phrases)}.");
        }

        if (profile.Fallacies.Count > 0)
        {
            sentence.Append(CultureInfo.InvariantCulture, $" Leans on the {string.Join(" and the ", profile.Fallacies).ToLowerInvariant()}.");
        }

        var digest = sentence.ToString();
        return digest.Length <= MaxDigestLength ? digest : digest[..MaxDigestLength].TrimEnd() + "…";
    }

    /// <summary>Tone is stored as a phrase; the words in it are what recur across debates.</summary>
    private static IEnumerable<string> Tones(StyleSnapshot snapshot) =>
        snapshot.Tone.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// What turns up across debates, most often first. The count is of debates rather than mentions: saying one
    /// phrase four times in one argument says less than saying it once in four.
    /// </summary>
    private static IReadOnlyList<string> Repeated(IEnumerable<IReadOnlyList<string>> perDebate, int minimum, int take)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var debate in perDebate)
        {
            foreach (var value in debate.Select(Normalize).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[value] = counts.GetValueOrDefault(value) + 1;
            }
        }

        return
        [
            .. counts.Where(kv => kv.Value >= minimum)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(take)
                .Select(kv => kv.Key),
        ];
    }

    /// <summary>The one said most often, and how many debates said it.</summary>
    private static (string Value, int Count) MostCommon(IEnumerable<string> values)
    {
        var best = values
            .Select(Normalize)
            .Where(v => v.Length > 0)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return best is null ? (string.Empty, 0) : (best.Key, best.Count());
    }

    /// <summary>Phrases are stored with their own count in brackets; the words are what should be compared.</summary>
    private static string Normalize(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        var bracket = trimmed.LastIndexOf(" (×", StringComparison.Ordinal);
        return bracket > 0 ? trimmed[..bracket] : trimmed;
    }
}
