using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Watch;

/// <summary>One line already spoken, as the prompt builders read it.</summary>
public sealed record RoundContext(string Speaker, string Text, string Mood);

/// <summary>
/// Builds the prompt for one line of dialogue.
/// </summary>
/// <remarks>
/// The layout is load-bearing, not cosmetic. Roughly 1,200 tokens — both profile blocks and the whole rule set — are
/// identical for every turn of a match, and providers only bill that once if the repeated part is a genuine shared
/// prefix. An earlier layout led with a speaker/opponent pair that swapped places every turn, so consecutive calls
/// diverged within their first hundred characters and none of the boilerplate could ever be cached. That failure is
/// invisible: the prompt reads correctly, the model answers correctly, and the only symptom is the bill. Hence the
/// fixed husband-first order and the rule that every per-turn value lives under CURRENT STATE.
/// </remarks>
public static class RoundPromptBuilder
{
    /// <summary>
    /// Hard word ceiling on one line. Asked for "2–4 sentences", the model returned 66–97 words per line — obedient
    /// on sentence count, with 25-word sentences — which rendered as 22–26 seconds of speech each and made a
    /// six-round match 143 seconds of audio. The cap is stated in words because sentences are not a unit a model
    /// holds to a length, and it is repeated in the output contract because that is the instruction closest to the
    /// thing being generated.
    /// </summary>
    public const int MaxLineWords = 45;

    /// <summary>
    /// Spliced into the participant block for the live human. A model handed a blank profile will happily invent one,
    /// producing a spouse who argues against traits the player never chose.
    /// </summary>
    public const string HumanOpponentDirective = """

              THIS OPPONENT IS A REAL PERSON speaking live into a microphone. Their profile is
                intentionally empty - you know NOTHING about them beyond the words in ARGUMENT
                HISTORY. Do NOT invent traits, a name, a job, or a backstory for them, and do not
                claim to know their habits. Attack ONLY what they actually said: quote it back,
                twist it, find the hole in it. Keep your line short and punchy so they can reply.
        """;

    public static GeminiPrompt Build(
        Profile husband,
        Profile wife,
        IReadOnlyList<RoundContext> history,
        string speaker,
        string attitude,
        bool wasSlapped,
        string? interjection = null,
        string? topic = null)
    {
        ArgumentNullException.ThrowIfNull(husband);
        ArgumentNullException.ThrowIfNull(wife);
        ArgumentNullException.ThrowIfNull(history);

        var speakingHusband = string.Equals(speaker, WatchRound.Husband, StringComparison.OrdinalIgnoreCase);
        var self = speakingHusband ? husband : wife;
        var other = speakingHusband ? wife : husband;

        var system = $$"""
            You are simulating a married couple argument. You write the next line of dialogue for
            exactly ONE spouse — the one named in CURRENT STATE — and nothing else.

            Stay in character. Be creative, specific, and funny. Reference concrete details from the
            participant profiles — never speak in generic platitudes.

            Tailor your attack: pick on your opponent's likes, rub THEIR dislikes in their face, or
            contradict THEIR philosophy. Quote a specific like or dislike verbatim when natural.

            LINE RULES:
            {{WatchRules.LineRulesFor(other.IsHuman)}}

            OUTPUT — respond with ONLY a JSON object (no markdown fences): {"line": "...", "mood": "..."}
              • "line": heated dialogue for the named speaker ONLY.
                 - HARD LIMIT: {{MaxLineWords}} WORDS. Count them. A real person interrupts; nobody
                   delivers a paragraph mid-argument. Short and cutting beats long and thorough.
                 - End the line with a short stage cue in parentheses — (snaps), (coldly), (seething).
              • "mood": exactly one of: {{string.Join(", ", WatchRules.Moods)}} — the register of the line.
            """;

        // Participants render in a fixed order so the block is byte-identical every turn, whoever is speaking.
        var participants = $"{RenderParticipant(husband)}\n{RenderParticipant(wife)}";

        // The chosen topic is constant for the whole match, so it sits with the stable block rather than in
        // CURRENT STATE, where it would still be constant but would shorten the cacheable prefix for nothing.
        var topicBlock = string.IsNullOrWhiteSpace(topic)
            ? string.Empty
            : $"\nTHE FIGHT IS ABOUT: \"{topic.Trim()}\" — every line must stay on this exact topic. "
                + "Attack the opponent THROUGH this topic; do not drift to a different fight.\n";

        var historyText = history.Count == 0
            ? "This is the opening statement."
            : string.Join("\n", history.Select(r => $"[{r.Speaker.ToUpperInvariant()}]: {r.Text}"));

        var heckle = Interjections.Find(interjection)
            ?? (wasSlapped ? Interjections.Find(Interjections.SlapKey) : null);

        var user = $$"""
            PARTICIPANTS:
            {{participants}}
            {{topicBlock}}
            ARGUMENT HISTORY (most recent last):
            {{historyText}}

            CURRENT STATE:
              YOU ARE SPEAKING AS: the {{(speakingHusband ? "husband" : "wife")}}, {{self.Name ?? self.Initials}}.
              Your opponent is the {{other.Role.ToString().ToLowerInvariant()}}, {{other.Name ?? other.Initials}}.
              Attitude injected this turn: {{attitude}}
              {{heckle?.PromptDirective ?? string.Empty}}
            """;

        return new GeminiPrompt(system, user);
    }

