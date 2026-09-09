using Bunit;
using Microsoft.Extensions.Time.Testing;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Driving the round loop's beat on a fake clock.
/// </summary>
/// <remarks>
/// The loop waits out a line with <c>Task.Delay(wait, Clock, token)</c>, and a single <c>Advance</c> races the
/// registration of that timer: land before the delay has been created and the time it was meant to consume is
/// already spent, so the delay never fires and the loop stops for good. The test then waits its full timeout for a
/// state nothing is going to reach.
///
/// The race is not new — it is inherent to advancing a fake clock at code that has not asked it for a timer yet —
/// but every await between rendering a line and reaching the beat widens the window, and the audio prefetch added
/// a few. Advancing until the page actually moves is the fix, and it is what the longer-running tests here already
/// did by playing a whole match out a beat at a time.
/// </remarks>
internal static class BeatDriver
{
    /// <summary>How many beats to spend waiting for one thing. Well past any state this loop reaches in one turn.</summary>
    public const int MaxBeats = 12;

    /// <summary>
    /// Advances the clock a beat at a time until <paramref name="selector"/> matches something, or the beats run
    /// out. It does not assert: the caller's own wait is what says whether the page got where it was going, and
    /// this only makes sure the clock is not the reason it did not.
    /// </summary>
    public static async Task AdvanceUntilAsync<T>(
        this IRenderedComponent<T> cut,
        FakeTimeProvider clock,
        string selector,
        double seconds = 5)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(clock);

        for (var beat = 0; beat < MaxBeats && cut.FindAll(selector).Count == 0; beat++)
        {
            await cut.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(seconds)));
        }
    }
}
