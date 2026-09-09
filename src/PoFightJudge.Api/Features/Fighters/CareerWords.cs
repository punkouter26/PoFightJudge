using System.Globalization;
using System.Text.RegularExpressions;
using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Fighters;

/// <summary>
/// A whole speaking history, counted. <see cref="SpeechMetrics"/> reads one debate from a timed transcript; this
/// reads every debate from the stored words, which is all that survives once the audio is gone.
/// </summary>
/// <remarks>
/// Nothing here calls a model. These are the numbers a profile can show on every visit without costing anything,
/// which is the whole reason they are counted rather than asked for.
/// </remarks>
public static partial class CareerWords
{
    /// <summary>A phrase has to run this long to be a catchphrase; below it, everybody sounds like everybody.</summary>
    private const int MinPhraseWords = 3;

    private const int MaxPhraseWords = 5;

    /// <summary>Said in two separate arguments, months apart, is a habit. Said twice in one is a raised voice.</summary>
    private const int MinDebatesForCatchphrase = 2;

    /// <summary>Numbers, money, clock times, days and months: the shape of an argument that brought evidence.</summary>
    [GeneratedRegex(
        @"\b(\d+([:.]\d+)?|\$\d+|monday|tuesday|wednesday|thursday|friday|saturday|sunday|january|february|march|april|june|july|august|september|october|november|december|yesterday|tonight|o'clock|am|pm|twice|once)\b",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReceiptPattern();

    /// <summary>
    /// Everything one person has ever said, oldest first. Ordering matters twice over: the grade trend is only a
    /// trend in order, and a word is only new in the debate it first appears in.
    /// </summary>
    public static CareerWordsDto Compute(IEnumerable<SpokenDebate> debates)
    {
        ArgumentNullException.ThrowIfNull(debates);

        var said = debates.Where(d => !d.IsEmpty).OrderBy(d => d.At).ToList();
        if (said.Count == 0)
        {
            return CareerWordsDto.Empty;
        }

        var perDebate = said.Select(d => Lexicon.Tokenize(d.Said)).ToList();
        var tokens = perDebate.SelectMany(t => t).ToList();
        if (tokens.Count == 0)
        {
            return CareerWordsDto.Empty;
        }

        var text = string.Join(' ', said.Select(d => d.Said));
        // The marker lists are written with straight apostrophes; a transcriber will hand back curly ones.
        var lower = text.ToLowerInvariant().Replace('’', '\'');
        var sentences = Sentences(text);
        var syllables = tokens.Sum(Readability.Syllables);

        var (challenges, genuine) = Questions(sentences);
        var (past, present, future) = TimeOrientation(lower);

        return new CareerWordsDto(
            Debates: said.Count,
            Words: tokens.Count,
            ReadingGrade: Readability.FleschKincaidGrade(tokens.Count, Math.Max(1, sentences.Count), syllables),
            OffensivePercent: Rate(tokens.Count(Lexicon.Profanity.Contains), tokens.Count, 100),
            BlamePer100: Rate(CountPhrases(lower, Lexicon.Blame), tokens.Count, 100),
            ApologyPer100: Rate(CountPhrases(lower, Lexicon.Apologies), tokens.Count, 100),
            NameCallingPer100: Rate(tokens.Count(Lexicon.NameCalling.Contains), tokens.Count, 100),
            ReceiptsPer100: Rate(ReceiptPattern().Count(text), tokens.Count, 100),
            IntensifierPer100: Rate(tokens.Count(Lexicon.Intensifiers.Contains), tokens.Count, 100),
            PastShare: past,
            PresentShare: present,
            FutureShare: future,
            Catchphrases: Catchphrases(perDebate),
            VocabularySize: tokens.Distinct(StringComparer.Ordinal).Count(),
            NewWordsLatest: NewInLatest(perDebate),
            GradeTrend: [.. perDebate.Zip(said).Select(pair => GradeOf(pair.First, pair.Second.Said))],
            Challenges: challenges,
            GenuineQuestions: genuine);
    }

    private static double GradeOf(IReadOnlyList<string> tokens, string said)
    {
        if (tokens.Count == 0)
        {
            return 0;
        }

        var sentences = Math.Max(1, Sentences(said).Count);
        return Readability.FleschKincaidGrade(tokens.Count, sentences, tokens.Sum(Readability.Syllables));
    }

    /// <summary>A rate per <paramref name="per"/> words, so a long history compares with a short one.</summary>
    private static double Rate(int hits, int words, int per) =>
        words == 0 ? 0 : Math.Round((double)per * hits / words, 1);

    /// <summary>
    /// Phrases said across separate debates. Counted by how many debates contain them rather than how many times
    /// they appear, and longer phrases win: "at the end of the day" should not be reported as "at the end".
    /// </summary>
    private static IReadOnlyList<string> Catchphrases(List<IReadOnlyList<string>> perDebate)
    {
        if (perDebate.Count < MinDebatesForCatchphrase)
        {
            return [];
        }

        var debatesByPhrase = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tokens in perDebate)
        {
            foreach (var phrase in PhrasesIn(tokens))
            {
                debatesByPhrase[phrase] = debatesByPhrase.GetValueOrDefault(phrase) + 1;
            }
        }

        var shared = debatesByPhrase
            .Where(p => p.Value >= MinDebatesForCatchphrase)
            .OrderByDescending(p => p.Value)
            .ThenByDescending(p => p.Key.Length)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key)
            .ToList();

        // A five-word phrase drags its own three-word openings along with it; only the longest form is worth showing.
        var kept = new List<string>();
        foreach (var phrase in shared)
        {
            if (!kept.Any(k => k.Contains(phrase, StringComparison.Ordinal)))
            {
                kept.Add(phrase);
            }
        }

        return [.. kept.Take(5)];
    }