    /// <summary>
    /// One spouse's full profile block. Both participants get the same detail: an earlier layout gave the opponent a
    /// condensed one-liner purely because the blocks swapped, and the condensed version was the one the model had to
    /// aim its attacks at.
    /// </summary>
    public static string RenderParticipant(Profile p)
    {
        ArgumentNullException.ThrowIfNull(p);

        return $"""
            ╔══ {p.Role.ToString().ToUpperInvariant()}: {p.Name ?? p.Initials} (age {p.Age?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}, {p.Occupation ?? "unemployed"}) ══╗

              Likes:             {OrDash(p.Likes)}
              Dislikes:          {OrDash(p.Dislikes)}
              Common arguments:  {OrDash(p.CommonArguments)}
              Philosophy / motto:{OrDash(p.Philosophy)}

              How they argue (0 = opposite extreme, 50 = neutral, 100 = full extreme):
                logic-vs-emotion   {p.LogicVsEmotion,3}  ({SliderLabel("logic", p.LogicVsEmotion)})
                patience           {p.Patience,3}  ({SliderLabel("patience", p.Patience)})
                holds grudges      {p.HoldsGrudges,3}  ({SliderLabel("grudges", p.HoldsGrudges)})
                jealousy           {p.Jealousy,3}  ({SliderLabel("jealousy", p.Jealousy)})
            {(p.IsHuman ? HumanOpponentDirective : string.Empty)}
            """;
    }

    /// <summary>
    /// Maps a 0–100 slider to a short directional phrase the model can act on. The logic axis reads the way the UI
    /// draws it — higher means MORE emotional — because an inverted table once described a persona dialled all the
    /// way to Emotion as "coldly logical".
    /// </summary>
    public static string SliderLabel(string axis, int v) => axis switch
    {
        "logic" => v < 35 ? "coldly logical" : v < 50 ? "leans logical" : v < 65 ? "leans emotional" : "very emotional",
        "jealousy" => v < 35 ? "trusting" : v < 50 ? "secure" : v < 65 ? "watchful" : "extremely possessive",
        "grudges" => v < 35 ? "instantly forgives" : v < 50 ? "moves on" : v < 65 ? "keeps score" : "never forgets",
        "patience" => v < 35 ? "hair-trigger temper" : v < 50 ? "short fuse" : v < 65 ? "even-keeled" : "saintly patience",
        _ => "neutral",
    };

    public static string OrDash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();
}
