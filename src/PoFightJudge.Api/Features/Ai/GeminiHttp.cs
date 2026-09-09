using System.Text.Json.Nodes;

namespace PoFightJudge.Api.Features.Ai;

/// <summary>
/// The named <see cref="HttpClient"/>s the Gemini REST clients talk through — one per latency class, because one
/// shared client means one shared timeout, and a dialogue line that should answer in a second or two must not share
/// its budget with a voice call that legitimately runs for most of a minute.
/// </summary>
public static class GeminiHttpClients
{
    /// <summary>Short interactive text calls: a round line, the WATCH judge, a generated profile.</summary>
    public const string Fast = "gemini-fast";

    /// <summary>Speech synthesis — legitimately slow, so it gets its own budget.</summary>
    public const string Tts = "gemini-tts";

    /// <summary>
    /// Streaming rounds. Deliberately no resilience handler: a per-attempt timeout disposes its cancellation source
    /// once <c>SendAsync</c> returns at the response headers, which cuts the body stream out from under the reader,
    /// and a retry cannot un-send tokens the client has already rendered. The client timeout is the whole guard.
    /// </summary>
    public const string Stream = "gemini-stream";

    /// <summary>The FIGHT analysis surface (Files API, transcription, the flex-tier judge): long, retried by <see cref="GeminiRetryHandler"/>.</summary>
    public const string Analysis = "gemini-analysis";

    public static IReadOnlyList<string> All { get; } = [Fast, Tts, Stream, Analysis];
}

/// <summary>Gemini REST plumbing shared by every client: endpoint, key header, error handling.</summary>
public static class GeminiHttp
{
    public const string ApiKeyHeader = "x-goog-api-key";

    public static Uri RestBase { get; } = new("https://generativelanguage.googleapis.com/");

    /// <summary>Turns a non-success answer into an <see cref="HttpRequestException"/> that names the operation and quotes the (truncated) body.</summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Gemini {operation} failed: {(int)response.StatusCode} {Truncate(body)}", null, response.StatusCode);
    }

    public static string Truncate(string s, int max = 400) => s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "…");
}

/// <summary>
/// The token counts a <c>generateContent</c> answer reports, as the ledger wants them. Shared rather than private
/// to one client: the judge sends by far the largest prompt in the app and used to report none of this, because it
/// builds its own payloads and never went past <see cref="GeminiTextClient"/>.
/// </summary>
/// <param name="PromptTokens">Everything sent, cached or not.</param>
/// <param name="OutputTokens">
/// What came back, with thinking folded in. Thinking tokens bill as output and are invisible in the text, so
/// leaving them out is what would hide the cost of a raised <c>thinkingLevel</c>.
/// </param>
/// <param name="CachedTokens">
/// The slice of the prompt served from the provider's context cache — the number that says whether cache-friendly
/// prompt ordering, and the judge's explicit cache, are actually paying.
/// </param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct GeminiUsage(int PromptTokens, int OutputTokens, int CachedTokens)
{
    /// <summary>Reads what is there. Anything missing, or not a number, counts as nothing rather than as a failure.</summary>
    public static GeminiUsage Read(JsonObject? usage)
    {
        if (usage is null)
        {
            return default;
        }

        return new GeminiUsage(
            Count(usage, "promptTokenCount"),
            Count(usage, "candidatesTokenCount") + Count(usage, "thoughtsTokenCount"),
            Count(usage, "cachedContentTokenCount"));

        static int Count(JsonObject usage, string key) =>
            usage[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
    }
}
