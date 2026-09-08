using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fighters;

/// <summary>
/// How somebody argues, gathered across their debates. It is careful about how much it claims: one debate is an
/// anecdote, and a phrase said once is a phrase rather than a habit.
/// </summary>
public class StyleProfileBuilderTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private static FighterResultDto With(int day, StyleSnapshot style, int score = 50) =>
        new("AL", "u", MatchId.New(), MatchMode.Fight, Start.AddDays(day), "the thermostat", "SM", true, false, score, style);

    private static StyleSnapshot Snapshot(
        string tone = "clipped, dry",
        string[]? phrases = null,
        string[]? fallacies = null,
        string opener = "Look, the thing is",
        string cefr = "B2",
        string[]? emotions = null,
        string quote = "you never listen",
        string[]? tips = null) =>
        new(tone, phrases ?? [], fallacies ?? [], opener, cefr, emotions ?? ["frustrated"], quote, tips ?? ["one", "two", "three"]);

    [Fact]
    public void Somebody_who_has_never_argued_has_no_style_and_the_host_is_told_so()
    {
        var profile = StyleProfileBuilder.Build("AL", []);

        profile.Debates.Should().Be(0);
        profile.Digest.Should().Be("First fight.");
        profile.Tone.Should().BeEmpty();
    }

    [Fact]
    public void A_watch_that_left_nothing_behind_does_not_count_as_a_reading()
    {
        var profile = StyleProfileBuilder.Build("AL", [With(1, StyleSnapshot.Empty)]);

        profile.Debates.Should().Be(0, "an empty snapshot means nobody read that one");
        profile.Digest.Should().Be("First fight.");
    }

    [Fact]
    public void One_debate_is_an_early_read_and_says_so()
    {
        var profile = StyleProfileBuilder.Build("AL", [With(1, Snapshot())]);

        profile.Debates.Should().Be(1);
        profile.IsEarly.Should().BeTrue();
        profile.Digest.Should().Contain("1 fight").And.Contain("early read");
    }

    [Fact]
    public void A_phrase_has_to_turn_up_in_more_than_one_debate_before_it_is_a_habit()
    {
        var profile = StyleProfileBuilder.Build("AL",
        [
            With(1, Snapshot(phrases: ["that is not (×3)", "once only (×2)"])),
            With(2, Snapshot(phrases: ["that is not (×2)"])),
        ]);

        profile.Phrases.Should().Contain("that is not");
        profile.Phrases.Should().NotContain("once only", "it was said in one argument, which is a sentence rather than a habit");
    }

    [Fact]
    public void How_often_they_open_the_same_way_is_counted_and_named_in_the_digest()
    {
        var profile = StyleProfileBuilder.Build("AL",
        [
            With(1, Snapshot(opener: "Look, the thing is")),
            With(2, Snapshot(opener: "Look, the thing is")),
            With(3, Snapshot(opener: "Honestly")),
        ]);

        profile.Opener.Should().Be("Look, the thing is");
        profile.OpenerRepeats.Should().Be(2);
        profile.Digest.Should().Contain("Opens with");
    }

    [Fact]
    public void An_opener_used_once_is_not_offered_to_the_host_as_a_habit()
    {
        var profile = StyleProfileBuilder.Build("AL",
        [
            With(1, Snapshot(opener: "Look, the thing is")),
            With(2, Snapshot(opener: "Honestly")),
            With(3, Snapshot(opener: "Right, so")),
        ]);

        profile.Digest.Should().NotContain("Opens with");
    }

    [Fact]
    public void The_level_is_the_most_recent_judgement_not_an_average_of_old_ones()
    {
        var profile = StyleProfileBuilder.Build("AL",
        [
            With(1, Snapshot(cefr: "B1")),
            With(2, Snapshot(cefr: "C1")),
        ]);

        profile.Cefr.Should().Be("C1");
    }

    [Fact]
    public void The_quote_kept_is_from_the_night_they_argued_best()
    {
        var profile = StyleProfileBuilder.Build("AL",
        [
            With(1, Snapshot(quote: "a middling line"), score: 40),
            With(2, Snapshot(quote: "the line that won it"), score: 90),
        ]);

        profile.BestQuote.Should().Be("the line that won it");
    }

    [Fact]
    public void A_habit_the_host_can_use_is_short_enough_to_sit_in_a_prompt()
    {
        var wordy = Snapshot(
            tone: string.Join(", ", Enumerable.Repeat("relentlessly circumlocutory", 5)),
            phrases: [.. Enumerable.Range(0, 5).Select(i => $"a very long repeated phrase number {i} (×2)")],
            fallacies: ["Straw man", "Ad hominem", "Slippery slope"]);

        var profile = StyleProfileBuilder.Build("AL", [With(1, wordy), With(2, wordy), With(3, wordy)]);

        profile.Digest.Length.Should().BeLessThanOrEqualTo(StyleProfileBuilder.MaxDigestLength + 1);
        profile.Digest.Should().StartWith("3 fights");
        profile.IsEarly.Should().BeFalse("three is enough to stop hedging");
    }

    [Fact]
    public void What_they_keep_doing_wrong_is_named_for_the_host()
    {
        var profile = StyleProfileBuilder.Build("AL",
        [
            With(1, Snapshot(fallacies: ["Straw man"])),
            With(2, Snapshot(fallacies: ["Straw man", "Ad hominem"])),
        ]);

        profile.Fallacies.Should().StartWith("Straw man");
        profile.Digest.Should().Contain("straw man");
    }
}
