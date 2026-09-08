using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Watch;

/// <summary>
/// The line rules the round prompt is built from. They live apart from the prompt builder because which set applies
/// is a domain decision — who the opponent is — and because the difference between them was a real bug, not a
/// stylistic choice.
/// </summary>
public static class WatchRules
{
    /// <summary>
    /// What the judge says when neither side won. It is a word rather than a blank because a response schema may
    /// not carry an empty enum value — Gemini answers 400 "enum[2]: cannot be empty" and the whole ruling fails.
    /// It never leaves the parser: <c>WatchAi.NormalizeWinner</c> turns it back into an empty winner.
    /// </summary>
    public const string NoWinner = "NEITHER";

    /// <summary>
    /// Rules for an ordinary AI-versus-AI turn. Anchoring the line on the speaker's own personality is what keeps two
    /// generated characters distinguishable from each other.
    /// </summary>
    public const string StockLineRules = """
        - The line MUST come from this character: their likes/dislikes, their usual arguments, their philosophy.
        - At least one slider MUST visibly affect the line — a patient spouse does not spit the same sentence an impatient one does.
        - Answer the argument as it stands; do not restart it from the top.
        - One to three sentences. Spoken out loud, mid-argument. No stage directions, no quotation marks, no name prefix.
        """;

    /// <summary>
    /// Rules for a turn whose opponent is the live human (SELF).
    /// </summary>
    /// <remarks>
    /// The stock rules anchor a line on the speaker's own likes and dislikes and require a slider to visibly drive
    /// it. That is reasonable when both spouses have profiles, but against a deliberately blank human profile it is
    /// the only branch the model can satisfy — so it monologues about its own habits and never answers what was
    /// said. Told "you left the freezer open all night and everything is ruined", the wife replied about her spice
    /// rack, the kids and screen time. These rules put the player's words first; deflection is still allowed,
    /// because refusing to answer is a legitimate move in an argument, but only out loud.
    /// </remarks>
    public const string HumanReplyLineRules = """
        - ANSWER THEIR LAST LINE. The player said those words out loud and is waiting to hear them engaged.
        - Quote, deny, mock, concede or turn it back on them — but engage with what was actually said.
        - You may refuse to answer and change the subject, but do it out loud and on purpose, never by ignoring it.
        - Stay in character while you do it: the personality colours HOW you answer, it is not the subject.
        - One to three sentences. Spoken out loud, mid-argument. No stage directions, no quotation marks, no name prefix.
        """;

    /// <summary>Which rules apply for this turn.</summary>
    public static string LineRulesFor(bool opponentIsHuman) => opponentIsHuman ? HumanReplyLineRules : StockLineRules;

    /// <summary>The registers a line may carry. Defined in Shared, because the tension meter on the play screen reads the same set.</summary>
    public static IReadOnlyList<string> Moods => WatchTurns.Moods;

    /// <inheritdoc cref="WatchTurns.NormalizeMood" />
    public static string NormalizeMood(string? mood) => WatchTurns.NormalizeMood(mood);

    /// <inheritdoc cref="WatchTurns.NextSpeaker" />
    public static string NextSpeaker(int linesSoFar, bool afterSlap) => WatchTurns.NextSpeaker(linesSoFar, afterSlap);

    /// <inheritdoc cref="WatchTurns.IsComplete" />
    public static bool IsComplete(IEnumerable<WatchRound> rounds)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        return WatchTurns.IsComplete(rounds.Select(r => r.Speaker));
    }
}
