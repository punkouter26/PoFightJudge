namespace PoFightJudge.Client.Services;

/// <summary>
/// Whether the API is answering at all. Nothing polls for this: every call the app already makes reports what
/// happened, so the banner appears on the first failure and goes away on the first success rather than up to thirty
/// seconds either side of the truth. On the F1 tier a heartbeat would also be the most frequent request the site made.
/// </summary>
public sealed class ConnectionState
{
    /// <summary>
    /// One failure is a failure; it takes this many in a row to say the server is unreachable. A single request lost
    /// to a flaky connection is not worth a banner, and the retry that follows usually succeeds.
    /// </summary>
    public const int FailuresBeforeSaying = 2;

    private int _failures;

    /// <summary>Raised when the answer to <see cref="IsReachable"/> changes, and only then.</summary>
    public event EventHandler? Changed;

    public bool IsReachable { get; private set; } = true;

    /// <summary>True when the server is up but refusing to work — a missing key in Production, per SPEC §11.</summary>
    public bool IsDegraded { get; private set; }

    public void ReportSuccess()
    {
        _failures = 0;
        Set(reachable: true, degraded: false);
    }

    /// <summary>The server answered, but said it cannot do its job. Reachable, so the shell keeps working.</summary>
    public void ReportDegraded()
    {
        _failures = 0;
        Set(reachable: true, degraded: true);
    }

    public void ReportFailure()
    {
        _failures++;
        if (_failures >= FailuresBeforeSaying)
        {
            Set(reachable: false, degraded: false);
        }
    }

    private void Set(bool reachable, bool degraded)
    {
        if (reachable == IsReachable && degraded == IsDegraded)
        {
            return;
        }

        IsReachable = reachable;
        IsDegraded = degraded;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Reports every call's fate to <see cref="ConnectionState"/>. A handler rather than something inside the API client,
/// so a call made anywhere — the hub's negotiate, a page's own fetch — counts the same way.
/// </summary>
public sealed class ConnectionWatchHandler(ConnectionState state) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Nothing answered: DNS, the socket, or the server being down. This is the case the banner is for.
            state.ReportFailure();
            throw;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout rather than somebody leaving the page.
            state.ReportFailure();
            throw;
        }

        // 503 is the degraded gate in Program.cs: configuration is missing and /api/* will not serve. Anything else
        // that came back at all — a 404, a 429, a validation problem — is the server working exactly as intended.
        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            state.ReportDegraded();
        }
        else
        {
            state.ReportSuccess();
        }

        return response;
    }
}
