using System.Text.Json.Nodes;

namespace PoMarriedFight.Api.Features.Analysis;

/// <summary>
/// Gemini <c>responseSchema</c> for the judge calls. Property names are camelCase to match
/// <see cref="System.Text.Json.JsonNamingPolicy.CamelCase"/>. Every array is bounded: an unbounded
/// "every factual claim" list against a token ceiling truncates the JSON and fails the whole report.
/// </summary>
public static class AnalysisSchema
{
    /// <summary>The full report shape. Kept so the whole contract can be asserted in one place; the client issues it in parts.</summary>
    public static JsonObject Build() => Obj(
        ("player1", PlayerAssessment()),
        ("player2", PlayerAssessment()),
        ("overall", Overall()));

    /// <summary>Both assessments in one response — the shape of the single call that carries the audio.</summary>
    public static JsonObject BothAssessments() => Obj(
        ("player1", PlayerAssessment()),
        ("player2", PlayerAssessment()));

    public static JsonObject Overall() => Obj(
        ("winnerLogic", Enum("player1", "player2")),
        ("winnerCorrect", Enum("player1", "player2")),
        ("overall", Enum("player1", "player2")),
        ("reasons", Arr(Str("One short reason"), "Exactly three reasons", min: 3, max: 3)),
        ("summary", Str("One paragraph summarising the debate and the ruling")));

    public static JsonObject PlayerAssessment() => Obj(
        ("cefr", Enum("A1", "A2", "B1", "B2", "C1", "C2")),
        ("cefrJustification", Str("One sentence on why this CEFR level")),
        ("grammarErrorCount", Int("Number of grammatical errors heard")),
        ("grammarExamples", Arr(Str("A short quoted grammar slip"), "Up to 5 examples", max: 5)),
        ("vocabularySophistication", Int("1-10")),
        ("clarity", Int("1-10")),
        ("logic", Int("1-10 logical soundness")),
        ("fallacies", Arr(Obj(("name", Str("Fallacy name")), ("quote", Str("The words that committed it"))), "The clearest logical fallacies detected, at most 8", max: 8)),
        ("evidenceUse", Int("1-10")),
        ("rebuttalQuality", Int("1-10")),
        ("persuasiveness", Int("1-10")),
        ("correctnessPercent", Int("0-100 share of factual claims that were true")),
        ("claims", Arr(Obj(("claim", Str("The claim")), ("verdict", Enum("True", "False", "Unverifiable")), ("note", Str("Why, in one sentence"))), "The most consequential factual claims the player made, at most 12", max: 12)),
        ("emotions", Obj(("calm", Int("%")), ("confident", Int("%")), ("frustrated", Int("%")), ("angry", Int("%")), ("anxious", Int("%")), ("amused", Int("%")))),
        ("emotionTimeline", Arr(Obj(("atSeconds", Int("Offset into the recording")), ("dominant", Str("Dominant emotion")), ("intensity", Int("1-10"))), "One point roughly every 15 seconds the player spoke, at most 24", max: 24)),
        ("peakMomentQuote", Str("Quote from the most emotionally charged moment")),
        ("toneDescriptors", Arr(Str("An adjective"), "3-5 adjectives describing the tone", min: 3, max: 5)),
        ("paceImpression", Str("Impression of speaking pace")),
        ("energyImpression", Str("Impression of vocal energy")),
        ("pitchVariationImpression", Str("Monotone vs expressive")),
        ("confidence", Int("1-10")),
        ("politeness", Int("1-10")),
        ("aggression", Int("1-10")),
        ("listening", Int("1-10 how well they engaged with the opponent's points")),
        ("bestMomentQuote", Str("Their strongest line")),
        ("worstMomentQuote", Str("Their weakest line")),
        ("coachingTips", Arr(Str("Actionable tip"), "Exactly three tips", min: 3, max: 3)));

    private static JsonObject Obj(params (string Name, JsonObject Schema)[] props)
    {
        var properties = new JsonObject();
        foreach (var (name, schema) in props)
        {
            properties[name] = schema;
        }

        return new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = properties,
            ["required"] = new JsonArray([.. props.Select(p => (JsonNode)p.Name)]),
        };
    }

    private static JsonObject Str(string description) => new() { ["type"] = "STRING", ["description"] = description };

    private static JsonObject Int(string description) => new() { ["type"] = "INTEGER", ["description"] = description };

    private static JsonObject Enum(params string[] values) => new() { ["type"] = "STRING", ["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]) };

    private static JsonObject Arr(JsonObject items, string description, int? min = null, int? max = null)
    {
        var array = new JsonObject { ["type"] = "ARRAY", ["items"] = items, ["description"] = description };
        if (min is not null)
        {
            array["minItems"] = min;
        }

        if (max is not null)
        {
            array["maxItems"] = max;
        }

        return array;
    }
}
