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

    /// <summary>Taking it back, or giving it up. The counterweight to <see cref="Blame"/>.</summary>
    public static IReadOnlyList<string> Apologies { get; } =
        ["sorry", "my bad", "my fault", "i apologise", "i apologize", "you're right", "youre right", "fair enough",
         "i was wrong", "i take that back", "point taken", "i shouldn't have", "i shouldnt have", "forgive me"];

    /// <summary>
    /// Aimed at a person rather than at what they did. Kept apart from <see cref="Profanity"/> on purpose: swearing
    /// and cruelty are not the same habit, and somebody can do a great deal of one without any of the other.
    /// </summary>
    public static IReadOnlySet<string> NameCalling { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "lazy", "selfish", "childish", "immature", "ridiculous", "pathetic", "stupid", "idiot", "idiotic", "moron",
        "useless", "hopeless", "insane", "crazy", "obsessed", "dramatic", "petty", "spoiled", "clueless", "toxic",
    };

    /// <summary>How hot it is said, independent of what is being said.</summary>
    public static IReadOnlySet<string> Intensifiers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "very", "so", "really", "absolutely", "totally", "completely", "utterly", "extremely", "incredibly",
        "seriously", "insanely", "ridiculously", "way", "super", "damn", "freaking",
    };

    /// <summary>A question with an edge on it. Anything else that ends in a question mark is taken as a real one.</summary>
    public static IReadOnlyList<string> ChallengeOpeners { get; } =
        ["why", "how come", "what do you mean", "are you serious", "do you really", "are you kidding",
         "how is that", "since when", "what is that supposed", "what's that supposed", "who said"];

    /// <summary>
    /// Pointing at a person, not at a problem. Second person plus an absolute is the shape of it — the raw
    /// second-person ratio counts "you" in "you and I agreed", which is the opposite of an accusation.
    /// </summary>
    public static IReadOnlyList<string> Blame { get; } =
        ["you always", "you never", "you didn't", "you didnt", "you don't", "you dont", "you won't", "you wont",
         "you can't", "you cant", "you refuse", "your fault", "you made me", "you forgot", "you promised",
         "you said you", "you lied", "you ignored", "you keep", "you have to stop"];

    /// <summary>Reaching for the past.</summary>
    public static IReadOnlyList<string> PastMarkers { get; } =
        ["was", "were", "had", "did", "went", "said", "told", "came", "got", "took", "gave", "made", "knew", "used to",
         "last time", "yesterday", "back then", "already"];

    /// <summary>Reaching for what happens next.</summary>
    public static IReadOnlyList<string> FutureMarkers { get; } =
        ["will", "'ll", "going to", "gonna", "shall", "next time", "from now on", "tomorrow", "later"];

    /// <summary>Staying in the room.</summary>
    public static IReadOnlyList<string> PresentMarkers { get; } =
        ["is", "are", "am", "do", "does", "now", "right now", "today", "currently", "this time"];

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
