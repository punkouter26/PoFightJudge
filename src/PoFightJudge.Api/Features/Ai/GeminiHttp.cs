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
