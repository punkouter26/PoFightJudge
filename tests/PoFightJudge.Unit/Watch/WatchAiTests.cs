using System.Text.Json.Nodes;
using NSubstitute;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Watch;

public class WatchAiParsingTests
{
    [Fact]
    public void A_schema_answer_becomes_a_line_and_a_mood_a_broken_one_still_becomes_a_line_and_a_verdict_names_one_of_the_two_sides()
    {
        // A schema that requires a property it does not define is invalid, and the fake, which answers from the
        // properties, never notices.
        foreach (var schema in new[] { WatchAi.ArgumentSchema, WatchAi.JudgeSchema("MAH", "KSH"), PoFightJudge.Api.Features.Profiles.ProfileGenerator.Schema })
        {
            var defined = schema["properties"]!.AsObject().Select(p => p.Key);
            schema["required"]!.AsArray().Select(r => r!.GetValue<string>()).Should().BeSubsetOf(defined);
        }

        var result = WatchAi.ParseArgument("""{"line":"You left the freezer open. (snaps)","mood":"furious"}""");

        result.Text.Should().Be("You left the freezer open. (snaps)");
        result.Mood.Should().Be("furious");

        // An unparseable answer still becomes a line, because a match must not stop.
        var refusal = WatchAi.ParseArgument("I cannot help with that.");

        refusal.Text.Should().Be("I cannot help with that.");
        WatchRules.Moods.Should().Contain(refusal.Mood);
        WatchAi.ParseArgument("   ").Text.Should().Be("…");

        // Cut off mid-object: what can be read of the line is the line. The braces are never shown or spoken.
        WatchAi.ParseArgument("""{"line":"You left the freezer open and""").Text.Should().Be("You left the freezer open and");
        WatchAi.ParseArgument("""{"mood":"ang""").Text.Should().Be("…");
        WatchAi.ParseVerdict("""{"winner":"KSH","verdict":"She answ""", "MAH", "KSH").Verdict.Should().Be("The judge could not reach a ruling.");

        // A verdict parses its scores and only ever names one of the two sides.
        var verdict = WatchAi.ParseVerdict("""{"winner":"KSH","verdict":"She answered the point.\nHe changed the subject.","husbandScore":41,"wifeScore":78}""", "MAH", "KSH");

        verdict.Winner.Should().Be("KSH");
        verdict.Verdict.Should().Contain("She answered the point.");
        verdict.HusbandScore.Should().Be(41);
        verdict.WifeScore.Should().Be(78);

        WatchAi.ParseVerdict("""{"winner":"the wife","verdict":"v","husbandScore":10,"wifeScore":90}""", "MAH", "KSH").Winner.Should().BeEmpty("a role word is not a side");
        WatchAi.ParseVerdict("""{"winner":"","verdict":"Too close to call.","husbandScore":50,"wifeScore":50}""", "MAH", "KSH").Winner.Should().BeEmpty();
        WatchAi.ParseVerdict("""{"winner":"ksh","verdict":"v","husbandScore":1,"wifeScore":2}""", "MAH", "KSH").Winner.Should().Be("KSH", "the judge's casing is not the record's problem");
    }
}
