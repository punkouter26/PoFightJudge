using System.Text.RegularExpressions;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>Word lists and tokenisation used by <see cref="SpeechMetrics"/>.</summary>
public static partial class Lexicon
{
    public static IReadOnlySet<string> Fillers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "um", "uh", "er", "ah", "hmm", "like", "basically", "literally", "actually", "so", "okay", "right", "well",
    };

    public static IReadOnlyList<string> FillerPhrases { get; } = ["you know", "i mean", "sort of", "kind of"];

    public static IReadOnlyList<string> Hedges { get; } = ["i think", "i guess", "maybe", "perhaps", "probably", "i feel like", "sort of", "kind of", "possibly", "i suppose", "arguably"];

    public static IReadOnlySet<string> Absolutes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "always", "never", "everyone", "nobody", "everything", "nothing", "all", "none", "impossible", "definitely", "obviously", "totally", "completely",
    };

    public static IReadOnlySet<string> Profanity { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "damn", "hell", "crap", "shit", "fuck", "fucking", "ass", "bitch", "bastard", "bullshit",
    };

    public static IReadOnlySet<string> FirstPerson { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "i", "me", "my", "mine", "myself", "i'm", "i've", "i'd", "i'll" };

    public static IReadOnlySet<string> SecondPerson { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "you", "your", "yours", "yourself", "you're", "you've", "you'd", "you'll" };

    public static IReadOnlySet<string> StopWords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "of", "to", "in", "on", "at", "for", "with", "is", "are", "was", "were", "be", "been", "it", "it's",
        "that", "this", "these", "those", "i", "you", "he", "she", "we", "they", "me", "my", "your", "our", "their", "not", "no", "yes", "do", "does",
        "did", "have", "has", "had", "so", "if", "then", "than", "as", "just", "very", "really", "there", "here", "what", "which", "who", "how", "why",
        "when", "where", "can", "could", "would", "should", "will", "about", "like", "um", "uh", "its", "im", "dont", "thats", "because",
    };

    // Letters plus straight or curly apostrophes and hyphens inside a word.
    [GeneratedRegex(@"[A-Za-z][A-Za-z'’-]*", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordPattern();

    public static IReadOnlyList<string> Tokenize(string text) =>
        [.. WordPattern().Matches(text).Select(m => m.Value.Replace('’', '\'').ToLowerInvariant())];
}
