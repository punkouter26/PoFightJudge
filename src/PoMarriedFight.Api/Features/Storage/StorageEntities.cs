using System.Globalization;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Storage;

/// <summary>Every table this solution writes. One place, so a name is never spelled twice.</summary>
public static class TableNames
{
    public const string Profiles = "pomarriedfightprofiles";
    public const string Matches = "pomarriedfightmatches";
    public const string Turns = "pomarriedfightturns";
    public const string Analyses = "pomarriedfightanalyses";
    public const string WatchResults = "pomarriedfightwatchresults";
    public const string FighterResults = "pomarriedfightfighterresults";
    public const string Fighters = "pomarriedfightfighters";
}

/// <summary>
/// PartitionKey = user id, RowKey = match id, so every lookup is a point read and one user's history is a single
/// partition scan. Both engines write this row; <see cref="Mode"/> says which one, and the sides are denormalised
/// (initials or tags, plus display names) so a history list never has to join.
/// </summary>
public sealed class MatchEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Mode { get; set; } = nameof(MatchMode.Watch);

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string Side1 { get; set; } = string.Empty;

    public string Side2 { get; set; } = string.Empty;

    public string Side1Name { get; set; } = string.Empty;

    public string Side2Name { get; set; } = string.Empty;

    /// <summary>Whether each side was an authored persona or a real person; a human side earns a fighter result row.</summary>
    public string Side1Kind { get; set; } = nameof(SideKind.Persona);

    public string Side2Kind { get; set; } = nameof(SideKind.Persona);

    public string Phase { get; set; } = nameof(SessionPhase.Intro);

    public string Status { get; set; } = nameof(SessionStatus.Live);

    public string Winner { get; set; } = string.Empty;

    public string? Verdict { get; set; }

    public string Persona { get; set; } = string.Empty;

    public string? AudioBlobName { get; set; }

    /// <summary>True when the match was played against the fakes, so history can label it and stats can exclude it.</summary>
    public bool IsFake { get; set; }

    public static MatchEntity From(MatchDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new MatchEntity
        {
            PartitionKey = dto.UserId,
            RowKey = dto.Id.Value,
            Mode = dto.Mode.ToString(),
            StartedAt = dto.StartedAt,
            EndedAt = dto.EndedAt,
            Topic = dto.Topic,
            Side1 = dto.Side1.Id,
            Side2 = dto.Side2.Id,
            Side1Name = dto.Side1.DisplayName,
            Side2Name = dto.Side2.DisplayName,
            Side1Kind = dto.Side1.Kind.ToString(),
            Side2Kind = dto.Side2.Kind.ToString(),
            Phase = dto.Phase.ToString(),
            Status = dto.Status.ToString(),
            Winner = dto.Winner,
            Verdict = dto.Verdict,
            Persona = dto.Persona,
            AudioBlobName = dto.AudioBlobName,
            IsFake = dto.IsFake,
        };
    }

    public MatchDto ToDto() => new(
        MatchId.From(RowKey),
        PartitionKey,
        Enum.TryParse<MatchMode>(Mode, ignoreCase: true, out var mode) ? mode : MatchMode.Watch,
        StartedAt,
        EndedAt,
        Topic,
        new MatchSide(Side1, Side1Name, ParseKind(Side1Kind)),
        new MatchSide(Side2, Side2Name, ParseKind(Side2Kind)),
        Enum.TryParse<SessionPhase>(Phase, ignoreCase: true, out var phase) ? phase : SessionPhase.Done,
        Enum.TryParse<SessionStatus>(Status, ignoreCase: true, out var status) ? status : SessionStatus.Ready,
        Winner,
        Verdict,
        IsFake)
    {
        Persona = Persona,
        AudioBlobName = AudioBlobName,
    };

    private static SideKind ParseKind(string? kind) => Enum.TryParse<SideKind>(kind, ignoreCase: true, out var parsed) ? parsed : SideKind.Persona;
}

/// <summary>PartitionKey = match id, RowKey = zero-padded index, so turns read back in play order without a sort.</summary>
public sealed class TurnEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Speaker { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public double StartSeconds { get; set; }

