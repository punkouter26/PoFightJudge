namespace PoMarriedFight.Unit;

/// <summary>T01 placeholder: proves the test tier wires up. Real suites arrive with each task.</summary>
public class SolutionTests
{
    [Fact]
    public void Every_project_builds_and_the_runner_works() =>
        typeof(Program).Assembly.GetName().Name.Should().Be("PoMarriedFight.Api");
}
