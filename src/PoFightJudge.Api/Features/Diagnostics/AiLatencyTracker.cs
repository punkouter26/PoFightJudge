using System.Diagnostics.Metrics;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Diagnostics;

/// <summary>
/// In-memory rolling window of AI call latencies per operation ("round", "round.ttft", "judge", "profile", "tts"…)
/// plus a running token ledger. Singleton, thread-safe, deliberately tiny — a p50/p95 readout on <c>/api/diag</c>,
/// not a metrics platform; App Insights still gets the dependency telemetry. Streams record <c>{op}.ttft</c>
/// separately because total wall time says little about when the first word appears, and the ledger keeps the
/// token counts the API returns so a prompt change can be shown to have reduced cost (or hit the context cache).
/// </summary>
public sealed class AiLatencyTracker(TimeProvider? clock = null)
{
    public const int WindowSize = 200;

    public const string TimeToFirstTokenSuffix = ".ttft";

    /// <summary>
    /// The same numbers as OpenTelemetry metrics. The window above is what <c>/api/diag</c> shows and it starts again
    /// every time the process does, which on a host that unloads when idle is several times a day; these go to
    /// Azure Monitor, where they are kept, and metrics are not sampled the way traces are.
    /// </summary>
    public const string MeterName = "PoFightJudge.Ai";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("ai.call.duration", "ms", "How long one call to a model took, by operation.");
    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>("ai.tokens", "{token}", "Tokens billed, by operation and kind (prompt, output, cached).");

    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<double>> _samples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TokenLedger> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _callerTokens = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateOnly _callerDay;

    private static readonly AsyncLocal<string?> CallerSlot = new();

    /// <summary>
    /// Who the AI work in this async flow is for. <see cref="AiBudgetGate"/> sets it per request and the analysis
    /// drain per job; work nobody is named for (a background persona rewrite) is counted per operation only.
    /// </summary>
    public static string? Caller
    {
        get => CallerSlot.Value;
        set => CallerSlot.Value = value;
    }

    public void Record(string operation, double milliseconds)
    {
        Duration.Record(milliseconds, new KeyValuePair<string, object?>("operation", operation));
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
        Count(operation, "prompt", promptTokens);
        Count(operation, "output", outputTokens);
        Count(operation, "cached", cachedTokens);
        lock (_gate)
        {
            _tokens.TryGetValue(operation, out var ledger);
            _tokens[operation] = new TokenLedger(ledger.Calls + 1, ledger.PromptTokens + promptTokens, ledger.OutputTokens + outputTokens, ledger.CachedTokens + cachedTokens);

            if (Caller is { } caller)
            {
                RollDay();
                _callerTokens[caller] = _callerTokens.GetValueOrDefault(caller) + promptTokens + outputTokens;
            }
        }
    }

    /// <summary>
    /// Prompt plus output tokens spent for one caller since midnight UTC.
    /// </summary>
    /// <remarks>
    /// ponytail: in memory, so a restart forgives the day. A runaway caller keeps the host warm, which is the case
    /// this guards; a table row per caller-day is the upgrade if restarts start mattering.
    /// </remarks>
    public long TokensToday(string caller)
    {
        lock (_gate)
        {
            RollDay();
            return _callerTokens.GetValueOrDefault(caller);
        }
    }

    /// <summary>How long until the allowance starts again.</summary>
    public TimeSpan UntilTomorrow()
    {
        var now = _clock.GetUtcNow();
        return new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero) - now;
    }

    /// <summary>Forgets yesterday the first time today is asked about. Called under the lock.</summary>
    private void RollDay()
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        if (today != _callerDay)
        {
            _callerTokens.Clear();
            _callerDay = today;
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

    private static void Count(string operation, string kind, int tokens) =>
        Tokens.Add(tokens, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("kind", kind));

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
