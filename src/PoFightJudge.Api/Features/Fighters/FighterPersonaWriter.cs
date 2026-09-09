using System.Globalization;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Fighters;

/// <summary>
/// Turns a real person's fights into a persona the CPU and 1P channels can put in a seat. It runs once the judge
/// has ruled, for every human side of the match. A failure here costs them the persona and nothing else: the
/// ruling and the record are already written by the time this is called.
/// </summary>
public interface IFighterPersonaWriter
{
    Task WriteAsync(MatchDto match, AnalysisReportDto report, CancellationToken ct = default);
}

/// <summary>
/// The persona is read from evidence — everything they have said in every debate they have ever spoken in, the
/// judge's read of them, and the style profile across every fight so far — through the same schema the
/// invent-a-character generator uses, so the editor, the pickers and the round prompts need nothing new. It is
/// rewritten after every fight: the fights are the truth about how somebody argues, and a card that stopped at
/// their first one would be wrong by their third.
/// </summary>
/// <remarks>
/// Two things are never touched. An authored cast member who happens to share the initials keeps their row — that
/// persona was written by a person, and a stranger typing the same three letters into 2P does not get to replace
/// it. And a face somebody added by hand survives the rewrite, because a fight says nothing about what they look
/// like. The identity is theirs too: the tag they argue under and the name they gave the roster, never a name the
/// model made up.
/// </remarks>
public sealed partial class FighterPersonaWriter(
    IFighterRepository fighters,
    IFighterResultRepository results,
    IFighterWordsRepository words,
    IProfileRepository profiles,
    IGeminiText gemini,
    GeminiModelOptions models,
    ILogger<FighterPersonaWriter> logger) : IFighterPersonaWriter
{
    public const string Operation = "fighter-persona";

    public async Task WriteAsync(MatchDto match, AnalysisReportDto report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(report);

        if (match.Side1.IsHuman)
        {
            await WriteOneAsync(match.Side1.Id, report.Player1.Assessment, ct);
        }

        if (match.Side2.IsHuman)
        {
            await WriteOneAsync(match.Side2.Id, report.Player2.Assessment, ct);
        }
    }

    private async Task WriteOneAsync(string tag, PlayerAssessmentDto assessment, CancellationToken ct)
    {
        try
        {
            var id = FighterId.From(tag);
            var fighter = await fighters.GetAsync(id, ct);
            if (fighter is null)
            {
                // Forgotten between the fight and its reading. A persona for somebody who asked to be forgotten is not written.
                LogNoFighter(logger, tag);
                return;
            }

            var personaId = ProfileId.From(tag);
            var existing = await profiles.GetByIdAsync(personaId, ct);
            if (existing is { FromFights: false })
            {
                LogKeptCast(logger, tag);
                return;
            }

            var style = StyleProfileBuilder.Build(tag, await results.ListForAnyoneAsync(id, ct));

            // Everything they have ever said, this debate included: the words are the evidence, and a person who
            // has argued five times has told us five times as much about how they argue.
            var corpus = SpokenCorpus.From(await words.ListAsync(id, ct));

            CreateProfileRequest draft = new();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var request = new GeminiTextRequest(
                    BuildPrompt(fighter, style, assessment, corpus, attempt),
                    models.Profile,
                    Operation,
                    ProfileGenerator.Schema,
                    Temperature: 0.7,
                    MaxOutputTokens: ProfileGenerator.MaxOutputTokens,
                    ThinkingLevel: "low");
                draft = ProfileGenerator.Parse(await gemini.GenerateAsync(request, ct), fighter.Role);
                if (ProfileGenerator.IsComplete(draft))
                {
                    break;
                }
            }

            draft.Initials = tag;
            draft.Name = fighter.DisplayName;
            draft.TtsSettings = ProfileGenerator.VoiceFor(fighter.Role, fighter.DisplayName);

            var persona = draft.ToDomain();
            persona.MarkFromFights();
            persona.UpdateFacePic(existing?.FacePic);
            await profiles.UpsertAsync(persona, ct);
            LogWritten(logger, tag, fighter.Role, style.Debates, corpus.Debates);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogFailed(logger, tag, ex);
        }
    }

    /// <summary>
    /// The instructions never change; the evidence does. The split keeps the constant half cacheable across every
    /// persona this ever writes, the same way the generator's prompt is split.
    /// </summary>
    public static GeminiPrompt BuildPrompt(Fighter fighter, StyleProfileDto style, PlayerAssessmentDto assessment, SpokenCorpus corpus, int attempt)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(corpus);

        const string system = """
            You are writing the persona card of a REAL person for a satirical "married couple argument simulator",
            from evidence of how they actually argued. This is not an invented character: everything you write must
            be a fair reading of the evidence. Where the evidence is silent (age, occupation, the sliders), choose
            the most plausible reading of how they talk and say nothing that contradicts what they said. Keep their
            own pet phrases and their own positions — commonArguments and philosophy should sound like them, in
            their register, and may quote them. Never moralise about them and never soften them.

            Fill the JSON schema. Every field is required. Age 24-68 (a guess is fine). Sliders are 0-100.
            """;

        var fallacies = assessment.Fallacies.Count == 0
            ? "(none noted)"
            : string.Join("\n", assessment.Fallacies.Select(f => $"  • {f.Name}: \"{f.Quote}\""));
        var tips = assessment.CoachingTips.Count == 0 ? "(none)" : string.Join("; ", assessment.CoachingTips);
        var tone = assessment.ToneDescriptors.Count == 0 ? "(not described)" : string.Join(", ", assessment.ToneDescriptors);
        var phrases = style.Phrases.Count == 0 ? "(none yet)" : string.Join(" / ", style.Phrases);
        var nudge = attempt == 0
            ? string.Empty
            : "\nThe previous attempt was too thin — this time fill EVERY field with specific detail drawn from what they said.";

        var user = $"""
            Write the persona of {fighter.DisplayName} (tag {fighter.Tag}), who argues as the {fighter.Role.ToString().ToLowerInvariant()}.

            {Evidence(corpus)}

            THE JUDGE'S READ OF THEM:
              • tone: {tone}
              • best line: "{assessment.BestMomentQuote}"
              • weakest moment: "{assessment.WorstMomentQuote}"
              • fallacies they leaned on:
            {fallacies}
              • language level: {assessment.Cefr} — {assessment.CefrJustification}
              • logic {assessment.Logic}/10, persuasiveness {assessment.Persuasiveness}/10, aggression {assessment.Aggression}/10, listening {assessment.Listening}/10, politeness {assessment.Politeness}/10
              • advice they were given: {tips}

            ACROSS EVERY ARGUMENT SO FAR ({style.Debates.ToString(CultureInfo.InvariantCulture)} including this one):
              • {style.Digest}
              • phrases they keep coming back to: {phrases}{nudge}
            """;

        return new GeminiPrompt(system, user);
    }

    /// <summary>
    /// Their own words, laid out for the model. Everything they have ever said, oldest first, so it can hear a
    /// person rather than a night — and it is told which one is the fight that was just judged.
    /// </summary>
    private static string Evidence(SpokenCorpus corpus) =>
        corpus.IsEmpty
            ? "WHAT THEY SAID: nothing of theirs was transcribed. Go on the judge's read of them alone."
            : $"""
                EVERYTHING THEY HAVE SAID, across {corpus.Debates.ToString(CultureInfo.InvariantCulture)} argument(s), oldest first — their own words:
                {corpus.Text}
                """;

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information, Message = "Persona {Tag}: written as the {Role} from {Debates} fight(s) and {Spoken} transcribed one(s)")]
    private static partial void LogWritten(ILogger logger, string tag, ProfileRole role, int debates, int spoken);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Information, Message = "Persona {Tag}: an authored cast member owns these initials; left alone")]
    private static partial void LogKeptCast(ILogger logger, string tag);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Information, Message = "Persona {Tag}: the fighter no longer exists; nothing written")]
    private static partial void LogNoFighter(ILogger logger, string tag);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Warning, Message = "Persona {Tag}: could not be written; the ruling and the record are unaffected")]
    private static partial void LogFailed(ILogger logger, string tag, Exception ex);
}
