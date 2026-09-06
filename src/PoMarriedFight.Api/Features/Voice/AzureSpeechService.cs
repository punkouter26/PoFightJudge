using System.Diagnostics;
using System.Text;
using PoMarriedFight.Api.Features.Diagnostics;
using PoMarriedFight.Api.Features.Profiles;

namespace PoMarriedFight.Api.Features.Voice;

/// <summary>Whether Azure Speech is usable: a key and a region are configured.</summary>
public sealed record AzureSpeechOptions(bool Enabled, string Key = "", string Region = "");

/// <summary>
/// Azure Speech REST TTS. Answers in ~1–2 s against the 12–17 s measured on Gemini TTS, at cents per million
/// characters — which is why the fast-first chain prefers it whenever a key is configured.
/// </summary>
public sealed partial class AzureSpeechService(IHttpClientFactory httpClients, AzureSpeechOptions options, TtsRoutingOptions routing, AiLatencyTracker latency, ILogger<AzureSpeechService> logger) : ITtsProvider
{
    public const string ProviderName = "azure";

    public const string ClientName = "azure-speech";

    public const string Operation = "tts.azure";

    /// <summary>The Speech endpoint rejects a request with no User-Agent with an HTTP 400 and an empty body, and HttpClient sends none by default. Do not remove.</summary>
    public const string UserAgent = "PoMarriedFight/1.0";

    // Slider-to-prosody conversion. Persona pitch runs ~0.6–1.5 and speed ~0.85–1.35. Azure honours numeric prosody
    // directly where Gemini only ever read the sliders as a loose natural-language hint, so applying stored values at
    // face value changes what they mean: 1.4/1.3 became +20% pitch at +30% rate, which made the wife a chipmunk.
    // Damp and clamp so the sliders still separate the characters audibly but stay inside a human range.
    public const double PitchPercentPerUnit = 25.0;
    public const double RatePercentPerUnit = 40.0;
    public const int MaxPitchPercent = 12;
    public const int MaxRatePercent = 15;

    /// <summary>Gemini voice name → closest Azure neural voice. Personas store Gemini names, and <c>TtsSettings.Normalize</c> guarantees one of these five.</summary>
    private static readonly Dictionary<string, string> VoiceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Charon"] = "en-US-GuyNeural",
        ["Puck"] = "en-US-DavisNeural",
        ["Fenrir"] = "en-US-TonyNeural",
        ["Kore"] = "en-US-JennyNeural",
        ["Zephyr"] = "en-US-AriaNeural",
    };

    public string Name => ProviderName;

    public bool IsFake => false;

    public bool CanSpeak(TtsSettings settings) => options.Enabled;

    /// <summary>mp3 at 48 kbit/s is ~10× smaller than the equivalent PCM and indistinguishable for speech; the payload also travels base64 inside JSON, so the saving lands twice.</summary>
    public static string OutputFormatHeader(string format) =>
        string.Equals(format, TtsAudioFormats.Mp3, StringComparison.OrdinalIgnoreCase) ? "audio-24khz-48kbitrate-mono-mp3" : "raw-24khz-16bit-mono-pcm";

    public static string BuildSsml(string text, TtsSettings settings)
    {
        var voice = VoiceMap.GetValueOrDefault(settings.VoiceName, "en-US-GuyNeural");
        var pitch = Math.Clamp((int)Math.Round((settings.Pitch - 1.0) * PitchPercentPerUnit), -MaxPitchPercent, MaxPitchPercent);
        var rate = Math.Clamp((int)Math.Round((settings.Speed - 1.0) * RatePercentPerUnit), -MaxRatePercent, MaxRatePercent);

        return $"""
            <speak version='1.0' xml:lang='en-US'>
              <voice name='{voice}'>
                <prosody pitch='{(pitch >= 0 ? "+" : string.Empty)}{pitch}%' rate='{(rate >= 0 ? "+" : string.Empty)}{rate}%'>{Escape(text)}</prosody>
              </voice>
            </speak>
            """;
    }

    public async Task<TtsAudio> SynthesizeAsync(string text, TtsSettings settings, CancellationToken ct = default)
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("Azure Speech is not configured (key or region missing).");
        }

        var format = TtsAudioFormats.Normalize(routing.WireFormat);
        var ssml = BuildSsml(text, settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"https://{options.Region}.tts.speech.microsoft.com/cognitiveservices/v1"))
        {
            Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml"),
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", options.Key);
        request.Headers.Add("X-Microsoft-OutputFormat", OutputFormatHeader(format));
        request.Headers.Add("User-Agent", UserAgent);

        var client = httpClients.CreateClient(ClientName);
        var started = Stopwatch.GetTimestamp();
        using var response = await client.SendAsync(request, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var elapsed = Stopwatch.GetElapsedTime(started);
        latency.Record(Operation, elapsed.TotalMilliseconds);
        LogCompleted(logger, elapsed.TotalMilliseconds, (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            // Azure explains a rejected request in the body (bad voice, malformed SSML, wrong output format), and
            // sometimes rejects with an empty one — so the envelope is logged too. Never the key, never the dialogue.
            var detail = Encoding.UTF8.GetString(bytes);
            LogFailed(logger, (int)response.StatusCode, detail.Length > 300 ? detail[..300] : detail, options.Region, settings.VoiceName, ssml.Length);
            throw new HttpRequestException($"Azure Speech returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }

        return bytes.Length == 0 ? TtsAudio.None : new TtsAudio(Convert.ToBase64String(bytes), format);
    }

    /// <summary>Escapes XML-special characters so dialogue cannot break the SSML document.</summary>
    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information, Message = "Azure Speech TTS completed in {ElapsedMs:F0}ms (HTTP {Status})")]
    private static partial void LogCompleted(ILogger logger, double elapsedMs, int status);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Error, Message = "Azure Speech rejected the request (HTTP {Status}): {Detail} [region={Region} voice={Voice} ssmlLen={SsmlLength}]")]
    private static partial void LogFailed(ILogger logger, int status, string detail, string region, string voice, int ssmlLength);
}
