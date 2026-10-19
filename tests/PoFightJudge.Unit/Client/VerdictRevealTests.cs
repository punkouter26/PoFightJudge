using PoFightJudge.Client.Pages;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// A ruling that has only just come in is revealed — a drumroll, then the name. One opened from the history a day
/// later is a document, and is simply shown: a drumroll every time somebody looks something up would be a toll.
/// </summary>
public class VerdictRevealTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 21, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, true)]
    [InlineData(14, true)]
    [InlineData(16, false)]
    [InlineData(60 * 24, false)]
    public void Only_a_fresh_ruling_is_staged(int minutesOld, bool staged) =>
        Verdict.Staged(Now.AddMinutes(-minutesOld), Now).Should().Be(staged);
}
