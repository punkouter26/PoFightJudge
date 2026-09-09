using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Integration.Support;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Integration;

/// <summary>
/// Everything a person has said, against the real table. The partition is the tag, so a whole speaking history is
/// one read — which is what makes writing a persona from every debate affordable.
/// </summary>
[Collection(AzuriteCollection.Name)]
public class FighterWordsRepositoryTests(AzuriteFixture azurite)
{
    private static readonly DateTimeOffset At = new(2026, 9, 9, 20, 41, 0, TimeSpan.Zero);

    private FighterWordsRepository Sut()
    {
        Skip.IfNot(azurite.IsAvailable, azurite.Unavailable);
        return new FighterWordsRepository(azurite.Tables);
    }

    private static FighterId Tag() => FighterId.From(TestTags.Next());

    [SkippableFact]
    public async Task A_persons_debates_come_back_newest_first_and_only_theirs()
    {
        var sut = Sut();
        var mine = Tag();
        var theirs = Tag();

        await sut.SaveAsync(SpokenDebate.From(mine.Value, MatchId.New(), At.AddDays(-7), MatchMode.Watch, ["the older thing I said"]));
        await sut.SaveAsync(SpokenDebate.From(mine.Value, MatchId.New(), At, MatchMode.Fight, ["the newer thing I said"]));
        await sut.SaveAsync(SpokenDebate.From(theirs.Value, MatchId.New(), At, MatchMode.Fight, ["not my words at all"]));

        var said = await sut.ListAsync(mine);

        said.Should().HaveCount(2);
        said[0].Said.Should().Be("the newer thing I said", "newest first, so a budget spends itself on how they argue now");
        said[0].Mode.Should().Be(MatchMode.Fight);
        said[1].Mode.Should().Be(MatchMode.Watch, "a debate against a persona is still theirs");
        said.Should().NotContain(d => d.Said.Contains("not my words", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Re_reading_a_debate_rewrites_what_it_said_rather_than_recording_it_twice()
    {
        var sut = Sut();
        var tag = Tag();
        var matchId = MatchId.New();

        await sut.SaveAsync(SpokenDebate.From(tag.Value, matchId, At, MatchMode.Fight, ["a first pass at the transcript"]));
        await sut.SaveAsync(SpokenDebate.From(tag.Value, matchId, At, MatchMode.Fight, ["a better pass at the transcript"]));

        var said = await sut.ListAsync(tag);

        said.Should().ContainSingle().Which.Said.Should().Be("a better pass at the transcript");
    }

    [SkippableFact]
    public async Task A_debate_nobody_spoke_in_is_not_stored_at_all()
    {
        var sut = Sut();
        var tag = Tag();

        await sut.SaveAsync(SpokenDebate.From(tag.Value, MatchId.New(), At, MatchMode.Fight, []));

        (await sut.ListAsync(tag)).Should().BeEmpty("a row of nothing is not evidence of how somebody argues");
    }

    [SkippableFact]
    public async Task Deleting_a_debate_takes_the_words_with_it_and_leaves_the_rest_alone()
    {
        var sut = Sut();
        var tag = Tag();
        var deleted = MatchId.New();

        await sut.SaveAsync(SpokenDebate.From(tag.Value, deleted, At.AddDays(-1), MatchMode.Fight, ["what I said that night"]));
        await sut.SaveAsync(SpokenDebate.From(tag.Value, MatchId.New(), At, MatchMode.Fight, ["what I said tonight"]));

        await sut.DeleteForMatchAsync(deleted, [tag.Value]);

        (await sut.ListAsync(tag)).Should().ContainSingle().Which.Said.Should().Be("what I said tonight");
    }

    [SkippableFact]
    public async Task Forgetting_a_person_forgets_everything_they_ever_said()
    {
        var sut = Sut();
        var forgotten = Tag();
        var kept = Tag();

        await sut.SaveAsync(SpokenDebate.From(forgotten.Value, MatchId.New(), At.AddDays(-1), MatchMode.Fight, ["one night"]));
        await sut.SaveAsync(SpokenDebate.From(forgotten.Value, MatchId.New(), At, MatchMode.Watch, ["another night"]));
        await sut.SaveAsync(SpokenDebate.From(kept.Value, MatchId.New(), At, MatchMode.Fight, ["somebody else entirely"]));

        await sut.DeleteForFighterAsync(forgotten);

        (await sut.ListAsync(forgotten)).Should().BeEmpty();
        (await sut.ListAsync(kept)).Should().ContainSingle("forgetting one person is not forgetting the room");
    }
}
