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
    public void The_host_may_not_rename_the_people_whose_records_this_goes_on()
    {
        var session = NewSession(Pair);

        session.SetPlayers("the dishwasher", "Steve", "Karen", At(5));

        session.Player1Name.Should().Be("AB", "the tags were entered before the show and key the fighter tables");
        session.Player2Name.Should().Be("CD");
        session.Topic.Should().Be("the thermostat", "they agreed the topic before the microphone went on; a restatement is not a rewrite");
    }

    [Fact]
    public void With_nothing_agreed_up_front_the_host_settles_the_topic_out_loud()
    {
        var session = NewSession();

        session.SetPlayers("who does the dishes", "Alex", "Sam", At(5));

        session.Topic.Should().Be("who does the dishes");
    }

    [Fact]
    public void Without_tags_up_front_the_host_supplies_the_names_it_was_given()
    {
        var session = NewSession();

        session.SetPlayers("who does the dishes", "Alex", "Sam", At(5));

        session.Player1Name.Should().Be("Alex");
        session.Player2Name.Should().Be("Sam");
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
    public void Probing_before_the_debate_was_closed_closes_it_rather_than_failing()
    {
        var session = Debating();

        var probe = session.AskProbe(PlayerId.Player1, "Where is the evidence?", At(60));

        probe.IsSuccess.Should().BeTrue("a host that skips a step must not be left stuck in the wrong phase");
        session.Phase.Should().Be(SessionPhase.Probe);
    }

    /// <summary>
    /// A real fight on 2026-09-07 sat in Setup for eight minutes: the host called set_players, never called
    /// start_turn, then asked to end the argument eleven times and was refused eleven times while the two of them
    /// talked over it. The phases exist to shape a show, not to trap one.
    /// </summary>
    [Fact]
    public void Ending_an_argument_the_host_never_formally_started_moves_the_show_on_rather_than_refusing()
    {
        var session = NewSession(Pair);
        session.SetPlayers("the thermostat", "AB", "CD", At(5));
        session.Phase.Should().Be(SessionPhase.Setup);

        var ended = session.EndDebate(At(90));

        ended.IsSuccess.Should().BeTrue("a host asking to end has plainly decided the argument is over");
        session.Phase.Should().Be(SessionPhase.Probe);
        session.Turns.Should().BeEmpty("nobody was given the floor, so nobody gets a turn on the record");
    }

    [Fact]
    public void A_question_asked_before_the_debate_was_ever_started_lands_in_the_probe()
    {
        var session = NewSession(Pair);
        session.SetPlayers("the thermostat", "AB", "CD", At(5));

        var probe = session.AskProbe(PlayerId.Player1, "Where is the evidence?", At(90));

        probe.IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(SessionPhase.Probe);
    }

    [Fact]
    public void A_host_that_names_somebody_by_their_tag_is_understood()
    {
        var session = NewSession(Pair);
        session.SetPlayers("the thermostat", "AB", "CD", At(5));

        session.TryResolvePlayer("player2", out var byPosition).Should().BeTrue();
        byPosition.Should().Be(PlayerId.Player2);

        session.TryResolvePlayer("cd", out var byTag).Should().BeTrue("the host has been calling them CD all night");
        byTag.Should().Be(PlayerId.Player2);

        session.TryResolvePlayer("the loud one", out _).Should().BeFalse("a guess would put words in the wrong mouth");
    }

    /// <summary>
    /// A real fight on 2026-09-07 asked its first question during Intro and was refused, and the show never left
    /// that phase. Both fighters were named in the request that started it, so Intro was waiting for the host to
    /// make introductions, not to learn anything.
    /// </summary>
    [Fact]
    public void A_question_asked_before_the_introductions_carries_the_show_past_them()
    {
        var session = NewSession(Pair);
        session.Phase.Should().Be(SessionPhase.Intro);

        var probe = session.AskProbe(PlayerId.Player2, "What is your evidence?", At(40));

        probe.IsSuccess.Should().BeTrue();
        session.Phase.Should().Be(SessionPhase.Probe);
    }

    [Fact]
    public void A_show_that_never_learned_who_is_arguing_still_refuses_to_move()
    {
        // No setup: nobody was named up front, so set_players is the only way the show can start.
        var session = NewSession();

        session.EndDebate(At(30)).IsSuccess.Should().BeFalse("who is arguing is genuinely not known yet");
        session.Phase.Should().Be(SessionPhase.Intro);
    }

    /// <summary>
    /// The silence nudges never fire while people are talking, and the debate clocks do not run until the argument
    /// formally starts — so a show whose host forgot to start it had nothing at all telling it to move.
    /// </summary>
    [Fact]
    public void A_host_that_lets_them_argue_without_starting_the_argument_is_told_to_get_on_with_it()
    {
        var session = NewSession(Pair);
        session.SetPlayers("the thermostat", "AB", "CD", At(5));

        // They are arguing over the top of the introductions, so nothing is ever silent.
        for (var second = 6; second < 50; second++)
        {
            session.NoteSpeech(At(second));
            session.Tick(At(second));
        }

        session.NoteSpeech(At(51));
        var nudges = session.Tick(At(51));

        nudges.Should().ContainSingle().Which.Kind.Should().Be(NudgeKind.SetupCap);
        nudges[0].Message.Should().Contain("start_turn").And.Contain("AB");
        session.Tick(At(60)).Should().BeEmpty("it is said once, not every second");
    }

    [Fact]
    public void The_clock_the_room_sees_stops_when_the_arguing_does()
    {
        var session = Debating();

        session.DebateElapsed(At(40)).Should().Be(TimeSpan.FromSeconds(30), "they started at ten seconds");

        session.EndDebate(At(60));

        session.DebateElapsed(At(200)).Should().Be(TimeSpan.FromSeconds(50),
            "the argument ran fifty seconds however long the questions take");
        session.DebateRemaining(At(200)).Should().BeGreaterThan(TimeSpan.Zero,
            "a timer that keeps running counts down to nothing in front of everybody");
    }

    [Fact]
    public void An_argument_nobody_formally_started_is_not_recorded_as_having_taken_no_time()
    {
        var session = NewSession(Pair);
        session.SetPlayers("the thermostat", "AB", "CD", At(5));

        session.EndDebate(At(300));

        session.DebateElapsed(At(300)).Should().Be(TimeSpan.FromSeconds(300),
            "they were arguing for the whole five minutes, whatever the host called it");
    }

    [Fact]
    public void A_second_verdict_is_refused_so_a_ruling_cannot_be_overwritten()
    {
        var session = Debating();
        var first = new VerdictCall(PlayerId.Player1, PlayerId.Player1, PlayerId.Player1, ["a", "b", "c"]);
        session.DeliverVerdict(first, At(100));
        session.Complete(At(110));

        var second = session.DeliverVerdict(new VerdictCall(PlayerId.Player2, PlayerId.Player2, PlayerId.Player2, ["x"]), At(120));

        second.IsSuccess.Should().BeFalse();
        session.Verdict.Should().Be(first);
    }

    [Fact]
    public void Someone_who_holds_the_floor_too_long_is_handed_over_once()
    {
        var session = Debating();

        // Somebody holding the floor is by definition still talking, so their audio keeps resetting the silence clock.
        session.NoteSpeech(At(50));
        session.Tick(At(50)).Should().BeEmpty("the long-talker clock runs from the start of their turn");

        session.NoteSpeech(At(56));
        var nudges = session.Tick(At(56));

        nudges.Should().ContainSingle().Which.Kind.Should().Be(NudgeKind.LongTalker);
        nudges[0].Message.Should().StartWith("SYSTEM:").And.Contain("AB").And.Contain("CD", "the host is told who to hand over to");

        session.NoteSpeech(At(70));
        session.Tick(At(70)).Should().NotContain(n => n.Kind == NudgeKind.LongTalker, "one nudge per turn, not one per tick");

        session.StartTurn(PlayerId.Player2, At(75));
        session.NoteSpeech(At(130));
        session.Tick(At(130)).Should().Contain(n => n.Kind == NudgeKind.LongTalker, "the clock restarts with the new turn");
    }

    [Fact]
    public void Silence_is_prompted_once_and_then_pushed_past()
    {
        var session = Debating();

        session.Tick(At(15)).Should().BeEmpty();
        session.Tick(At(21)).Should().ContainSingle().Which.Kind.Should().Be(NudgeKind.Silence);
        session.Tick(At(25)).Should().BeEmpty("the first prompt stands until the silence deepens");
        session.Tick(At(31)).Should().ContainSingle().Which.Kind.Should().Be(NudgeKind.SilenceAdvance);

        session.NoteSpeech(At(40));
        session.Tick(At(51)).Should().Contain(n => n.Kind == NudgeKind.Silence, "somebody spoke, so the clock starts again");
    }

    [Fact]
    public void The_producer_opens_the_probe_window_and_then_calls_time()
    {
        var session = Debating();

        session.Tick(At(75)).Should().Contain(n => n.Kind == NudgeKind.ProbeWindowOpen);
        session.Tick(At(80)).Should().NotContain(n => n.Kind == NudgeKind.ProbeWindowOpen, "the window opens once");

        var capped = session.Tick(At(200));
        capped.Should().Contain(n => n.Kind == NudgeKind.DebateCap);
        capped.Single(n => n.Kind == NudgeKind.DebateCap).Message.Should().Contain("end_debate");
    }

    [Fact]
    public void Questioning_that_runs_long_is_told_to_rule()
    {
        var session = Debating();
        session.EndDebate(At(70));

        session.Tick(At(150)).Should().NotContain(n => n.Kind == NudgeKind.ProbeCap);
        session.Tick(At(200)).Should().Contain(n => n.Kind == NudgeKind.ProbeCap);
    }

    [Fact]
    public void Once_the_ruling_is_in_the_producer_stops_talking()
    {
        var session = Debating();
        session.DeliverVerdict(new VerdictCall(PlayerId.Player1, PlayerId.Player1, PlayerId.Player1, ["a", "b", "c"]), At(100));

        session.Tick(At(400)).Should().BeEmpty("no nudge can help a show that has already been ruled on");
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

    [Fact]
    public void The_ruling_is_written_into_the_transcript_as_the_hosts_own_turn()
    {
        var session = Debating();

        session.DeliverVerdict(new VerdictCall(PlayerId.Player2, PlayerId.Player1, PlayerId.Player2, ["clearer", "kinder", "right"]), At(120));

        var verdict = session.Turns.Should().ContainSingle(t => t.Kind == TurnKind.Verdict).Subject;
        verdict.Speaker.Should().Be(Speaker.Host);
        verdict.Text.Should().Contain("clearer").And.Contain("right");
    }

    [Fact]
    public void The_snapshot_is_what_the_room_sees_while_it_is_happening()
    {
        var session = Debating();

        var snapshot = session.Snapshot(At(70));

        snapshot.MatchId.Should().Be(Match);
        snapshot.Phase.Should().Be(SessionPhase.Debate);
        snapshot.Topic.Should().Be("the thermostat");
        snapshot.Player1Name.Should().Be("AB");
        snapshot.Speaking.Should().Be(Speaker.Player1);
        snapshot.DebateElapsedSeconds.Should().Be(60, "the debate clock starts when the first turn does, not when the session does");
        snapshot.DebateRemainingSeconds.Should().Be(120);
        snapshot.SessionElapsedSeconds.Should().Be(70);
        snapshot.Persona.Should().Be(HostPersonaId.Referee);
        snapshot.Verdict.Should().BeNull();
    }

    [Fact]
    public void Model_supplied_text_is_trimmed_and_capped_before_it_reaches_storage()
    {
        DebateSession.Clean("  spaced  ", 100, "fallback").Should().Be("spaced");
        DebateSession.Clean("", 100, "fallback").Should().Be("fallback");
        DebateSession.Clean(null, 100, "fallback").Should().Be("fallback");
        DebateSession.Clean(new string('x', 500), 200, "fallback").Should().HaveLength(200);
    }

    [Theory]
    [InlineData("player1", PlayerId.Player1)]
    [InlineData("Player 2", PlayerId.Player2)]
    [InlineData("p1", PlayerId.Player1)]
    [InlineData("2", PlayerId.Player2)]
    [InlineData("two", PlayerId.Player2)]
    public void The_ways_a_model_might_name_a_player_are_all_understood(string spoken, PlayerId expected)
    {
        PlayerIdExtensions.TryParse(spoken, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("")]
    [InlineData(null)]
    public void A_player_that_cannot_be_identified_is_rejected_rather_than_guessed(string? spoken) =>
        PlayerIdExtensions.TryParse(spoken, out _).Should().BeFalse();
}
