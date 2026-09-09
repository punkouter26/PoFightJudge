using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Profiles;

namespace PoFightJudge.Api.Features.Voice;

/// <summary>
/// Fish Audio configuration. <paramref name="Enabled"/> is the all-or-nothing gate (a key is present);
/// <paramref name="DefaultReferenceId"/> is an optional shared voice used by personas that carry none of their own.
/// </summary>
public sealed record FishAudioOptions(bool Enabled, string? DefaultReferenceId = null);

/// <summary>
/// Fish Audio REST TTS — the cloned-voice provider. A persona's <c>FishReferenceId</c> is the voice model id from
/// its fish.audio/m/{id} page; without one the shared default is used, and without either this provider is skipped.
/// </summary>
public sealed partial class FishAudioService(IHttpClientFactory httpClients, FishAudioOptions options, TtsRoutingOptions routing, AiLatencyTracker latency, ILogger<FishAudioService> logger) : ITtsProvider
{
    public const string ProviderName = "fish";

    public const string ClientName = "fish-audio";

    public const string Operation = "tts.fish";

    /// <summary>s2.1-pro-free is Fish's most advanced model offered free to developers; s2-pro needs paid credit and 402s without it.</summary>
    public const string Model = "s2.1-pro-free";

    /// <summary>Raw PCM is emitted at the pipeline's 24 kHz.</summary>
    public const int PcmSampleRate = 24_000;

    /// <summary>
    /// Fish accepts only 32000 or 44100 for <c>mp3</c> and rejects anything else with a 400 — a hard API constraint,
    /// not a tuning choice. Getting it wrong is nearly invisible: the request 400s, the chain falls through, and the
    /// line plays in somebody else's voice, which is exactly what a cloned-voice persona exists to avoid.
    /// </summary>
    public const int Mp3SampleRate = 44_100;

    public static Uri Endpoint { get; } = new("https://api.fish.audio/v1/tts");

    public string Name => ProviderName;

    public bool IsFake => false;

    public static int SampleRateFor(string format) =>
        string.Equals(format, TtsAudioFormats.Mp3, StringComparison.OrdinalIgnoreCase) ? Mp3SampleRate : PcmSampleRate;

    /// <summary>The voice this persona should speak in: its own reference id, else the shared default, else none.</summary>
    public string? ReferenceIdFor(TtsSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.FishReferenceId) ? settings.FishReferenceId : options.DefaultReferenceId;

    public bool CanSpeak(TtsSettings settings) => options.Enabled && !string.IsNullOrWhiteSpace(ReferenceIdFor(settings));

    public async Task<TtsAudio> SynthesizeAsync(string text, TtsSettings settings, CancellationToken ct = default)
    {
        var referenceId = ReferenceIdFor(settings)
            ?? throw new InvalidOperationException("Fish Audio has no reference id for this persona and no configured default.");
        var format = TtsAudioFormats.Normalize(routing.WireFormat);

        // Fish names its formats the way this pipeline does, so the wire value passes straight through.
        var payload = new JsonObject
        {
            ["text"] = text,
            ["reference_id"] = referenceId,
            ["format"] = format,
            ["sample_rate"] = SampleRateFor(format),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("model", Model);

        var client = httpClients.CreateClient(ClientName);
        var started = Stopwatch.GetTimestamp();
        using var response = await client.SendAsync(request, ct);
        var elapsed = Stopwatch.GetElapsedTime(started);
        latency.Record(Operation, elapsed.TotalMilliseconds);
        LogCompleted(logger, elapsed.TotalMilliseconds, (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var error = GeminiHttp.Truncate(await response.Content.ReadAsStringAsync(ct));
            LogFailed(logger, (int)response.StatusCode, error);
            throw new HttpRequestException($"Fish Audio returned {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length == 0 ? TtsAudio.None : new TtsAudio(Convert.ToBase64String(bytes), format);
    }

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information, Message = "Fish Audio TTS completed in {ElapsedMs:F0}ms (HTTP {Status})")]
    private static partial void LogCompleted(ILogger logger, double elapsedMs, int status);

    [LoggerMessage(EventId = 3202, Level = LogLevel.Warning, Message = "Fish Audio rejected the request (HTTP {Status}): {Detail}")]
    private static partial void LogFailed(ILogger logger, int status, string detail);
}
