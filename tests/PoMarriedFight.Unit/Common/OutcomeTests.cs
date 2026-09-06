using PoMarriedFight.Api.Common;

namespace PoMarriedFight.Unit.Common;

public class OutcomeTests
{
    [Fact]
    public void Outcome_is_ok_or_carries_an_error()
    {
        Outcome.Ok.IsSuccess.Should().BeTrue();
        Outcome.Ok.Error.Should().BeNull();

        var failed = Outcome.Fail("Not in debate phase.");
        failed.IsSuccess.Should().BeFalse();
        failed.Error.Should().Be("Not in debate phase.");
    }

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

    [Fact]
    public void Match_routes_to_the_right_branch()
    {
        Outcome.Success("x").Match(v => v + "!", e => "err:" + e).Should().Be("x!");
        Outcome.Failure<string>("nope").Match(v => v + "!", e => "err:" + e).Should().Be("err:nope");
    }
}
