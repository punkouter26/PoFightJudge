namespace PoMarriedFight.Api.Features.Analysis;

/// <summary>
/// Growing wait between status polls. A fixed one-or-two-second tick spends most of its budget waiting on work
/// that finished in the first few hundred milliseconds, which is pure dead time in front of the user.
/// </summary>
public sealed class PollBackoff(TimeSpan initial, TimeSpan max, TimeSpan budget, TimeProvider clock)
{
    private TimeSpan _next = initial;
    private TimeSpan _waited = TimeSpan.Zero;

    /// <summary>Waits for the next interval and returns false once the budget is spent.</summary>
    public async Task<bool> WaitAsync(CancellationToken ct)
    {
        if (_waited >= budget)
        {
            return false;
        }

        await Task.Delay(_next, clock, ct);
        _waited += _next;
        var grown = _next * 1.5;
        _next = grown < max ? grown : max;
        return true;
    }
}
