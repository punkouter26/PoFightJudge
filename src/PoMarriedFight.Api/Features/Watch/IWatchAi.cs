using PoMarriedFight.Api.Features.Profiles;

namespace PoMarriedFight.Api.Features.Watch;

/// <summary>One generated line and the register it was delivered in.</summary>
public sealed record ArgumentResult(string Text, string Mood);

/// <summary>
/// One piece of a streamed line. <paramref name="Delta"/> is new displayable text; the closing part carries
/// <paramref name="Final"/> instead, once the whole answer has been parsed.
/// </summary>
public readonly record struct ArgumentStreamPart(string? Delta, ArgumentResult? Final = null);

/// <summary>The judge's ruling. <paramref name="Winner"/> is one side's initials, or empty when neither was called.</summary>
public sealed record JudgeVerdictResult(string Winner, string Verdict, int HusbandScore, int WifeScore);

/// <summary>
/// The WATCH engine's view of the model: prompts in, parsed results out. It sits above <c>IGeminiText</c> so the
/// endpoints depend on the game's vocabulary rather than on a transport, and so the fake substitutes here — where a
/// whole match can be played without a key — rather than at the HTTP layer.
/// </summary>
public interface IWatchAi
{
    bool IsFake { get; }

    Task<ArgumentResult> GenerateArgumentAsync(
        Profile husband,
        Profile wife,
        IReadOnlyList<RoundContext> history,
        string speaker,
        string attitude,
        bool wasSlapped,
        string? interjection = null,
        string? topic = null,
        CancellationToken ct = default);

    /// <summary>The same line, delivered as it is written. Deltas are display-clean: a partial escape sequence is held back rather than shown.</summary>
    IAsyncEnumerable<ArgumentStreamPart> StreamArgumentAsync(
        Profile husband,
        Profile wife,
        IReadOnlyList<RoundContext> history,
        string speaker,
        string attitude,
        bool wasSlapped,
        string? interjection = null,
        string? topic = null,
        CancellationToken ct = default);

    Task<JudgeVerdictResult> JudgeAsync(Profile husband, Profile wife, IReadOnlyList<RoundContext> rounds, CancellationToken ct = default);
}
