using PoFightJudge.Api.Common;

namespace PoFightJudge.Unit.Common;

public class OutcomeTests
{

    [Fact]
    public void Generic_outcome_carries_a_value_only_on_success()
    {
        var ok = Outcome.Success(42);
        ok.IsSuccess.Should().BeTrue();
        ok.Value.Should().Be(42);
        ok.Error.Should().BeNull();

        var failed = Outcome.Failure<int>("boom");
        failed.IsSuccess.Should().BeFalse();
        failed.Error.Should().Be("boom");
        failed.Invoking(f => f.Value).Should().Throw<InvalidOperationException>().WithMessage("*boom*");
    }
}
