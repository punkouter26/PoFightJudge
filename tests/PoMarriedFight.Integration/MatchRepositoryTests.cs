using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Integration.Support;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Integration;

[Collection(AzuriteCollection.Name)]
public class MatchRepositoryTests(AzuriteFixture azurite)
{
    private static readonly DateTimeOffset At = new(2026, 9, 6, 20, 41, 0, TimeSpan.Zero);

    private (MatchRepository Matches, WatchResultRepository Results) Sut()
    {
        Skip.IfNot(azurite.IsAvailable, azurite.Unavailable);
        return (new MatchRepository(azurite.Tables), new WatchResultRepository(azurite.Tables));
    }

    private static MatchDto Match(string userId, MatchMode mode = MatchMode.Watch, DateTimeOffset? startedAt = null, string side1 = "MAH", string side2 = "KSH") => new(
        MatchId.New(),
        userId,
        mode,
        startedAt ?? At,
        (startedAt ?? At).AddMinutes(4),
        "the thermostat",
        mode == MatchMode.Watch ? MatchSide.Persona(side1, "Matthew") : MatchSide.Human(side1, "Alex"),
        mode == MatchMode.Watch ? MatchSide.Persona(side2, "Kimberly") : MatchSide.Human(side2, "Casey"),
        SessionPhase.Done,
        SessionStatus.Ready,
        side1,
        "He held the point.",
        IsFake: true);

    [SkippableFact]
    public async Task A_match_round_trips_and_history_comes_back_newest_first_per_user()
    {
        var (matches, _) = Sut();
        var user = $"user-{Guid.NewGuid():N}";
        var older = Match(user, startedAt: At.AddHours(-2));
        var newer = Match(user, MatchMode.Fight, At, side1: "AB", side2: "CD");

        await matches.UpsertAsync(older);
        await matches.UpsertAsync(newer);
        await matches.UpsertAsync(Match($"other-{Guid.NewGuid():N}"));

        (await matches.GetAsync(user, older.Id)).Should().Be(older);
        (await matches.GetAsync(user, MatchId.New())).Should().BeNull("a missing match is null, not an exception");
        (await matches.GetAsync("someone-else", older.Id)).Should().BeNull("a match belongs to its owner's partition");

        var history = await matches.ListAsync(user);
        history.Select(m => m.Id).Should().Equal(newer.Id, older.Id);
        (await matches.ListAsync(user, MatchMode.Fight)).Should().ContainSingle().Which.Id.Should().Be(newer.Id);
        (await matches.ListAsync(user, MatchMode.Watch)).Should().ContainSingle().Which.Id.Should().Be(older.Id);
    }

    [SkippableFact]
    public async Task Turns_save_as_a_batch_and_read_back_in_play_order()
    {
        var (matches, _) = Sut();
        var match = Match($"user-{Guid.NewGuid():N}");
        var turns = new List<TurnDto>
        {
            new(match.Id, 2, Speaker.Player1, TurnKind.Round, "Third.") { Mood = "furious" },
            new(match.Id, 0, Speaker.Player1, TurnKind.Round, "First.") { Mood = "angry", AudioBlobName = $"{match.Id.Value}/0.mp3", AudioFormat = "mp3" },
            new(match.Id, 1, Speaker.Player2, TurnKind.Round, "Second.") { Mood = "smug" },
        };

        await matches.UpsertAsync(match);
        await matches.SaveTurnsAsync(match.Id, turns);

        var stored = await matches.GetTurnsAsync(match.Id);
        stored.Select(t => t.Index).Should().Equal(0, 1, 2);
        stored.Select(t => t.Text).Should().Equal("First.", "Second.", "Third.");
        stored[0].AudioFormat.Should().Be("mp3");
        stored[0].AudioBlobName.Should().Be($"{match.Id.Value}/0.mp3");

        // Re-saving the same indices replaces rather than duplicating, which is what a retried verdict does.
        await matches.SaveTurnsAsync(match.Id, [turns[1] with { Text = "First, again." }]);
        (await matches.GetTurnsAsync(match.Id)).Should().HaveCount(3);
        (await matches.GetTurnsAsync(match.Id))[0].Text.Should().Be("First, again.");
    }

    [SkippableFact]
    public async Task An_analysis_round_trips_through_the_chunked_row()
    {
        var (matches, _) = Sut();
        var match = Match($"user-{Guid.NewGuid():N}", MatchMode.Fight);
        var report = string.Concat(Enumerable.Range(0, 70_000).Select(i => (char)('a' + (i % 26))));

        await matches.UpsertAsync(match);
        (await matches.GetAnalysisAsync(match.Id)).Should().BeNull();

        await matches.SaveAnalysisAsync(new AnalysisRecordDto(match.Id, AnalysisStatus.Judging, null, null, At));
        await matches.SaveAnalysisAsync(new AnalysisRecordDto(match.Id, AnalysisStatus.Ready, report, null, At.AddMinutes(1)));

        var stored = await matches.GetAnalysisAsync(match.Id);
        stored!.Status.Should().Be(AnalysisStatus.Ready, "the second write replaces the progress row");
        stored.ReportJson.Should().Be(report);
        stored.Error.Should().BeNull();
    }

