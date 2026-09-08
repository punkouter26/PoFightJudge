using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>Everything about how a player talked that can be computed without a model.</summary>
public static class SpeechMetrics
{
    private const double PauseThresholdSeconds = 1.0;
    private const double SentenceGapSeconds = 1.5;

    public static PlayerMetricsDto Compute(Speaker player, MappedTranscript transcript, IReadOnlyList<TurnDto> turns)
    {
        var mine = transcript.For(player).OrderBy(w => w.Start).ToList();
        var others = transcript.For(player.Other()).OrderBy(w => w.Start).ToList();
        var allSpeech = transcript.Words.Sum(w => Duration(w));

        var talkSeconds = mine.Sum(Duration) + GapsWithin(mine);
        var talkShare = allSpeech + GapsWithin(others) + GapsWithin(mine) <= 0 ? 0 : talkSeconds / (talkSeconds + others.Sum(Duration) + GapsWithin(others));

        var myTurns = turns.Where(t => t.Speaker == player && t.Kind is TurnKind.Talk or TurnKind.Probe).ToList();
        var turnLengths = myTurns.Where(t => t.EndSeconds is not null).Select(t => t.EndSeconds!.Value - t.StartSeconds).ToList();

        var pauses = Pauses(mine);
        var (made, received, overlap) = Interruptions(mine, others);
        var hostInterrupts = turns.Count(t => t.Kind == TurnKind.Interrupt && t.Speaker == player);

        var text = string.Join(' ', mine.Select(w => w.Text));
        var tokens = Lexicon.Tokenize(text);
        var sentences = Sentences(mine);
        var syllables = tokens.Sum(Readability.Syllables);
        var fillerCount = tokens.Count(Lexicon.Fillers.Contains) + CountPhrases(text, Lexicon.FillerPhrases);
        var contentWords = tokens.Where(t => !Lexicon.StopWords.Contains(t) && t.Length > 2).ToList();

        return new PlayerMetricsDto(
            TalkSeconds: Round(talkSeconds),
            TalkShare: Round(talkShare),
            Turns: myTurns.Count,
            AverageTurnSeconds: turnLengths.Count == 0 ? 0 : Round(turnLengths.Average()),
            LongestTurnSeconds: turnLengths.Count == 0 ? 0 : Round(turnLengths.Max()),
            Words: tokens.Count,
            WordsPerMinute: talkSeconds <= 0 ? 0 : Round(tokens.Count / (talkSeconds / 60.0)),
            Pauses: pauses.Count,
            MeanPauseSeconds: pauses.Count == 0 ? 0 : Round(pauses.Average()),
            LongestPauseSeconds: pauses.Count == 0 ? 0 : Round(pauses.Max()),
            InterruptionsMade: made,
            InterruptionsReceived: received,
            OverlapSeconds: Round(overlap),
            HostInterrupts: hostInterrupts,
            FillersPer100: tokens.Count == 0 ? 0 : Round(100.0 * fillerCount / tokens.Count),
            Hedges: CountPhrases(text, Lexicon.Hedges),
            Absolutes: tokens.Count(Lexicon.Absolutes.Contains),
            Questions: sentences.Count(s => s.EndsWith('?')) + CountQuestionOpeners(sentences),
            TypeTokenRatio: tokens.Count == 0 ? 0 : Round((double)tokens.Distinct(StringComparer.Ordinal).Count() / tokens.Count),
            MeanWordLength: tokens.Count == 0 ? 0 : Round(tokens.Average(t => t.Length)),
            SyllablesPerWord: tokens.Count == 0 ? 0 : Round((double)syllables / tokens.Count),
            MeanSentenceLength: sentences.Count == 0 ? 0 : Round((double)tokens.Count / sentences.Count),
            FleschReadingEase: Readability.FleschReadingEase(tokens.Count, Math.Max(1, sentences.Count), syllables),
            FleschKincaidGrade: Readability.FleschKincaidGrade(tokens.Count, Math.Max(1, sentences.Count), syllables),
            TopWords: [.. contentWords.GroupBy(w => w, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(10).Select(g => g.Key)],
            LongestWord: tokens.OrderByDescending(t => t.Length).ThenBy(t => t, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty,
            RepeatedPhrases: RepeatedPhrases(tokens),
            Profanity: tokens.Count(Lexicon.Profanity.Contains),
            FirstPersonRatio: tokens.Count == 0 ? 0 : Round((double)tokens.Count(Lexicon.FirstPerson.Contains) / tokens.Count),
            SecondPersonRatio: tokens.Count == 0 ? 0 : Round((double)tokens.Count(Lexicon.SecondPerson.Contains) / tokens.Count));
    }

    private static double Duration(MappedWord w) => Math.Max(0, w.End - w.Start);

    /// <summary>Short gaps between consecutive words count as talking; long ones are pauses.</summary>
    private static double GapsWithin(List<MappedWord> words)
    {
        double total = 0;
        for (var i = 1; i < words.Count; i++)
        {
            var gap = words[i].Start - words[i - 1].End;
            if (gap > 0 && gap < PauseThresholdSeconds)
            {
                total += gap;
            }
        }

        return total;
    }

    private static List<double> Pauses(List<MappedWord> words)
    {
        var pauses = new List<double>();
        for (var i = 1; i < words.Count; i++)
        {
            var gap = words[i].Start - words[i - 1].End;
            if (gap >= PauseThresholdSeconds && gap < 15)
            {
                pauses.Add(gap);
            }
        }

        return pauses;
    }

    private static (int Made, int Received, double Overlap) Interruptions(List<MappedWord> mine, List<MappedWord> others)
    {
        var mineFirst = FirstOfRun(mine);
        var othersFirst = FirstOfRun(others);
        var made = 0;
        var received = 0;
        double overlap = 0;
        var j = 0;
        for (var i = 0; i < mine.Count; i++)
        {
            var w = mine[i];
            while (j < others.Count && others[j].End <= w.Start)
            {
                j++;
            }

            for (var k = j; k < others.Count && others[k].Start < w.End; k++)
            {
                var o = others[k];
                var ov = Math.Min(w.End, o.End) - Math.Max(w.Start, o.Start);
                if (ov <= 0)
                {
                    continue;
                }

                overlap += ov;
                if (w.Start > o.Start && mineFirst[i])
                {
                    made++;
                }
                else if (o.Start > w.Start && othersFirst[k])
                {
                    received++;
                }
            }
        }

        return (made, received, overlap);
    }

    /// <summary>Marks the first word of every speaking run (a run breaks on a pause of at least the threshold).</summary>
    private static bool[] FirstOfRun(List<MappedWord> words)
    {
        var flags = new bool[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            flags[i] = i == 0 || words[i].Start - words[i - 1].End >= PauseThresholdSeconds;
        }

        return flags;
    }

    private static List<string> Sentences(List<MappedWord> words)
    {
        var sentences = new List<string>();
        var current = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            current.Add(words[i].Text);
            var endsWithPunct = words[i].Text.EndsWith('.') || words[i].Text.EndsWith('?') || words[i].Text.EndsWith('!');
            var longGap = i + 1 < words.Count && words[i + 1].Start - words[i].End >= SentenceGapSeconds;
            if (endsWithPunct || longGap)
            {
                sentences.Add(string.Join(' ', current));
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            sentences.Add(string.Join(' ', current));
        }

        return sentences;
    }

    private static int CountQuestionOpeners(List<string> sentences) =>
        sentences.Count(static s => !s.EndsWith('?') && FirstToken(s) is "why" or "how" or "what" or "isn't" or "aren't" or "don't");

    /// <summary>The opening word of a sentence, which is what tells a rhetorical question from a real one.</summary>
    private static string? FirstToken(string sentence)
    {
        var tokens = Lexicon.Tokenize(sentence);
        return tokens.Count == 0 ? null : tokens[0];
    }

    private static int CountPhrases(string text, IEnumerable<string> phrases)
    {
        var lower = " " + string.Join(' ', Lexicon.Tokenize(text)) + " ";
        var count = 0;
        foreach (var phrase in phrases)
        {
            var needle = " " + phrase + " ";
            var idx = 0;
            while ((idx = lower.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length - 1;
            }
        }

        return count;
    }

    private static List<string> RepeatedPhrases(IReadOnlyList<string> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            var tri = $"{tokens[i]} {tokens[i + 1]} {tokens[i + 2]}";
            counts[tri] = counts.GetValueOrDefault(tri) + 1;
        }

        return [.. counts.Where(kv => kv.Value >= 2 && Array.Exists(kv.Key.Split(' '), t => !Lexicon.StopWords.Contains(t)))
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(5).Select(kv => $"{kv.Key} (×{kv.Value})")];
    }

    private static double Round(double v) => Math.Round(v, 2);
}
