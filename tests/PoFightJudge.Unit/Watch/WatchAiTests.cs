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
    public void Fences_and_chatter_around_the_object_are_stripped()
    {
        var fenced = string.Join('\n', "```json", """{"line":"Fenced.","mood":"angry"}""", "```");
        var chatty = """Sure! Here you go: {"line":"Chatty.","mood":"angry"} Hope that helps.""";

        WatchAi.ParseArgument(fenced).Text.Should().Be("Fenced.");
        WatchAi.ParseArgument(chatty).Text.Should().Be("Chatty.");
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
    public void An_off_vocabulary_mood_is_inferred_from_the_line_instead()
    {
        var result = WatchAi.ParseArgument("""{"line":"Fine. Whatever you say. (coldly)","mood":"exasperated"}""");

        WatchRules.Moods.Should().Contain(result.Mood);
        WatchAi.ParseArgument("""{"line":"I am sorry, truly. (quietly)","mood":""}""").Mood.Should().Be("apologetic");
    }

    [Fact]
    public void The_streaming_prefix_shows_only_the_decoded_line_and_holds_back_a_half_written_escape()
    {
        const string quote = "\"";
        const string backslash = "\\";
        var opening = "{" + quote + "line" + quote + ":" + quote;

        WatchAi.ExtractLinePrefix("""{"line":"You left the""").Should().Be("You left the");
        WatchAi.ExtractLinePrefix(opening + "Broken " + backslash + quote).Should().Be("Broken " + quote, "a complete escape is decoded");
        WatchAi.ExtractLinePrefix(opening + "Half " + backslash).Should().Be("Half ", "an incomplete escape at the buffer edge is held back");
        WatchAi.ExtractLinePrefix("""{"mo""").Should().BeEmpty("nothing to show before the line key arrives");
        WatchAi.ExtractLinePrefix("""{"line":"Done.","mood":"angry"}""").Should().Be("Done.");
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

    [Fact]
    public void A_broken_verdict_is_a_draw_with_the_answer_as_its_explanation()
    {
        var verdict = WatchAi.ParseVerdict("the model rambled", "MAH", "KSH");

        verdict.Winner.Should().BeEmpty();
        verdict.Verdict.Should().Be("the model rambled");
        verdict.HusbandScore.Should().Be(50);
        verdict.WifeScore.Should().Be(50);
        WatchAi.ParseVerdict("""{"winner":"MAH","verdict":"v","husbandScore":900,"wifeScore":-5}""", "MAH", "KSH").HusbandScore.Should().Be(100, "scores are clamped");
    }

    [Fact]
    public void The_word_the_judge_uses_for_a_draw_never_reaches_the_rest_of_the_app()
    {
        var drawn = WatchAi.ParseVerdict($$"""{"winner":"{{WatchRules.NoWinner}}","verdict":"Even.","husbandScore":50,"wifeScore":50}""", "MAH", "KSH");

        drawn.Winner.Should().BeEmpty("a draw is an empty winner everywhere past the parser");
        WatchAi.NormalizeWinner(WatchRules.NoWinner, "MAH", "KSH").Should().BeEmpty();
    }

    [Fact]
    public void The_schemas_constrain_the_answer_to_the_vocabulary_the_game_understands()
    {
        WatchAi.ArgumentSchema["properties"]!["mood"]!["enum"]!.AsArray().Select(m => m!.GetValue<string>()).Should().BeEquivalentTo(WatchRules.Moods);

        var judge = WatchAi.JudgeSchema("MAH", "KSH");
        var winners = judge["properties"]!["winner"]!["enum"]!.AsArray().Select(w => w!.GetValue<string>()).ToList();
        winners.Should().BeEquivalentTo(["MAH", "KSH", WatchRules.NoWinner]);
        winners.Should().NotContain(string.Empty, "Gemini rejects a schema with an empty enum value, and the whole ruling fails with it");
        judge["properties"]!["husbandScore"]!["maximum"]!.GetValue<int>().Should().Be(100);
    }
}

public class WatchAiRequestTests
{
    private static Profile Husband() => Profile.Create("MAH", ProfileRole.Husband, "guitars", "clutter", "the budget", "stay chill");

    private static Profile Wife() => Profile.Create("KSH", ProfileRole.Wife, "tv", "being told she is messy", "the mess", "make a list");

    [Fact]
    public async Task A_round_goes_to_the_round_model_with_the_argument_schema()
    {
        var gemini = Substitute.For<IGeminiText>();
        gemini.GenerateAsync(Arg.Any<GeminiTextRequest>(), Arg.Any<CancellationToken>()).Returns("""{"line":"No.","mood":"angry"}""");
        var sut = new WatchAi(gemini, GeminiModelOptions.Defaults);

        await sut.GenerateArgumentAsync(Husband(), Wife(), [], WatchRound.Husband, "angry", wasSlapped: false);

        await gemini.Received(1).GenerateAsync(
            Arg.Is<GeminiTextRequest>(r =>
                r.Model == GeminiModelOptions.Defaults.Round
                && r.Operation == WatchAi.RoundOperation
                && r.ResponseSchema != null
                && r.ThinkingLevel == "minimal"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_verdict_goes_to_the_judge_model_and_reports_the_transports_fakeness()
    {
        var gemini = Substitute.For<IGeminiText>();
        gemini.IsFake.Returns(true);
        gemini.GenerateAsync(Arg.Any<GeminiTextRequest>(), Arg.Any<CancellationToken>()).Returns("""{"winner":"MAH","verdict":"He held the point.","husbandScore":70,"wifeScore":55}""");
        var sut = new WatchAi(gemini, GeminiModelOptions.Defaults);

        var verdict = await sut.JudgeAsync(Husband(), Wife(), [new RoundContext(WatchRound.Husband, "The budget is the budget.", "calm")]);

        verdict.Winner.Should().Be("MAH");
        sut.IsFake.Should().BeTrue();
        await gemini.Received(1).GenerateAsync(
            Arg.Is<GeminiTextRequest>(r => r.Model == GeminiModelOptions.Defaults.Judge && r.Operation == WatchAi.JudgeOperation),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Streaming_yields_display_clean_deltas_then_the_parsed_line()
    {
        var gemini = Substitute.For<IGeminiText>();
        gemini.StreamAsync(Arg.Any<GeminiTextRequest>(), Arg.Any<CancellationToken>())
            .Returns(Fragments("""{"line":"You """, """left the freezer open.","mood":"angry"}"""));
        var sut = new WatchAi(gemini, GeminiModelOptions.Defaults);

        var deltas = new List<string>();
        ArgumentResult? final = null;
        await foreach (var part in sut.StreamArgumentAsync(Husband(), Wife(), [], WatchRound.Husband, "angry", wasSlapped: false))
        {
            if (part.Delta is { } delta)
            {
                deltas.Add(delta);
            }

            final ??= part.Final;
        }

        string.Concat(deltas).Should().Be("You left the freezer open.", "the viewer never sees the JSON around the line");
        deltas.Should().NotContain(d => d.Contains('{', StringComparison.Ordinal) || d.Contains('"', StringComparison.Ordinal));
        final!.Text.Should().Be("You left the freezer open.");
        final.Mood.Should().Be("angry");
    }

    private static async IAsyncEnumerable<string> Fragments(params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            await Task.Yield();
            yield return fragment;
        }
    }
}

public class FakeWatchAiTests
{
    private static Profile Husband() => Profile.Create("MAH", ProfileRole.Husband, "guitars, basketball", "clutter, mess", "the budget", "stay chill");

    private static Profile Wife() => Profile.Create("KSH", ProfileRole.Wife, "tv dramas", "being told she is messy", "the mess", "make a list");

    [Fact]
    public async Task A_line_is_built_from_the_personas_own_words_and_says_it_is_fake()
    {
        var sut = new FakeWatchAi(TimeSpan.Zero);

        var opener = await sut.GenerateArgumentAsync(Husband(), Wife(), [], WatchRound.Husband, "frustrated", wasSlapped: false, topic: "the thermostat");

        opener.Text.Should().StartWith("[fake]", "a fake match must never be mistaken for a real one");
        opener.Text.Should().Contain("the thermostat");
        opener.Mood.Should().Be("frustrated");
        sut.IsFake.Should().BeTrue();
    }

    [Fact]
    public async Task The_same_turn_of_the_same_matchup_reads_the_same_so_a_prefetched_round_stays_valid()
    {
        var sut = new FakeWatchAi(TimeSpan.Zero);
        var history = new List<RoundContext> { new(WatchRound.Husband, "You left it open.", "angry") };

        var first = await sut.GenerateArgumentAsync(Husband(), Wife(), history, WatchRound.Wife, "smug", false);
        var again = await sut.GenerateArgumentAsync(Husband(), Wife(), history, WatchRound.Wife, "smug", false);
        var otherAttitude = await sut.GenerateArgumentAsync(Husband(), Wife(), history, WatchRound.Wife, "furious", false);

        first.Should().Be(again);
        otherAttitude.Text.Should().NotBe(first.Text, "the attitude shapes the line");
    }

    [Fact]
    public async Task A_slap_is_answered_in_the_line_itself()
    {
        var sut = new FakeWatchAi(TimeSpan.Zero);

        var slapped = await sut.GenerateArgumentAsync(Husband(), Wife(), [], WatchRound.Wife, "furious", wasSlapped: true);

        slapped.Text.Should().Contain("SLAPPED");
    }

    [Fact]
    public async Task Streaming_delivers_the_same_line_word_by_word()
    {
        var sut = new FakeWatchAi(TimeSpan.Zero);

        var whole = await sut.GenerateArgumentAsync(Husband(), Wife(), [], WatchRound.Husband, "angry", false);
        var deltas = new List<string>();
        ArgumentResult? final = null;
        await foreach (var part in sut.StreamArgumentAsync(Husband(), Wife(), [], WatchRound.Husband, "angry", false))
        {
            if (part.Delta is { } delta)
            {
                deltas.Add(delta);
            }

            final ??= part.Final;
        }

        deltas.Should().HaveCountGreaterThan(3);
        string.Concat(deltas).Should().Be(whole.Text);
        final.Should().Be(whole);
    }

    [Fact]
    public async Task The_fake_judge_follows_the_argument_rather_than_inventing_a_ruling()
    {
        var sut = new FakeWatchAi(TimeSpan.Zero);
        var lopsided = new List<RoundContext>
        {
            new(WatchRound.Husband, "The budget says four hundred, the receipt says nine hundred, and the difference is a television.", "calm"),
            new(WatchRound.Wife, "No! No! No!", "furious"),
        };

        var verdict = await sut.JudgeAsync(Husband(), Wife(), lopsided);

        verdict.Winner.Should().Be("MAH", "concrete clauses beat bare exclamations");
        verdict.Verdict.Should().StartWith("[fake judge]");
        verdict.HusbandScore.Should().BeGreaterThan(verdict.WifeScore);

        var drawn = await sut.JudgeAsync(Husband(), Wife(), []);
        drawn.Winner.Should().BeEmpty();
        drawn.HusbandScore.Should().Be(0);
    }
}
