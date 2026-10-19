using PoFightJudge.Client.Components;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The live share of the room: seconds a fighter actually spent talking while they had the floor. Not floor time —
/// the host hands the floor out in even slices, so that bar would sit in the middle all night. The browser counts
/// voiced seconds frame by frame; the clock credits each snapshot's worth of them to whoever held the floor.
/// </summary>
public class FloorClockTests
{
    [Fact]
    public void Nobody_has_said_anything_so_it_is_level() =>
        new FloorClock().Share.Should().Be(0.5);

    [Fact]
    public void Voice_heard_while_a_fighter_has_the_floor_is_theirs()
    {
        var clock = new FloorClock();
        clock.Tick(Speaker.Player1, hostSpeaking: false, voicedSeconds: 2.0);
        clock.Tick(Speaker.Player2, hostSpeaking: false, voicedSeconds: 2.8);
        clock.Tick(Speaker.Player2, hostSpeaking: false, voicedSeconds: 3.0);

        clock.One.Should().BeApproximately(0.8, 0.001, "what was voiced up to the hand-over belongs to whoever held the floor through it");
        clock.Two.Should().BeApproximately(0.2, 0.001);
        clock.Share.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void Voice_under_the_host_or_before_anybody_has_the_floor_counts_for_nobody()
    {
        var clock = new FloorClock();
        clock.Tick(null, hostSpeaking: false, voicedSeconds: 0);
        clock.Tick(Speaker.Player1, hostSpeaking: true, voicedSeconds: 0.5);
        clock.Tick(Speaker.Player1, hostSpeaking: false, voicedSeconds: 0.9);

        clock.One.Should().Be(0, "the room talking over the host is the host's time, not theirs");
    }

    [Fact]
    public void A_stall_in_the_snapshots_is_not_handed_to_whoever_had_the_floor_before_it()
    {
        var clock = new FloorClock();
        clock.Tick(Speaker.Player1, hostSpeaking: false, voicedSeconds: 0);
        clock.Tick(Speaker.Player2, hostSpeaking: false, voicedSeconds: 30);

        clock.One.Should().Be(FloorClock.LongestGap.TotalSeconds, "after half a minute without word, nobody knows whose turn it was");
    }
}
