using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Api.Features.Records;

/// <summary>One fight, as the search reads it: what it was about, when, and where it sits in meaning-space.</summary>
public sealed record FightVectorRow(MatchId Id, string Topic, DateTimeOffset At, IReadOnlyList<float> Vector);

/// <summary>
/// Storing and comparing a fight's embedding.
/// </summary>
/// <remarks>
/// A vector is a thousand-odd floats and the row it rides on is a Table Storage entity. Written as base64 of the
/// raw little-endian bytes it is four bytes a number; as the JSON array it would otherwise be it is nearer twelve,
/// and Table Storage caps a string property at 64 KB.
/// </remarks>
public static class FightVector
{
    public static string ToBase64(IReadOnlyList<float> vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Count == 0)
        {
            return string.Empty;
        }

        var bytes = new byte[vector.Count * sizeof(float)];
        for (var i = 0; i < vector.Count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), vector[i]);
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Back to floats. Anything unreadable is an empty vector rather than an exception: a corrupted row is a fight
    /// that cannot be found by meaning, which is a worse search result and not a failed request.
    /// </summary>
    public static IReadOnlyList<float> FromBase64(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return [];
        }

        try
        {
            var bytes = Convert.FromBase64String(stored);
            if (bytes.Length % sizeof(float) != 0)
            {
                return [];
            }

            var vector = new float[bytes.Length / sizeof(float)];
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
            }

            return vector;
        }
        catch (FormatException)
        {
            return [];
        }
    }

    /// <summary>
    /// Cosine similarity: direction only, so a longer summary does not outrank a shorter one for being longer.
    /// Vectors of different lengths score zero rather than throwing — a fight embedded by an older model is not a
    /// bad match, it is one this query cannot read.
    /// </summary>
    public static double Similarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count == 0 || left.Count != right.Count)
        {
            return 0;
        }

        double dot = 0;
        double leftLength = 0;
        double rightLength = 0;
        for (var i = 0; i < left.Count; i++)
        {
            dot += (double)left[i] * right[i];
            leftLength += (double)left[i] * left[i];
            rightLength += (double)right[i] * right[i];
        }

        if (leftLength == 0 || rightLength == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftLength) * Math.Sqrt(rightLength));
    }
}

/// <summary>Ranking a history by meaning, and deciding what about a fight is worth embedding.</summary>
public static class FightSearch
{
    /// <summary>
    /// Below this a result is noise. Cosine similarity on a good embedding model puts genuinely related text well
    /// above it; without a floor, every search returns the whole history in a confident-looking order.
    /// </summary>
    public const double MinimumSimilarity = 0.55;

    /// <summary>
    /// What gets embedded: the topic, who argued it, and the ruling. That is what somebody types into a search box
    /// when they are looking for a fight — "the one about the heating bill", "the one Sam won on evidence" — and
    /// the whole transcript would bury it under filler.
    /// </summary>
    public static string Describe(string topic, string side1, string side2, string? summary)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{side1} argued {side2} about {topic}.");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            text.Append(' ').Append(summary.Trim());
        }

        return text.ToString();
    }

    /// <summary>The closest fights, closest first. One that was never embedded is skipped rather than ranked last.</summary>
    public static IReadOnlyList<FightVectorRow> Rank(
        IEnumerable<FightVectorRow> rows,
        IReadOnlyList<float> query,
        int take,
        double minimum = MinimumSimilarity)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Count == 0 || take <= 0)
        {
            return [];
        }

        return
        [
            .. rows
                .Where(r => r.Vector.Count > 0)
                .Select(r => (Row: r, Score: FightVector.Similarity(r.Vector, query)))
                .Where(x => x.Score >= minimum)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Row.At)
                .Take(take)
                .Select(x => x.Row),
        ];
    }
}