    public double? EndSeconds { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Mood { get; set; } = string.Empty;

    public string? AudioBlobName { get; set; }

    public string AudioFormat { get; set; } = "pcm";

    public static TurnEntity From(TurnDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new TurnEntity
        {
            PartitionKey = dto.MatchId.Value,
            RowKey = RowKeyFor(dto.Index),
            Speaker = dto.Speaker.ToString(),
            Kind = dto.Kind.ToString(),
            StartSeconds = dto.StartSeconds,
            EndSeconds = dto.EndSeconds,
            Text = dto.Text,
            Mood = dto.Mood,
            AudioBlobName = dto.AudioBlobName,
            AudioFormat = dto.AudioFormat,
        };
    }

    public static string RowKeyFor(int index) => index.ToString("D6", CultureInfo.InvariantCulture);

    public TurnDto ToDto() => new(
        MatchId.From(PartitionKey),
        int.Parse(RowKey, CultureInfo.InvariantCulture),
        Enum.TryParse<Speaker>(Speaker, ignoreCase: true, out var speaker) ? speaker : Shared.Models.Speaker.Player1,
        Enum.TryParse<TurnKind>(Kind, ignoreCase: true, out var kind) ? kind : TurnKind.Talk,
        Text)
    {
        StartSeconds = StartSeconds,
        EndSeconds = EndSeconds,
        Mood = Mood,
        AudioBlobName = AudioBlobName,
        AudioFormat = AudioFormat,
    };
}

/// <summary>
/// PartitionKey = match id, RowKey = <see cref="FixedRowKey"/>. The report is split across eight properties because
/// Table Storage caps a single one at 64 KB, and a full analysis comfortably exceeds that.
/// </summary>
public sealed class AnalysisEntity : ITableEntity
{
    public const string FixedRowKey = "analysis";

    public const int ChunkSize = 32_000;

    public const int ChunkCount = 8;

    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = FixedRowKey;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Status { get; set; } = nameof(AnalysisStatus.Queued);

    public string? Error { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? Json0 { get; set; }

    public string? Json1 { get; set; }

    public string? Json2 { get; set; }

    public string? Json3 { get; set; }

    public string? Json4 { get; set; }

    public string? Json5 { get; set; }

    public string? Json6 { get; set; }

    public string? Json7 { get; set; }

    public static AnalysisEntity From(AnalysisRecordDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var entity = new AnalysisEntity
        {
            PartitionKey = dto.MatchId.Value,
            Status = dto.Status.ToString(),
            Error = dto.Error,
            UpdatedAt = dto.UpdatedAt,
        };

        var json = dto.ReportJson ?? string.Empty;
        if (json.Length > ChunkSize * ChunkCount)
        {
            throw new InvalidOperationException($"An analysis report is limited to {ChunkSize * ChunkCount} characters.");
        }

        var chunks = new string?[ChunkCount];
        for (var i = 0; i < ChunkCount && i * ChunkSize < json.Length; i++)
        {
            chunks[i] = json.Substring(i * ChunkSize, Math.Min(ChunkSize, json.Length - (i * ChunkSize)));
        }

        (entity.Json0, entity.Json1, entity.Json2, entity.Json3, entity.Json4, entity.Json5, entity.Json6, entity.Json7) =
            (chunks[0], chunks[1], chunks[2], chunks[3], chunks[4], chunks[5], chunks[6], chunks[7]);
        return entity;
    }

    public AnalysisRecordDto ToDto()
    {
        var json = string.Concat(Json0, Json1, Json2, Json3, Json4, Json5, Json6, Json7);
        return new AnalysisRecordDto(
            MatchId.From(PartitionKey),
            Enum.TryParse<AnalysisStatus>(Status, ignoreCase: true, out var status) ? status : AnalysisStatus.Queued,
            json.Length == 0 ? null : json,
            Error,
            UpdatedAt);
    }
}

/// <summary>
/// One persona's record of one WATCH match. PartitionKey = the persona's initials, so a profile's whole record is a
/// single-partition read; RowKey = match id, so re-judging overwrites rather than double-counting.
/// </summary>
public sealed class WatchResultEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public DateTimeOffset At { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string Opponent { get; set; } = string.Empty;

