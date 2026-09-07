using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Records;

public class StorageEntityTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 6, 20, 41, 0, TimeSpan.Zero);

    private static MatchDto Match(MatchMode mode = MatchMode.Watch) => new(
        MatchId.New(),
        "user-1",
        mode,
        At,
        At.AddMinutes(4),
        "the thermostat",
        mode == MatchMode.Watch ? MatchSide.Persona("MAH", "Matthew") : MatchSide.Human("AB", "Alex"),
        mode == MatchMode.Watch ? MatchSide.Persona("KSH", "Kimberly") : MatchSide.Human("CD", "Casey"),
        SessionPhase.Done,
        SessionStatus.Ready,
        "MAH",
        "He held the point.",
        IsFake: true)
    {
        Persona = "Referee",
        AudioBlobName = "audio/x.wav",
    };

    [Fact]
    public void A_match_round_trips_with_its_keys_derived_from_the_owner_and_the_id()
    {
        var match = Match();

        var entity = MatchEntity.From(match);
        var back = entity.ToDto();

        entity.PartitionKey.Should().Be("user-1", "one user's history is a single partition");
        entity.RowKey.Should().Be(match.Id.Value);
        back.Should().Be(match);
    }

    [Fact]
    public void Both_modes_use_the_same_row_and_an_unknown_enum_reads_back_as_a_safe_default()
    {
        MatchEntity.From(Match(MatchMode.Fight)).ToDto().Mode.Should().Be(MatchMode.Fight);

        var legacy = new MatchEntity { PartitionKey = "u", RowKey = MatchId.New().Value, Mode = "Sideways", Phase = "Nope", Status = "Unknown", Side1Kind = "Nonsense" };

        var dto = legacy.ToDto();
        dto.Side1.Kind.Should().Be(SideKind.Persona, "an unreadable side kind reads as a persona, which is the older shape");
        dto.Mode.Should().Be(MatchMode.Watch);
        dto.Phase.Should().Be(SessionPhase.Done);
        dto.Status.Should().Be(SessionStatus.Ready, "a row written by older code still has to render in history");
        dto.IsDraw.Should().BeTrue("no winner recorded is a draw");
    }

    [Fact]
    public void A_turn_round_trips_and_its_row_key_sorts_in_play_order()
    {
        var id = MatchId.New();
        var turn = new TurnDto(id, 7, Speaker.Player2, TurnKind.Round, "You left the freezer open.")
        {
            StartSeconds = 12.5,
            EndSeconds = 18.25,
            Mood = "angry",
            AudioBlobName = $"{id.Value}/7.mp3",
            AudioFormat = "mp3",
        };

        var back = TurnEntity.From(turn).ToDto();

        back.Should().Be(turn);
        TurnEntity.RowKeyFor(7).Should().Be("000007");
        var keys = new[] { 2, 10, 1 }.Select(TurnEntity.RowKeyFor).ToList();
        // Zero padding is what keeps lexical order the same as play order.
        keys.Order(StringComparer.Ordinal).Should().Equal("000001", "000002", "000010");
    }

    [Fact]
    public void An_analysis_report_is_chunked_across_properties_and_rejoins_intact()
    {
        var id = MatchId.New();
        var report = string.Concat(Enumerable.Range(0, 90_000).Select(i => (char)('a' + (i % 26))));
        var dto = new AnalysisRecordDto(id, AnalysisStatus.Ready, report, null, At);

        var entity = AnalysisEntity.From(dto);

        entity.RowKey.Should().Be(AnalysisEntity.FixedRowKey);
        entity.Json0!.Length.Should().Be(AnalysisEntity.ChunkSize, "a Table property caps at 64 KB, so the report is split");
        entity.Json2.Should().NotBeNullOrEmpty();
        entity.Json3.Should().BeNull("only as many chunks as the report needs are written");
        entity.ToDto().Should().Be(dto);
    }

    [Fact]
    public void A_report_beyond_the_last_chunk_is_refused_rather_than_silently_truncated()
    {
        var tooBig = new string('x', (AnalysisEntity.ChunkSize * AnalysisEntity.ChunkCount) + 1);

        var act = () => AnalysisEntity.From(new AnalysisRecordDto(MatchId.New(), AnalysisStatus.Ready, tooBig, null, At));

        act.Should().Throw<InvalidOperationException>().WithMessage("*limited to*");
        AnalysisEntity.From(new AnalysisRecordDto(MatchId.New(), AnalysisStatus.Queued, null, null, At)).ToDto().ReportJson.Should().BeNull("nothing analysed yet is null, not an empty string");
    }

    [Fact]
    public void A_result_row_is_keyed_by_side_and_match_so_re_judging_overwrites()
    {
        var id = MatchId.New();
        var stats = new AdvancedStatsDto(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var watch = new WatchResultDto("MAH", id, At, "the thermostat", "KSH", Won: true, Draw: false, Score: 71, stats);

        var entity = WatchResultEntity.From(watch);

        entity.PartitionKey.Should().Be("MAH", "a persona's whole record is one partition read");
        entity.RowKey.Should().Be(id.Value, "the second judgement of a match replaces the first");
        entity.ToDto().Should().Be(watch);
    }

    [Fact]
    public void A_fighter_result_carries_the_style_snapshot_their_profile_is_built_from()
    {
        var id = MatchId.New();
        var style = new StyleSnapshot("clipped", ["you always"], ["strawman"], "Look, the thing is", "B2", ["angry"], "That is not what I said.", ["let them finish"]);
        var result = new FighterResultDto("AB", "u", id, MatchMode.Fight, At, "the bins", "CD", Won: false, Draw: true, Score: 55, style);

        var back = FighterResultEntity.From(result).ToDto();

        // BeEquivalentTo, not Be: the snapshot holds lists, which records compare by reference.
        back.Should().BeEquivalentTo(result);
        back.Style.Phrases.Should().Equal("you always");
        FighterResultEntity.From(result with { Style = StyleSnapshot.Empty }).ToDto().Style.Should().BeEquivalentTo(StyleSnapshot.Empty);
    }

    [Fact]
    public void A_fighter_lives_in_one_partition_because_the_roster_is_read_whole()
    {
        var fighter = new FighterDto("AB", "Alex", At, At.AddDays(2));

        var entity = FighterEntity.From(fighter);

        entity.PartitionKey.Should().Be(FighterEntity.Partition);
        entity.RowKey.Should().Be("AB");
        entity.ToDto().Should().Be(fighter);
    }
}
