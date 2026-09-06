using PoMarriedFight.Client.Components;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Client;

public class TensionCalculatorTests
{
    private static WatchRoundDto Line(string mood, string speaker = WatchTurns.Husband) => new(speaker, "words", mood);

    [Fact]
    public void An_argument_that_has_not_started_is_not_tense()
    {
        TensionCalculator.Tension([]).Should().Be(0);
        TensionCalculator.Level(0).Should().Be("Civil");
    }

    [Fact]
    public void The_calm_end_and_the_furious_end_span_the_whole_scale()
    {
        var calm = TensionCalculator.Tension([Line("calm"), Line("calm"), Line("calm")]);
        var furious = TensionCalculator.Tension([Line("furious"), Line("furious"), Line("furious")]);

        calm.Should().BeLessThan(20);
        furious.Should().Be(100);
        TensionCalculator.Level(calm).Should().Be("Civil");
        TensionCalculator.Level(furious).Should().Be("Nuclear");
    }

    [Fact]
    public void What_was_just_said_counts_for_more_than_what_was_said_first()
    {
        // The meter reads the temperature now, not the average of the whole match: an argument that cooled off
        // must not still show red, and one that just boiled over must show it immediately.
        var escalating = TensionCalculator.Tension([Line("calm"), Line("furious")]);
        var cooling = TensionCalculator.Tension([Line("furious"), Line("calm")]);

        escalating.Should().BeGreaterThan(cooling);
    }

    [Fact]
    public void A_slap_raises_the_temperature_of_the_same_transcript()
    {
        WatchRoundDto[] rounds = [Line("smug"), Line("frustrated")];

        var slapped = TensionCalculator.Tension(rounds, slapped: true);

        slapped.Should().BeGreaterThan(TensionCalculator.Tension(rounds));
    }

    [Fact]
    public void Every_mood_the_wire_allows_reads_somewhere_on_the_scale_and_an_unknown_one_does_not_throw()
    {
        foreach (var mood in WatchTurns.Moods)
        {
            var value = TensionCalculator.Tension([Line(mood)]);
            value.Should().BeInRange(0, 100);
        }

        // An unrecognised mood is normalised the same way the server normalises it, rather than reading as zero.
        TensionCalculator.Tension([Line("incandescent")]).Should().Be(TensionCalculator.Tension([Line("angry")]));
    }

    [Fact]
    public void The_scale_never_leaves_its_bounds_however_hot_it_gets()
    {
        var everything = WatchTurns.Moods.Select(m => Line(m)).ToList();

        TensionCalculator.Tension(everything, slapped: true).Should().BeInRange(0, 100);
        TensionCalculator.Level(100).Should().Be("Nuclear");
    }
}
