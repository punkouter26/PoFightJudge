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
    public void A_schema_answer_becomes_a_line_and_a_mood()
    {
        var result = WatchAi.ParseArgument("""{"line":"You left the freezer open. (snaps)","mood":"furious"}""");

        result.Text.Should().Be("You left the freezer open. (snaps)");
        result.Mood.Should().Be("furious");
    }

    [Fact]
    public void An_unparseable_answer_still_becomes_a_line_because_a_match_must_not_stop()
    {
        var refusal = WatchAi.ParseArgument("I cannot help with that.");

        refusal.Text.Should().Be("I cannot help with that.");
        WatchRules.Moods.Should().Contain(refusal.Mood);
        WatchAi.ParseArgument("   ").Text.Should().Be("…");
    }

    [Fact]
    public void A_verdict_parses_scores_and_only_ever_names_one_of_the_two_sides()
    {
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
