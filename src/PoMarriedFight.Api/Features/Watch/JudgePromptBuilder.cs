using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Api.Features.Profiles;

namespace PoMarriedFight.Api.Features.Watch;

/// <summary>
/// Builds the prompt that rules on a finished match. The criteria and the output contract are identical for every
/// match ever judged, so they are the system half; only the participants and the transcript vary.
/// </summary>
public static class JudgePromptBuilder
{
    /// <summary>
    /// Kept as its own constant so a test can pin it. The regression it guards against: a judge that rewarded persona
    /// loyalty declared a winner because he "stayed remarkably true to his profile" rather than because his argument
    /// had merit.
    /// </summary>
    public const string Criteria = """
        You are an impartial judge evaluating a marital argument ON THE LOGICAL MERITS of each
        spouse's case. Emotion, tone, and persona consistency are explicitly NOT criteria —
        decide which spouse made the stronger *argument*, not the louder or more
        "in-character" one.

        Score each line on:
        1. Logical validity — does the reasoning actually support the claim?
        2. Relevance — does it address the opponent's last point, or dodge it?
        3. Evidence — concrete examples, specifics, facts vs. vague feelings and insults.
        4. Rebuttal quality — engages the opponent's strongest point, not their weakest.
        Deduct for logical fallacies (ad hominem, strawman, whataboutism, appeal to
        emotion as a substitute for argument), deflection, and personal attacks that
        don't advance the case.
        """;

    public const string TiebreakerAndOutput = """
        Pick whichever spouse argued more rigorously. Do NOT favour one side for tone,
        volume, or how closely they matched their persona. The tiebreaker applies ONLY when
        argument quality is genuinely even, and goes to whoever raised the more relevant
        concrete point last.

        Respond with a JSON object carrying the winner's initials, a two or three sentence
        verdict naming the specific strengths that won it and the specific fallacies or
        evasions that lost it, and a 0-100 score for each spouse.
        """;

    public static GeminiPrompt Build(Profile husband, Profile wife, IReadOnlyList<RoundContext> rounds)
    {
        ArgumentNullException.ThrowIfNull(husband);
        ArgumentNullException.ThrowIfNull(wife);
        ArgumentNullException.ThrowIfNull(rounds);

        var transcript = string.Join("\n", rounds.Select(r => $"[{r.Speaker.ToUpperInvariant()}]: {r.Text}"));

        var user = $"""
            PARTICIPANTS:
            - Husband {husband.Initials} — {Persona(husband)}
            - Wife {wife.Initials} — {Persona(wife)}

            TRANSCRIPT:
            {transcript}
            """;

        return new GeminiPrompt($"{Criteria}\n\n{TiebreakerAndOutput}", user);
    }

    /// <summary>A one-line personality digest, so a verdict can describe who each spouse is without re-sending the whole profile block.</summary>
    public static string Persona(Profile p)
    {
        ArgumentNullException.ThrowIfNull(p);

        var bits = new List<string>
        {
            $"{p.Name ?? p.Initials}, {p.Occupation ?? "unemployed"}",
            RoundPromptBuilder.SliderLabel("logic", p.LogicVsEmotion),
            RoundPromptBuilder.SliderLabel("patience", p.Patience),
            RoundPromptBuilder.SliderLabel("grudges", p.HoldsGrudges),
        };

        if (p.StressResponse is { } stress)
        {
            bits.Add($"{stress}-under-stress");
        }

        var traits = RoundPromptBuilder.FormatTraits(p);
        if (!string.Equals(traits, "—", StringComparison.Ordinal))
        {
            bits.Add(traits);
        }

        if (!string.IsNullOrWhiteSpace(p.Philosophy))
        {
            bits.Add($"motto \"{p.Philosophy.Trim()}\"");
        }

        return string.Join("; ", bits);
    }
}
