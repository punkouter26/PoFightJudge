using PoFightJudge.Client.Services;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// A microphone that would not open is told in words somebody can act on, the same on every page that opens one.
/// "AbortError" used to reach the page as itself; it is two different problems, and each now says which.
/// </summary>
public class MicErrorTests
{
    [Theory]
    [InlineData("NotAllowedError", "Allow it for this site")]
    [InlineData("NotFoundError", "No microphone was found")]
    [InlineData("NotReadableError", "Something else is holding the microphone")]
    [InlineData("AbortError", "would not start")]
    [InlineData("AudioLoadError", "Reload the page")]
    [InlineData("SomethingNew", "(SomethingNew)")]
    public void Each_failure_says_what_to_do_about_it(string error, string advice)
    {
        MicError.Describe(error).Should().Contain(advice);
        MicError.Describe("AbortError").Should().NotContain("(AbortError)", "nobody can act on the browser's own name for it");
    }
}