    public bool Won { get; set; }

    public bool Draw { get; set; }

    public int Score { get; set; }

    public string StatsJson { get; set; } = string.Empty;

    public static WatchResultEntity From(WatchResultDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new WatchResultEntity
        {
            PartitionKey = dto.Initials,
            RowKey = dto.MatchId.Value,
            At = dto.At,
            Topic = dto.Topic,
            Opponent = dto.Opponent,
            Won = dto.Won,
            Draw = dto.Draw,
            Score = dto.Score,
            StatsJson = JsonSerializer.Serialize(dto.Stats),
        };
    }

    public WatchResultDto ToDto() => new(
        PartitionKey,
        MatchId.From(RowKey),
        At,
        Topic,
        Opponent,
        Won,
        Draw,
        Score,
        string.IsNullOrEmpty(StatsJson) ? AdvancedStatsDto.Empty : JsonSerializer.Deserialize<AdvancedStatsDto>(StatsJson) ?? AdvancedStatsDto.Empty);
}

/// <summary>
/// One person's record of one debate they spoke in, either mode, with the style snapshot their profile is built
/// from. Keyed like <see cref="WatchResultEntity"/>: PK the tag, RK the match.
/// </summary>
public sealed class FighterResultEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public DateTimeOffset At { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string Opponent { get; set; } = string.Empty;

    public bool Won { get; set; }

    public bool Draw { get; set; }

    public int Score { get; set; }

    public string StyleJson { get; set; } = string.Empty;

    /// <summary>Which engine the debate ran in, so a profile can say how much of it came from arguing people rather than personas.</summary>
    public string Mode { get; set; } = nameof(MatchMode.Fight);

    /// <summary>The account whose debate this was. Rows are read back filtered on it; see <see cref="FighterResultDto"/>.</summary>
    public string UserId { get; set; } = string.Empty;

    public static FighterResultEntity From(FighterResultDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new FighterResultEntity
        {
            PartitionKey = dto.Tag,
            UserId = dto.UserId,
            Mode = dto.Mode.ToString(),
            RowKey = dto.MatchId.Value,
            At = dto.At,
            Topic = dto.Topic,
            Opponent = dto.Opponent,
            Won = dto.Won,
            Draw = dto.Draw,
            Score = dto.Score,
            StyleJson = JsonSerializer.Serialize(dto.Style),
        };
    }

    public FighterResultDto ToDto() => new(
        PartitionKey,
        UserId,
        MatchId.From(RowKey),
        Enum.TryParse<MatchMode>(Mode, ignoreCase: true, out var mode) ? mode : MatchMode.Fight,
        At,
        Topic,
        Opponent,
        Won,
        Draw,
        Score,
        string.IsNullOrEmpty(StyleJson) ? StyleSnapshot.Empty : JsonSerializer.Deserialize<StyleSnapshot>(StyleJson) ?? StyleSnapshot.Empty);
}

/// <summary>A person, created by their first debate. One partition: the roster is small and always read whole.</summary>
public sealed class FighterEntity : ITableEntity
{
    public const string Partition = "fighter";

    public string PartitionKey { get; set; } = Partition;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The seat their persona argues from; see <see cref="FighterDto.Role"/>. Stored by name.</summary>
    public string Role { get; set; } = nameof(ProfileRole.Husband);

    public static FighterEntity From(FighterDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new FighterEntity
        {
            RowKey = dto.Tag,
            DisplayName = dto.DisplayName,
            CreatedAt = dto.CreatedAt,
            LastSeenAt = dto.LastSeenAt,
            Role = dto.Role.ToString(),
        };
    }

    public FighterDto ToDto() => new(
        RowKey,
        DisplayName,
        CreatedAt,
        LastSeenAt,
        Enum.TryParse<ProfileRole>(Role, ignoreCase: true, out var role) ? role : ProfileRole.Husband);
}
