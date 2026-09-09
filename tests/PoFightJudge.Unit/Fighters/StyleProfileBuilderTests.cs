using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fighters;

/// <summary>
/// How somebody argues, gathered across their debates. It is careful about how much it claims: one debate is an
/// anecdote, and a phrase said once is a phrase rather than a habit.
/// </summary>
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
}
