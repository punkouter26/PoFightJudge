using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Watch;

/// <summary>
/// Argument analytics for one speaker in one match, computed from the words they actually said. Everything is 0–100
/// except the raw fallacy count. <see cref="VolumeScore"/> is word-count-based, not decibels — the name says so
/// because the original did not, and people read it as loudness.
/// </summary>
public sealed record AdvancedStats(
    double PassiveAggressionIndex,
    double HistoricalGrievanceRate,
    double BlameMetric,
    int LogicalFallacyCount,
    double VolumeScore,
    double WordCountDominance,
    double EmotionalVolatility,
    double DeflectionCoefficient,
    double LexicalComplexity,
    double ApologyToInsultRatio)
{
    public static AdvancedStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public AdvancedStatsDto ToDto() => new(
        PassiveAggressionIndex,
        HistoricalGrievanceRate,
        BlameMetric,
        LogicalFallacyCount,
        VolumeScore,
        WordCountDominance,
        EmotionalVolatility,
        DeflectionCoefficient,
        LexicalComplexity,
        ApologyToInsultRatio);

    public static AdvancedStats FromDto(AdvancedStatsDto dto) => new(
        dto.PassiveAggressionIndex,
        dto.HistoricalGrievanceRate,
        dto.BlameMetric,
        dto.LogicalFallacyCount,
        dto.VolumeScore,
        dto.WordCountDominance,
        dto.EmotionalVolatility,
        dto.DeflectionCoefficient,
        dto.LexicalComplexity,
        dto.ApologyToInsultRatio);
}