    [SkippableFact]
    public async Task Deleting_a_match_takes_its_turns_analysis_and_both_sides_results_with_it()
    {
        var (matches, results) = Sut();
        var user = $"user-{Guid.NewGuid():N}";
        var match = Match(user, side1: $"H{Random.Shared.Next(10, 99)}", side2: $"W{Random.Shared.Next(10, 99)}");

        await matches.UpsertAsync(match);
        await matches.SaveTurnsAsync(match.Id, [new TurnDto(match.Id, 0, Speaker.Player1, TurnKind.Round, "Only line.")]);
        await matches.SaveAnalysisAsync(new AnalysisRecordDto(match.Id, AnalysisStatus.Ready, "{}", null, At));
        await results.SaveAsync(
        [
            new WatchResultDto(match.Side1.Id, match.Id, At, match.Topic, match.Side2.Id, Won: true, Draw: false, Score: 70, AdvancedStatsDto.Empty),
            new WatchResultDto(match.Side2.Id, match.Id, At, match.Topic, match.Side1.Id, Won: false, Draw: false, Score: 40, AdvancedStatsDto.Empty),
        ]);

        (await results.ListForAsync(match.Side1.Id)).Should().ContainSingle();

        var deleted = await matches.DeleteAsync(user, match.Id);

        deleted.Should().BeTrue();
        (await matches.GetAsync(user, match.Id)).Should().BeNull();
        (await matches.GetTurnsAsync(match.Id)).Should().BeEmpty();
        (await matches.GetAnalysisAsync(match.Id)).Should().BeNull();
        (await results.ListForAsync(match.Side1.Id)).Should().BeEmpty("a half-deleted match would still show on a leaderboard");
        (await results.ListForAsync(match.Side2.Id)).Should().BeEmpty();
        (await matches.DeleteAsync(user, match.Id)).Should().BeFalse("there is nothing left to delete");
    }
}

[Collection(AzuriteCollection.Name)]
public class WatchResultRepositoryTests(AzuriteFixture azurite)
{
    private static readonly DateTimeOffset At = new(2026, 9, 6, 20, 41, 0, TimeSpan.Zero);

    private WatchResultRepository Sut()
    {
        Skip.IfNot(azurite.IsAvailable, azurite.Unavailable);
        return new WatchResultRepository(azurite.Tables);
    }

    [SkippableFact]
    public async Task Re_judging_a_match_replaces_its_row_rather_than_counting_twice()
    {
        var sut = Sut();
        var side = $"R{Random.Shared.Next(10, 99)}";
        var id = MatchId.New();
        var stats = new AdvancedStatsDto(10, 20, 30, 4, 50, 60, 70, 80, 90, 100);

        await sut.SaveAsync([new WatchResultDto(side, id, At, "the bins", "OPP", Won: false, Draw: false, Score: 40, stats)]);
        await sut.SaveAsync([new WatchResultDto(side, id, At, "the bins", "OPP", Won: true, Draw: false, Score: 80, stats)]);

        var rows = await sut.ListForAsync(side);
        rows.Should().ContainSingle();
        rows[0].Won.Should().BeTrue();
        rows[0].Score.Should().Be(80);
        rows[0].Stats.Should().Be(stats, "the analytics survive the round trip");
    }

    [SkippableFact]
    public async Task A_personas_results_come_back_newest_first_and_deletion_is_per_match()
    {
        var sut = Sut();
        var side = $"S{Random.Shared.Next(10, 99)}";
        var older = MatchId.New();
        var newer = MatchId.New();

        await sut.SaveAsync(
        [
            new WatchResultDto(side, older, At.AddDays(-1), "old", "OPP", true, false, 60, AdvancedStatsDto.Empty),
            new WatchResultDto(side, newer, At, "new", "OPP", false, true, 50, AdvancedStatsDto.Empty),
        ]);

        (await sut.ListForAsync(side)).Select(r => r.MatchId).Should().Equal(newer, older);
        (await sut.ListAllAsync()).Should().Contain(r => r.MatchId == newer);

        await sut.DeleteForMatchAsync(newer, [side]);

        (await sut.ListForAsync(side)).Select(r => r.MatchId).Should().Equal(older);
    }
}
