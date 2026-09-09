using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Watch;

/// <summary>
/// Picks the attitude a spouse opens a turn with, from their own personality plus where the argument currently
/// stands. This is what makes the sliders audible: with a hard-coded "angry" every speaker in every round opened at
/// the same temperature, and a persona with saintly patience and a Fawn stress response fought exactly like one with
/// a hair-trigger temper.
/// </summary>
/// <remarks>
/// Deliberately deterministic — no randomness. The same matchup picks the same attitudes twice, which is what makes
/// the sliders legible ("she opens cold because her patience is 90") and what lets the client's speculative prefetch
/// of the next round stay valid. Variety comes from the argument itself: the round number escalates the heat and the
/// opponent's last mood pushes it up or down.
/// </remarks>
public static class AttitudeSelector
{
    /// <summary>Heat below which a speaker is not really fighting yet.</summary>
    public const int CalmCeiling = 20;

    /// <summary>
    /// One attitude word for this turn. It is injected verbatim into the prompt's current-state block, so it stays a
    /// short evocative adjective rather than a sentence.
    /// </summary>
    /// <param name="speaker">The persona about to talk.</param>
    /// <param name="lastOpponentMood">Mood of the opponent's most recent line, or null on the opening turn.</param>
    /// <param name="roundNumber">1-based round; later rounds run hotter.</param>
    /// <param name="wasSlapped">True when the speaker was just slapped.</param>
    public static string Select(Profile speaker, string? lastOpponentMood, int roundNumber, bool wasSlapped)
    {
        ArgumentNullException.ThrowIfNull(speaker);

        // A slap is not a mood the personality gets a vote on.
        if (wasSlapped)
        {
            return "furious";
        }

        // Sarcasm and stonewalling are styles, so they are chosen at the bands where a person is annoyed but still in
        // control. Past those bands everyone is simply shouting, and a "sarcastic" cue would flatten the climax.
        return Heat(speaker, lastOpponentMood, roundNumber) switch
        {
            >= 85 => "furious",
            >= 72 => speaker.HoldsGrudges >= 70 ? "hateful" : "hostile",

            // Sarcasm belongs to the cold ones. It used to be its own switch, and the switch was the better signal;
            // the logic end of the axis is the nearest honest stand-in, because dry is what that end sounds like.
            >= 58 => speaker.LogicVsEmotion <= 40 ? "sarcastic" : "angry",
            >= 45 => "frustrated",
            >= 32 => Withdraws(speaker) ? "passive-aggressive" : "smug",
            >= CalmCeiling => "humble",
            _ => Concedes(speaker) ? "apologetic" : "calm",
        };
    }

    /// <summary>How hot this speaker is right now, 0–100. Every term is additive and named, so a surprising attitude traces back to the slider that caused it.</summary>
    public static int Heat(Profile speaker, string? lastOpponentMood, int roundNumber)
    {
        ArgumentNullException.ThrowIfNull(speaker);

        // Patience is the spine of it: an impatient spouse starts near boiling.
        var heat = 100 - speaker.Patience;

        // Someone who never lets anything go arrives already annoyed.
        if (speaker.HoldsGrudges >= 65)
        {
            heat += 10;
        }

        // Jealousy used to sit in the editor doing nothing to the argument. It is one of the four dials that
        // survived, so it now costs what the stress response used to: a suspicious spouse starts further up.
        heat += (speaker.Jealousy - 50) / 5;

        // Emotional speakers run hotter than analytical ones, on the same axis the UI shows; ±10 at the extremes.
        heat += (speaker.LogicVsEmotion - 50) / 5;

        // Arguments escalate. Round 1 is the opener, round 3 is where it lands.
        heat += Math.Max(0, roundNumber - 1) * 12;

        // And they escalate at each other, rather than reciting a fixed arc regardless of what was just said.
        heat += (lastOpponentMood ?? string.Empty).ToLowerInvariant() switch
        {
            "furious" or "hateful" => 14,
            "hostile" or "angry" => 8,
            "smug" => 6,
            "apologetic" => -12,
            "calm" or "peaceful" => -8,
            "sad" => -6,
            _ => 0,
        };

        return Math.Clamp(heat, 0, 100);
    }

    /// <summary>
    /// The spouses who go quiet rather than loud. A grudge is the quiet form of anger — somebody keeping score does
    /// not shout, they wait — so the dial that measures it decides who stonewalls and who gets smug about it.
    /// </summary>
    private static bool Withdraws(Profile speaker) => speaker.HoldsGrudges >= 55;

    /// <summary>Who says sorry before anybody asks: patient, and not keeping score.</summary>
    private static bool Concedes(Profile speaker) => speaker.Patience >= 70 && speaker.HoldsGrudges <= 30;
}
