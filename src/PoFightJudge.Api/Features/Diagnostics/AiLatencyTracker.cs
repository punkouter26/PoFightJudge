using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Diagnostics;

/// <summary>
/// In-memory rolling window of AI call latencies per operation ("round", "round.ttft", "judge", "profile", "tts"…)
/// plus a running token ledger. Singleton, thread-safe, deliberately tiny — a p50/p95 readout on <c>/api/diag</c>,
/// not a metrics platform; App Insights still gets the dependency telemetry. Streams record <c>{op}.ttft</c>
/// separately because total wall time says little about when the first word appears, and the ledger keeps the
/// token counts the API returns so a prompt change can be shown to have reduced cost (or hit the context cache).
/// </summary>
public sealed class AiLatencyTracker
{
    public const int WindowSize = 200;

    public const string TimeToFirstTokenSuffix = ".ttft";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<double>> _samples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TokenLedger> _tokens = new(StringComparer.Ordinal);

    public void Record(string operation, double milliseconds)
    {
        lock (_gate)
        {
            if (!_samples.TryGetValue(operation, out var list))
            {
                list = new List<double>(WindowSize);
                _samples[operation] = list;
            }

            if (list.Count >= WindowSize)
            {
                list.RemoveAt(0);
            }

            list.Add(milliseconds);
        }
    }

    public void RecordTimeToFirstToken(string operation, double milliseconds) => Record(operation + TimeToFirstTokenSuffix, milliseconds);

    /// <summary><paramref name="cachedTokens"/> is the slice of the prompt served from the provider's context cache — the number that says whether cache-friendly prompt ordering pays off.</summary>
    public void RecordUsage(string operation, int promptTokens, int outputTokens, int cachedTokens)
    {
        lock (_gate)
        {
            _tokens.TryGetValue(operation, out var ledger);
            _tokens[operation] = new TokenLedger(ledger.Calls + 1, ledger.PromptTokens + promptTokens, ledger.OutputTokens + outputTokens, ledger.CachedTokens + cachedTokens);
        }
    }

    public IReadOnlyList<AiLatencyDto> Snapshot()
    {
        lock (_gate)
        {
            return
            [
                .. _samples
                    .Select(kv =>
                    {
                        var sorted = kv.Value.Order().ToArray();
                        return new AiLatencyDto(kv.Key, sorted.Length, Percentile(sorted, 0.50), Percentile(sorted, 0.95), kv.Value[^1]);
                    })
                    .OrderBy(s => s.Operation, StringComparer.Ordinal),
            ];
        }
    }

    public IReadOnlyList<AiUsageDto> UsageSnapshot()
    {
        lock (_gate)
        {
            return
            [
                .. _tokens
                    .Select(kv => new AiUsageDto(kv.Key, kv.Value.Calls, kv.Value.PromptTokens, kv.Value.OutputTokens, kv.Value.CachedTokens))
                    .OrderBy(s => s.Operation, StringComparer.Ordinal),
            ];
        }
    }

    /// <summary>Nearest-rank percentile over a sorted window.</summary>
    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct TokenLedger(int Calls, long PromptTokens, long OutputTokens, long CachedTokens);
}
