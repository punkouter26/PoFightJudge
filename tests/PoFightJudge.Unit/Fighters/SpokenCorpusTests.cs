using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fighters;

/// <summary>
/// Everything one person has ever said, gathered for the model that writes their persona. The point of it is that a
/// regular is written from a body of work rather than from whichever night happened to be last.
/// </summary>
public sealed class SpokenCorpusTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);

    private static SpokenDebate Debate(string said, int daysAgo, MatchMode mode = MatchMode.Fight) =>
        SpokenDebate.From("KDH", MatchId.New(), Now.AddDays(-daysAgo), mode, [said]);

    [Fact]
    public void Somebody_who_has_never_said_anything_has_nothing_to_read()
    {
        var corpus = SpokenCorpus.From([]);

        corpus.IsEmpty.Should().BeTrue();
        corpus.Debates.Should().Be(0);
    }

    [Fact]
    public void A_debate_nobody_transcribed_is_not_counted_as_one_they_spoke_in()
    {
        var corpus = SpokenCorpus.From([Debate("   ", 1), Debate("I said something", 0)]);

        corpus.Debates.Should().Be(1);
        corpus.Text.Should().Contain("I said something");
    }

    [Fact]
    public void Every_debate_they_have_spoken_in_is_there_oldest_first()
    {
        var corpus = SpokenCorpus.From([Debate("tonight", 0), Debate("last week", 7), Debate("last month", 30)]);

        corpus.Debates.Should().Be(3);
        corpus.Text.IndexOf("last month", StringComparison.Ordinal)
            .Should().BeLessThan(corpus.Text.IndexOf("last week", StringComparison.Ordinal), "a person is read in the order they changed");
        corpus.Text.IndexOf("last week", StringComparison.Ordinal)
            .Should().BeLessThan(corpus.Text.IndexOf("tonight", StringComparison.Ordinal));
    }

    [Fact]
    public void The_debate_just_judged_is_named_as_such_so_the_model_knows_which_night_it_is_reading()
    {
        var corpus = SpokenCorpus.From([Debate("tonight", 0), Debate("last week", 7)]);

        corpus.Text.Should().Contain("the one just judged");
        corpus.Text.IndexOf("the one just judged", StringComparison.Ordinal)
            .Should().BeGreaterThan(corpus.Text.IndexOf("last week", StringComparison.Ordinal), "it is the last one in the list");
    }

    [Fact]
    public void Who_they_were_arguing_is_said_because_a_persona_is_not_a_person()
    {
        var corpus = SpokenCorpus.From([Debate("at a persona", 1, MatchMode.Watch), Debate("at a person", 0)]);

        corpus.Text.Should().Contain("against a persona");
        corpus.Text.Should().Contain("against another person");
    }

    [Fact]
    public void A_regular_costs_no_more_to_read_than_a_newcomer()
    {
        var many = Enumerable.Range(0, 40).Select(i => Debate(new string('x', 900), i)).ToList();

        var corpus = SpokenCorpus.From(many);

        corpus.Text.Length.Should().BeLessThanOrEqualTo(SpokenCorpus.MaxChars + 900, "the budget holds, give or take the last debate let in whole");
        corpus.Debates.Should().BeLessThanOrEqualTo(SpokenCorpus.MaxDebates);
        corpus.Debates.Should().BeGreaterThan(0);
    }

    [Fact]
    public void When_the_budget_runs_out_it_is_the_oldest_nights_that_go()
    {
        var debates = new List<SpokenDebate>
        {
            Debate(new string('n', 5_000), 0),
            Debate(new string('o', 5_000), 90),
        };

        var corpus = SpokenCorpus.From(debates);

        corpus.Text.Should().Contain("nnn", "the most recent one is always read");
        corpus.Text.Should().NotContain("ooo", "how somebody argued three months ago loses to how they argue now");
    }

    [Fact]
    public void One_enormous_night_is_still_read_rather_than_dropped_for_being_too_long()
    {
        var corpus = SpokenCorpus.From([Debate(new string('x', SpokenDebate.MaxChars * 2), 0)]);

        corpus.IsEmpty.Should().BeFalse();
        corpus.Debates.Should().Be(1);
    }

    [Fact]
    public void A_single_debate_is_capped_where_it_is_stored_so_a_row_can_never_grow_without_end()
    {
        var debate = SpokenDebate.From("KDH", MatchId.New(), Now, MatchMode.Fight, [new string('x', 50_000)]);

        debate.Said.Length.Should().BeLessThanOrEqualTo(SpokenDebate.MaxChars + 1, "cut at the tail, plus the ellipsis that says so");
        debate.Said.Should().EndWith("…");
    }

    [Fact]
    public void The_words_are_kept_in_the_order_they_were_said_and_the_silence_between_them_is_not()
    {
        var debate = SpokenDebate.From("KDH", MatchId.New(), Now, MatchMode.Fight, ["Look,", "   ", "the thing is", string.Empty, "you never listen"]);

        debate.Said.Should().Be("Look, the thing is you never listen");
    }
}
