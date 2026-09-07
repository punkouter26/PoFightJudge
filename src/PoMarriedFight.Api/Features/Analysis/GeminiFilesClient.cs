using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using PoMarriedFight.Api.Features.Ai;

namespace PoMarriedFight.Api.Features.Analysis;

public sealed record GeminiFile(string Name, string Uri, string State, string MimeType);

/// <summary>The Files API: a resumable upload, then polling until the file is ready. Files live about two days.</summary>
public interface IGeminiFilesClient
{
    Task<GeminiFile> UploadAsync(Stream content, long length, string mimeType, string displayName, CancellationToken ct);

    Task<GeminiFile> GetAsync(string name, CancellationToken ct);

    /// <summary>Uploads and waits until the file is usable, checking soon and then less often.</summary>
    async Task<GeminiFile> UploadAndWaitAsync(Stream content, long length, string mimeType, string displayName, TimeProvider clock, CancellationToken ct)
    {
        var file = await UploadAsync(content, length, mimeType, displayName, ct);
        var backoff = new PollBackoff(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), clock);
        while (string.Equals(file.State, "PROCESSING", StringComparison.Ordinal) && await backoff.WaitAsync(ct))
        {
            file = await GetAsync(file.Name, ct);
        }

        return string.Equals(file.State, "ACTIVE", StringComparison.Ordinal)
            ? file
            : throw new InvalidOperationException($"Gemini file {file.Name} is {file.State}.");
    }
}

/// <summary>The resumable-upload protocol: start with the metadata, send the bytes, get the file back.</summary>
public sealed class GeminiFilesClient(IHttpClientFactory factory) : IGeminiFilesClient
{
    public async Task<GeminiFile> UploadAsync(Stream content, long length, string mimeType, string displayName, CancellationToken ct)
    {
        using var http = factory.CreateClient(GeminiHttpClients.Analysis);
        using var start = new HttpRequestMessage(HttpMethod.Post, "upload/v1beta/files");
        start.Headers.Add("X-Goog-Upload-Protocol", "resumable");
        start.Headers.Add("X-Goog-Upload-Command", "start");
        start.Headers.Add("X-Goog-Upload-Header-Content-Length", length.ToString(CultureInfo.InvariantCulture));
        start.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType);
        start.Content = new StringContent(
            new JsonObject { ["file"] = new JsonObject { ["display_name"] = displayName } }.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        using var startResponse = await http.SendAsync(start, ct);
        await GeminiHttp.EnsureSuccessAsync(startResponse, "files.start", ct);

        var uploadUrl = startResponse.Headers.TryGetValues("X-Goog-Upload-URL", out var urls) ? urls.FirstOrDefault() : null;
        if (string.IsNullOrEmpty(uploadUrl))
        {
            throw new InvalidOperationException("The Files API did not return an upload URL.");
        }

        using var upload = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        upload.Headers.Add("X-Goog-Upload-Offset", "0");
        upload.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
        upload.Content = new StreamContent(content);
        upload.Content.Headers.ContentLength = length;
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        using var uploadResponse = await http.SendAsync(upload, ct);
        await GeminiHttp.EnsureSuccessAsync(uploadResponse, "files.upload", ct);
        return Parse(JsonNode.Parse(await uploadResponse.Content.ReadAsStringAsync(ct))?["file"]);
    }

    public async Task<GeminiFile> GetAsync(string name, CancellationToken ct)
    {
        using var http = factory.CreateClient(GeminiHttpClients.Analysis);
        using var response = await http.GetAsync(new Uri($"v1beta/{name}", UriKind.Relative), ct);
        await GeminiHttp.EnsureSuccessAsync(response, "files.get", ct);
        return Parse(JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)));
    }

    public static GeminiFile Parse(JsonNode? file) => file is null
        ? throw new InvalidOperationException("The Files API returned no file.")
        : new GeminiFile(
            file["name"]?.GetValue<string>() ?? string.Empty,
            file["uri"]?.GetValue<string>() ?? string.Empty,
            file["state"]?.GetValue<string>() ?? "ACTIVE",
            file["mimeType"]?.GetValue<string>() ?? string.Empty);
}
