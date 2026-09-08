using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bogus;
using PoFightJudge.Api.Features.Diagnostics;

namespace PoFightJudge.Api.Features.Ai.Fakes;

/// <summary>
/// The deterministic stand-in for <see cref="IGeminiText"/> when there is no key (or the flag forces it). It reads
/// the request's response schema and synthesizes JSON that satisfies it — objects, enums, integer ranges, arrays —
/// so every schema'd caller (profile generation, WATCH rounds, the judge) gets a well-formed answer without a
/// hand-written fake per feature. Seeded from the prompt, so the same input yields the same output.
/// </summary>
public sealed class FakeGeminiText(AiLatencyTracker latency, TimeSpan? chunkDelay = null) : IGeminiText
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] Lines =
    [
        "Look, the thing is, I did the dishes on Tuesday and nobody threw me a parade.",
        "Okay so, that is not what happened and you know it.",
        "Honestly? The thermostat is a shared resource and I am the only one sharing.",
        "Can I just say, the calendar exists for a reason and the reason is you.",
    ];

    private readonly TimeSpan _chunkDelay = chunkDelay ?? TimeSpan.FromMilliseconds(30);

    public bool IsFake => true;

    public Task<string> GenerateAsync(GeminiTextRequest request, CancellationToken ct = default)
    {
        var text = Synthesize(request);
        latency.Record(request.Operation, 1);
        latency.RecordUsage(request.Operation, promptTokens: Estimate(request.Prompt), outputTokens: text.Length / 4, cachedTokens: Estimate(request.Prompt) / 2);
        return Task.FromResult(text);
    }

    public async IAsyncEnumerable<string> StreamAsync(GeminiTextRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var text = await GenerateAsync(request, ct);
        latency.RecordTimeToFirstToken(request.Operation, 1);
        const int chunk = 6;
        for (var i = 0; i < text.Length; i += chunk)
        {
            if (_chunkDelay > TimeSpan.Zero)
            {
                await Task.Delay(_chunkDelay, ct);
            }

            yield return text.Substring(i, Math.Min(chunk, text.Length - i));
        }
    }

    /// <summary>Schema in, conforming JSON out; without a schema, one canned spoken line.</summary>
    public static string Synthesize(GeminiTextRequest request)
    {
        var faker = new Faker("en") { Random = new Randomizer(Seed(request.Prompt.User)) };
        if (request.ResponseSchema is null)
        {
            return faker.PickRandom(Lines);
        }

        return FromSchema(request.ResponseSchema, faker, "root", depth: 0)?.ToJsonString(JsonOptions) ?? "{}";
    }

    private static int Seed(string text)
    {
        // A stable hash: string.GetHashCode is randomized per process, which would make the fake non-deterministic.
        unchecked
        {
            var hash = 17;
            foreach (var c in text)
            {
                hash = (hash * 31) + c;
            }

            return hash;
        }
    }

    private static int Estimate(GeminiPrompt prompt) => (prompt.System.Length + prompt.User.Length) / 4;

    private static JsonNode? FromSchema(JsonNode schema, Faker faker, string name, int depth)
    {
        if (schema is not JsonObject s || depth > 8)
        {
            return null;
        }

        if (s["enum"] is JsonArray options && options.Count > 0)
        {
            return JsonValue.Create(options[faker.Random.Int(0, options.Count - 1)]?.GetValue<string>() ?? string.Empty);
        }

        var type = s["type"]?.GetValue<string>()?.ToUpperInvariant() ?? "OBJECT";
        switch (type)
        {
            case "OBJECT":
                var result = new JsonObject();
                if (s["properties"] is JsonObject properties)
                {
                    foreach (var property in properties)
                    {
                        if (property.Value is not null)
                        {
                            result[property.Key] = FromSchema(property.Value, faker, property.Key, depth + 1);
                        }
                    }
                }

                return result;
            case "ARRAY":
                var min = (int?)Number(s["minItems"]) ?? 1;
                var max = (int?)Number(s["maxItems"]) ?? Math.Max(min, 2);
                var array = new JsonArray();
                var count = faker.Random.Int(min, Math.Max(min, max));
                for (var i = 0; i < count; i++)
                {
                    array.Add(s["items"] is { } items ? FromSchema(items, faker, name, depth + 1) : JsonValue.Create(faker.Lorem.Word()));
                }

                return array;
            case "INTEGER":
                var lo = (int?)Number(s["minimum"]) ?? 0;
                var hi = (int?)Number(s["maximum"]) ?? 100;
                return JsonValue.Create(faker.Random.Int(lo, Math.Max(lo, hi)));
            case "NUMBER":
                var nlo = Number(s["minimum"]) ?? 0;
                var nhi = Number(s["maximum"]) ?? 1;
                return JsonValue.Create(Math.Round(faker.Random.Double(nlo, Math.Max(nlo, nhi)), 2));
            case "BOOLEAN":
                return JsonValue.Create(faker.Random.Bool());
            default:
                var text = StringFor(name, faker);
                var minLength = (int?)Number(s["minLength"]) ?? 0;
                while (text.Length < minLength)
                {
                    text += " " + faker.Lorem.Sentence(8);
                }

                return JsonValue.Create(text);
        }
    }

    /// <summary>Schema bounds arrive as whatever numeric type the author used; a JsonValue only converts to its own.</summary>
    private static double? Number(JsonNode? node) =>
        node is JsonValue value
            ? value.TryGetValue<double>(out var d) ? d
            : value.TryGetValue<int>(out var i) ? i
            : value.TryGetValue<long>(out var l) ? l
            : value.TryGetValue<decimal>(out var m) ? (double)m
            : null
            : null;

    /// <summary>A few property-name heuristics keep the fake readable where it shows on screen; everything else is a sentence.</summary>
    private static string StringFor(string name, Faker faker)
    {
        var key = name.ToLowerInvariant();
        if (key.Contains("initials", StringComparison.Ordinal))
        {
            return faker.Random.String2(3, "ABCDEFGHJKLMNPQRSTUVWXYZ");
        }

        if (key.EndsWith("name", StringComparison.Ordinal))
        {
            return faker.Name.FullName();
        }

        if (key.Contains("occupation", StringComparison.Ordinal) || key.Contains("job", StringComparison.Ordinal))
        {
            return faker.Name.JobTitle();
        }

        if (key.Contains("line", StringComparison.Ordinal) || key.Contains("quote", StringComparison.Ordinal))
        {
            return faker.PickRandom(Lines);
        }

        if (key.Contains("age", StringComparison.Ordinal))
        {
            return faker.Random.Int(25, 70).ToString(CultureInfo.InvariantCulture);
        }

        return faker.Lorem.Sentence(faker.Random.Int(5, 12));
    }
}
