using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Records;

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
}
