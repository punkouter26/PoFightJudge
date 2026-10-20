using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The one guard every effect, sound and canvas call goes through. None of them is load-bearing: a browser that
/// refuses, a prerender with no browser yet, or a page that moved on changes nothing about the match.
/// </summary>
internal static class BestEffort
{
    public static async Task JsAsync(Func<ValueTask> call)
    {
        try
        {
            await call();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Decoration that did not happen. The page is unaffected.
        }
    }
}
