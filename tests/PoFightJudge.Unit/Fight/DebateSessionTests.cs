using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fight;

/// <summary>
/// The debate's rules, with no clock and no I/O of its own: every timing decision is a function of the timestamps
/// it is handed, so the whole show is reproducible from a list of moments.
/// </summary>
public class DebateSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 19, 0, 0, TimeSpan.Zero);
    private static readonly MatchId Match = MatchId.New();

    private static readonly DebateOptions Options = new()
    {
        MaxDebateSeconds = 180,
        LongTalkerSeconds = 45,
        SilenceSeconds = 10,
        ProbeWindowStartSeconds = 60,
        MaxProbeSeconds = 120,
    };

    private static readonly ShowSetup Pair = new(HostPersonaId.Referee, "AB", "CD", "the thermostat");

    private static DateTimeOffset At(double seconds) => Start.AddSeconds(seconds);

    private static DebateSession NewSession(ShowSetup? setup = null) => new(Options, Match, Start, setup);

    /// <summary>Drives a session to the point where both players are debating.</summary>
    private static DebateSession Debating()
    {
        var session = NewSession(Pair);
        session.SetPlayers("the thermostat", "AB", "CD", At(5));
        session.StartTurn(PlayerId.Player1, At(10));
        return session;
    }

    [Fact]
    public void A_show_walks_from_the_introduction_through_to_the_ruling()
    {
        var session = NewSession(Pair);
        session.Phase.Should().Be(SessionPhase.Intro);

        session.SetPlayers("the thermostat", "AB", "CD", At(5)).IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(SessionPhase.Setup);

        session.StartTurn(PlayerId.Player1, At(10)).IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(SessionPhase.Debate);
        session.CurrentSpeaker.Should().Be(PlayerId.Player1);

        session.EndDebate(At(70)).IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(SessionPhase.Probe);
        session.CurrentSpeaker.Should().BeNull("nobody holds the floor between the debate and the questions");

        session.AskProbe(PlayerId.Player2, "Where did that number come from?", At(75)).IsSuccess.Should().BeTrue();
        session.DeliverVerdict(new VerdictCall(PlayerId.Player1, PlayerId.Player2, PlayerId.Player1, ["clearer", "evidence", "stayed on topic"]), At(120))
            .IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(SessionPhase.Verdict);

        session.Complete(At(140));
        session.Phase.Should().Be(SessionPhase.Done);
    }

    [Fact]
    public void Steps_taken_out_of_order_are_refused_by_name_rather_than_half_applied()
    {
        var session = NewSession(Pair);

        // Skipping forward is allowed — a host that has moved on has moved on — but going back is not: the debate
        // is over once the questions start, and a refused step must leave the show exactly where it was.
        session.SetPlayers("t", "AB", "CD", At(5));
        session.StartTurn(PlayerId.Player1, At(10));
        session.EndDebate(At(60));

        session.StartTurn(PlayerId.Player2, At(65)).Error.Should().Contain("Probe");
        session.Phase.Should().Be(SessionPhase.Probe, "a refused step changes nothing");

        session.SetPlayers("something else", "ZZ", "YY", At(70)).Error.Should().Contain("Probe");
        session.Player1Name.Should().Be("AB", "and nothing it refused was half applied");
    }

    [Fact]
    public void Turns_are_recorded_with_their_own_start_and_end_and_an_interruption_names_who_was_cut_off()
    {
        var session = Debating();
        session.NoteInterrupt(At(55));
        session.StartTurn(PlayerId.Player2, At(56));
        session.EndDebate(At(90));

        var talk = session.Turns.Where(t => t.Kind == TurnKind.Talk).ToList();
        talk.Should().HaveCount(2);
        talk[0].Speaker.Should().Be(Speaker.Player1);
        talk[0].StartSeconds.Should().Be(10);
        talk[0].EndSeconds.Should().Be(56, "a turn ends when the next one starts");
        talk[1].EndSeconds.Should().Be(90, "the last turn ends when the debate does");

        var interrupt = session.Turns.Should().ContainSingle(t => t.Kind == TurnKind.Interrupt).Subject;
        interrupt.Speaker.Should().Be(Speaker.Player1, "the interruption belongs to whoever was cut off");
        interrupt.Text.Should().Contain("AB");
        session.Turns.Should().OnlyContain(t => t.MatchId == Match);
    }
}
