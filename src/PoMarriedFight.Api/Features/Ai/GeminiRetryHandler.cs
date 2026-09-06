using System.Net;

namespace PoMarriedFight.Api.Features.Ai;

/// <summary>
/// Retries the transient half of the Gemini REST surface for the analysis client. Without this a single 429 during
/// the judge call fails a whole analysis into a user-facing error, and the recording is only analysed again if
/// someone clicks retry. The resumable-upload legs are skipped: replaying a finalize could leave a duplicate file.
/// </summary>
public sealed partial class GeminiRetryHandler(TimeProvider clock, ILogger<GeminiRetryHandler> logger) : DelegatingHandler
{
    public const int MaxAttempts = 3;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(20);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!CanRetry(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            TimeSpan delay;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (attempt >= MaxAttempts || !IsTransient(response.StatusCode))
                {
                    return response;
                }

                delay = RetryAfter(response) ?? Backoff(attempt);
                LogRetrying(logger, request.RequestUri?.AbsolutePath ?? "?", (int)response.StatusCode, attempt, delay.TotalMilliseconds);
                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                delay = Backoff(attempt);
                LogRetryingAfterError(logger, request.RequestUri?.AbsolutePath ?? "?", attempt, delay.TotalMilliseconds, ex);
            }

            await Task.Delay(delay, clock, cancellationToken);
        }
    }

    /// <summary>The bodies we send are small and buffered, so a retry can reuse the request — except mid-upload.</summary>
    private static bool CanRetry(HttpRequestMessage request) => !request.Headers.Contains("X-Goog-Upload-Command");

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var after = response.Headers.RetryAfter;
        var value = after?.Delta ?? (after?.Date is { } date ? date - clock.GetUtcNow() : null);
        return value is { } d && d > TimeSpan.Zero ? (d < MaxDelay ? d : MaxDelay) : null;
    }

    /// <summary>Exponential with full jitter, so concurrent analyses do not retry in lockstep.</summary>
    private static TimeSpan Backoff(int attempt)
    {
        var window = BaseDelay * Math.Pow(2, attempt - 1);
        var capped = window < MaxDelay ? window : MaxDelay;
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capped.TotalMilliseconds);
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "Gemini {Path} returned {Status} (attempt {Attempt}); retrying in {Delay:F0}ms")]
    private static partial void LogRetrying(ILogger logger, string path, int status, int attempt, double delay);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning, Message = "Gemini {Path} failed (attempt {Attempt}); retrying in {Delay:F0}ms")]
    private static partial void LogRetryingAfterError(ILogger logger, string path, int attempt, double delay, Exception ex);
}
