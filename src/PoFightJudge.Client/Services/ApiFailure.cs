namespace PoFightJudge.Client.Services;

/// <summary>
/// What to tell somebody when a call to the API did not work. There are two ways it fails — the server answered
/// with a problem, or nothing answered at all — and every page was catching the pair and wording the second one
/// itself.
/// </summary>
public static class ApiFailure
{
    /// <summary>
    /// The sentence for the page, or null when <paramref name="exception"/> is not a failed call and should carry
    /// on up. <paramref name="what"/> says what could not be done, without a full stop: "Your history could not be
    /// loaded".
    /// </summary>
    public static string? Describe(Exception exception, string what) => exception switch
    {
        ApiException api => api.Summary,
        HttpRequestException http => $"{what} ({http.Message}).",
        _ => null,
    };
}