    /// <summary>Every phrase in one debate, counted once however often it was said: this measures spread, not volume.</summary>
    private static HashSet<string> PhrasesIn(IReadOnlyList<string> tokens)
    {
        var phrases = new HashSet<string>(StringComparer.Ordinal);
        for (var size = MinPhraseWords; size <= MaxPhraseWords; size++)
        {
            for (var i = 0; i + size <= tokens.Count; i++)
            {
                var window = tokens.Skip(i).Take(size).ToList();

                // A window that is nothing but stop words is grammar, not a catchphrase.
                if (window.TrueForAll(Lexicon.StopWords.Contains))
                {
                    continue;
                }

                phrases.Add(string.Join(' ', window));
            }
        }

        return phrases;
    }

    /// <summary>How much of their vocabulary they had never used before the last time they argued.</summary>
    private static int NewInLatest(List<IReadOnlyList<string>> perDebate)
    {
        if (perDebate.Count < 2)
        {
            return 0;
        }

        var before = perDebate.Take(perDebate.Count - 1).SelectMany(t => t).ToHashSet(StringComparer.Ordinal);
        return perDebate[^1].Distinct(StringComparer.Ordinal).Count(w => !before.Contains(w));
    }

    /// <summary>
    /// Which way the time-marked language points. Shares of the markers found rather than of every word: most words
    /// carry no tense at all, and dividing by those would report everybody as living entirely in the present.
    /// </summary>
    private static (double Past, double Present, double Future) TimeOrientation(string lower)
    {
        var past = CountPhrases(lower, Lexicon.PastMarkers);
        var present = CountPhrases(lower, Lexicon.PresentMarkers);
        var future = CountPhrases(lower, Lexicon.FutureMarkers);
        var total = past + present + future;
        return total == 0
            ? (0, 0, 0)
            : (Math.Round((double)past / total, 2), Math.Round((double)present / total, 2), Math.Round((double)future / total, 2));
    }

    /// <summary>
    /// A question that wanted an answer, against one that was a move. The opener decides it: "why did you" is an
    /// accusation with a question mark on the end, and counting it as curiosity would say the opposite of the truth.
    /// </summary>
    private static (int Challenges, int Genuine) Questions(IReadOnlyList<string> sentences)
    {
        var challenges = 0;
        var genuine = 0;
        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            var opener = trimmed.TrimStart('"', '\'', '(').ToLowerInvariant();
            var asked = trimmed.EndsWith('?');
            var challenging = Lexicon.ChallengeOpeners.Any(c => opener.StartsWith(c, StringComparison.Ordinal));

            if (challenging)
            {
                challenges++;
            }
            else if (asked)
            {
                genuine++;
            }
        }

        return (challenges, genuine);
    }

    /// <summary>
    /// Sentences as the transcriber punctuated them. A stored debate has no timings to fall back on, so where it
    /// put the full stops is the only sentence boundary there is.
    /// </summary>
    private static IReadOnlyList<string> Sentences(string text) =>
        [.. text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Select((s, i) => Restore(text, s, i))];

    /// <summary>Split throws the punctuation away, and whether it was a question mark is the whole point.</summary>
    private static string Restore(string text, string sentence, int index)
    {
        var at = text.IndexOf(sentence, StringComparison.Ordinal);
        if (at < 0 || at + sentence.Length >= text.Length)
        {
            return sentence;
        }

        var ended = text[at + sentence.Length];
        return ended is '.' or '!' or '?' ? sentence + ended.ToString(CultureInfo.InvariantCulture) : sentence;
    }

    private static int CountPhrases(string lower, IEnumerable<string> phrases) =>
        phrases.Sum(phrase => Occurrences(lower, phrase));

    /// <summary>
    /// Whole words only. A plain substring search counts "is" inside "rubbish" and "am" inside "example", which is
    /// enough on its own to report somebody who never used a tense marker as speaking entirely in the present.
    /// </summary>
    /// <remarks>
    /// The boundary is only required where the marker itself ends in a letter: <c>'ll</c> has to keep matching
    /// inside "you'll", and demanding a break before its apostrophe would find it nowhere.
    /// </remarks>
    private static int Occurrences(string haystack, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            var opensOnALetter = char.IsLetterOrDigit(needle[0]);
            var closesOnALetter = char.IsLetterOrDigit(needle[^1]);
            var end = at + needle.Length;

            var startsCleanly = !opensOnALetter || at == 0 || !char.IsLetterOrDigit(haystack[at - 1]);
            var endsCleanly = !closesOnALetter || end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);

            if (startsCleanly && endsCleanly)
            {
                count++;
            }

            at += needle.Length;
        }

        return count;
    }
}
