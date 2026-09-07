using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Integration.Support;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;
using PoMarriedFight.TestSupport;

namespace PoMarriedFight.Integration;

[Collection(AzuriteCollection.Name)]
public class FighterRepositoryTests(AzuriteFixture azurite)
{
    private static readonly DateTimeOffset At = new(2026, 9, 6, 20, 41, 0, TimeSpan.Zero);

    private FighterRepository Sut()
    {
        Skip.IfNot(azurite.IsAvailable, azurite.Unavailable);
        return new FighterRepository(azurite.Tables);
    }

    /// <summary>
    /// A tag no earlier test and no earlier run has used. The table outlives the run, and these tests assert
    /// things like "a tag that never argued cannot be renamed" — with eighty-nine possible tags that was true
    /// most of the time, which is the worst kind of true.
    /// </summary>
    private static FighterId Tag() => FighterId.From(TestTags.Next());

    [SkippableFact]
    public async Task Ensure_creates_a_fighter_once_and_then_only_moves_the_last_seen_time()
    {
        var sut = Sut();
        var tag = Tag();

        (await sut.GetAsync(tag)).Should().BeNull("a tag that has never argued is not a fighter yet");

        var first = await sut.EnsureAsync(tag, At);
        var second = await sut.EnsureAsync(tag, At.AddHours(3));

        first.CreatedAt.Should().Be(At);
        second.CreatedAt.Should().Be(At, "the second debate does not re-create the person");
        second.LastSeenAt.Should().Be(At.AddHours(3));

        var stored = await sut.GetAsync(tag);
        stored!.CreatedAt.Should().Be(At);
        stored.LastSeenAt.Should().Be(At.AddHours(3));
        stored.DisplayName.Should().Be(tag.Value);
    }

    [SkippableFact]
    public async Task Renaming_changes_the_name_only_and_an_unknown_tag_renames_to_nothing()
    {
        var sut = Sut();
        var tag = Tag();
        await sut.EnsureAsync(tag, At);

        var renamed = await sut.RenameAsync(tag, "Alex");

        renamed!.DisplayName.Should().Be("Alex");
        renamed.Tag.Should().Be(tag.Value);
        (await sut.GetAsync(tag))!.DisplayName.Should().Be("Alex");
        (await sut.RenameAsync(Tag(), "Nobody")).Should().BeNull("a tag that never argued cannot be renamed");
    }

    [SkippableFact]
    public async Task The_roster_comes_back_most_recently_seen_first_and_a_fighter_can_be_removed()
    {
        var sut = Sut();
        var older = Tag();
        var newer = Tag();
        await sut.EnsureAsync(older, At.AddDays(-2));
        await sut.EnsureAsync(newer, At);

        var roster = await sut.ListAsync();
        roster.Select(f => f.Tag).Should().ContainInOrder(newer.Value, older.Value);

        await sut.DeleteAsync(newer);

        (await sut.GetAsync(newer)).Should().BeNull();
        (await sut.ListAsync()).Select(f => f.Tag).Should().NotContain(newer.Value).And.Contain(older.Value);
    }
}

[Collection(AzuriteCollection.Name)]
public class FighterResultRepositoryTests(AzuriteFixture azurite)
{
    private static readonly DateTimeOffset At = new(2026, 9, 6, 20, 41, 0, TimeSpan.Zero);

    private FighterResultRepository Sut()
    {
        Skip.IfNot(azurite.IsAvailable, azurite.Unavailable);
        return new FighterResultRepository(azurite.Tables);
    }

    private static FighterId Tag() => FighterId.From(TestTags.Next());

    /// <summary>The account these rows belong to. Results are read back per account, so every read here names it.</summary>
    private const string User = "repo-user";

    private static FighterResultDto Result(FighterId tag, MatchId id, MatchMode mode = MatchMode.Fight, bool won = true, StyleSnapshot? style = null, string userId = User) =>
        new(tag.Value, userId, id, mode, At, "the bins", "OPP", won, Draw: false, Score: won ? 80 : 40, style ?? StyleSnapshot.Empty);

    [SkippableFact]
    public async Task Re_analysing_a_debate_replaces_its_row_rather_than_counting_twice()
    {
        var sut = Sut();
        var tag = Tag();
        var id = MatchId.New();
        var style = new StyleSnapshot("clipped", ["you always"], ["strawman"], "Look, the thing is", "B2", ["angry"], "That is not what I said.", ["let them finish"]);

        await sut.SaveAsync([Result(tag, id, won: false)]);
        await sut.SaveAsync([Result(tag, id, won: true, style: style)]);

        var rows = await sut.ListForAsync(tag, User);
        rows.Should().ContainSingle();
        rows[0].Won.Should().BeTrue();
        rows[0].Style.Should().BeEquivalentTo(style, "the style snapshot is what the profile is built from");
    }

    [SkippableFact]
    public async Task Results_from_both_modes_land_in_one_persons_history_newest_first()
    {
        var sut = Sut();
        var tag = Tag();
        var watch = MatchId.New();
        var fight = MatchId.New();

        await sut.SaveAsync(
        [
            Result(tag, watch, MatchMode.Watch) with { At = At.AddDays(-1) },
            Result(tag, fight, MatchMode.Fight),
        ]);

        var rows = await sut.ListForAsync(tag, User);
        rows.Select(r => r.MatchId).Should().Equal(fight, watch);
        // Arguing a persona counts towards the same profile as arguing a person.
        rows.Select(r => r.Mode).Should().Equal(MatchMode.Fight, MatchMode.Watch);
        (await sut.ListAllAsync(User)).Should().Contain(r => r.MatchId == fight);
    }

    [SkippableFact]
    public async Task Deleting_a_debate_or_a_whole_fighter_takes_the_contributions_back()
    {
        var sut = Sut();
        var tag = Tag();
        var first = MatchId.New();
        var second = MatchId.New();
        await sut.SaveAsync([Result(tag, first), Result(tag, second)]);

        await sut.DeleteForMatchAsync(first, [tag.Value]);
        (await sut.ListForAsync(tag, User)).Select(r => r.MatchId).Should().Equal(second);

        await sut.DeleteForFighterAsync(tag);
        (await sut.ListForAsync(tag, User)).Should().BeEmpty("deleting the person removes everything their profile was built from");
    }
}
