using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace PoFightJudge.TestSupport;

/// <summary>
/// Scriptable <see cref="HttpMessageHandler"/> for the Gemini/Fish/Azure clients: enqueue responses (or a responder
/// that inspects the request), then assert on what was sent. Bodies are captured eagerly because the clients dispose
/// their request content.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, string?, HttpResponseMessage>> _responders = new();

    private readonly List<(HttpMethod Method, Uri? Uri, string? Body, IReadOnlyDictionary<string, string> Headers)> _requests = [];

    public IReadOnlyList<(HttpMethod Method, Uri? Uri, string? Body, IReadOnlyDictionary<string, string> Headers)> Requests => _requests;

    /// <summary>Answer the next request with this status and body.</summary>
    public FakeHttpHandler Enqueue(HttpStatusCode status, string body = "", string mediaType = "application/json")
    {
        _responders.Enqueue((_, _) => Json(status, body, mediaType));
        return this;
    }

    /// <summary>Answer the next request by inspecting it.</summary>
    public FakeHttpHandler Enqueue(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
    {
        _responders.Enqueue(responder);
        return this;
    }

    /// <summary>Answer every request that has no scripted response with this one (a "default route").</summary>
    public Func<HttpRequestMessage, string?, HttpResponseMessage> Fallback { get; set; } =
        (request, _) => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"No scripted response for {request.Method} {request.RequestUri}") };

    public static HttpResponseMessage Json(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    /// <summary>
    /// The one place test code is allowed to construct an HttpClient over a handler: production code goes through
    /// IHttpClientFactory (BannedSymbols.txt), a test needs the fake wired directly.
    /// </summary>
#pragma warning disable RS0030 // Banned API — see summary.
    public HttpClient CreateClient(string baseAddress = "https://generativelanguage.googleapis.com/") =>
        new(this, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
#pragma warning restore RS0030

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        lock (_requests)
        {
            _requests.Add((request.Method, request.RequestUri, body, headers));
        }

        var responder = _responders.TryDequeue(out var next) ? next : Fallback;
        return responder(request, body);
    }
}
