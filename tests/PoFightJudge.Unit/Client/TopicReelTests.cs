using PoFightJudge.Client.Components;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Spinning for a topic: the reel shows a run of topics from the heat that was asked for, never the same one twice in
/// a row, and lands on the last of them — slowing down as it goes, the way a reel does.
/// </summary>
public class TopicReelTests
{
    [Theory]
    [InlineData(TopicField.Heat.Mild)]
    [InlineData(TopicField.Heat.Medium)]
    [InlineData(TopicField.Heat.Spicy)]
    public void The_reel_only_shows_topics_of_the_heat_asked_for(TopicField.Heat heat)
    {
        var reel = TopicField.Reel(new Random(7), heat, 14);

        reel.Should().HaveCount(14).And.OnlyContain(t => TopicField.Topics[heat].Contains(t));
        reel.Zip(reel.Skip(1)).Should().NotContain(pair => pair.First == pair.Second, "a reel that does not move looks stuck");
    }

    [Fact]
    public void It_slows_down_before_it_lands()
    {
        var pauses = Enumerable.Range(0, 14).Select(frame => TopicField.Pause(frame, 14)).ToList();

        pauses.Should().BeInAscendingOrder();
        pauses[^1].Should().BeGreaterThan(pauses[0] * 3);
    }
}
