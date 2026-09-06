namespace PoMarriedFight.Api.Features.Diagnostics;

/// <summary>
/// Set once at startup when required configuration is missing in Production. The host stays up — static shell and
/// <c>/api/diag</c> keep serving so the problem can be seen — while every other <c>/api/*</c> route answers 503 and
/// <c>/api/health</c> fails, so a misconfigured deploy is never mistaken for a healthy one.
/// </summary>
public sealed class StartupHealthState
{
    private readonly Lock _lock = new();
    private string[] _missing = [];

    public bool IsDegraded => _missing.Length > 0;

    public IReadOnlyList<string> MissingKeys => _missing;

    public void MarkDegraded(IEnumerable<string> missingKeys)
    {
        lock (_lock)
        {
            _missing = [.. missingKeys];
        }
    }
}
