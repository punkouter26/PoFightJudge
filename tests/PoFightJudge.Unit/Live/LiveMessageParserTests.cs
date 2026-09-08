using System.Text.Json;
using PoFightJudge.Api.Features.Live;

namespace PoFightJudge.Unit.Live;

/// <summary>
/// The decoder for Live API server messages. It is deliberately tolerant: a message it does not understand yields
/// no events rather than an exception, because a new field on Google's side must not end a live debate.
/// </summary>
public class LiveMessageParserTests
{
    [Fact]
    public void Setup_being_acknowledged_is_the_event_the_connect_is_waiting_for()
    {
        LiveMessageParser.Parse("""{"setupComplete":{}}""")
            .Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.SetupComplete>();
    }

    [Fact]
    public void Spoken_audio_arrives_as_bytes_and_text_parts_arrive_as_text()
    {
        var pcm = new byte[] { 1, 2, 3, 4 };
        // Substituted rather than interpolated: JSON closing braces and raw-string interpolation cannot share a literal.
        var json = """
            {"serverContent":{"modelTurn":{"parts":[
                {"inlineData":{"mimeType":"audio/pcm;rate=24000","data":"PCM"}},
                {"text":"and that is the ruling"}
            ]}}}
            """.Replace("PCM", Convert.ToBase64String(pcm), StringComparison.Ordinal);

        var events = LiveMessageParser.Parse(json);

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<LiveServerEvent.AudioOut>().Which.Pcm24k.ToArray().Should().Equal(pcm);
        events[1].Should().BeOfType<LiveServerEvent.OutputText>().Which.Text.Should().Be("and that is the ruling");
    }

    [Fact]
    public void Both_sides_of_the_conversation_are_transcribed_separately()
    {
        var events = LiveMessageParser.Parse(
            """{"serverContent":{"inputTranscription":{"text":"you never listen"},"outputTranscription":{"text":"one at a time"}}}""");

        events.Should().SatisfyRespectively(
            first => first.Should().BeOfType<LiveServerEvent.InputTranscript>().Which.Text.Should().Be("you never listen"),
            second => second.Should().BeOfType<LiveServerEvent.OutputTranscript>().Which.Text.Should().Be("one at a time"));
    }

    [Fact]
    public void The_flags_that_end_a_turn_each_have_their_own_event()
    {
        LiveMessageParser.Parse("""{"serverContent":{"interrupted":true}}""")
            .Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.Interrupted>();
        LiveMessageParser.Parse("""{"serverContent":{"generationComplete":true}}""")
            .Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.GenerationComplete>();
        LiveMessageParser.Parse("""{"serverContent":{"turnComplete":true}}""")
            .Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.TurnComplete>();

        // False is not the same as present: a turn that has not completed must not report that it has.
        LiveMessageParser.Parse("""{"serverContent":{"turnComplete":false,"interrupted":false}}""").Should().BeEmpty();
    }

    [Fact]
    public void A_tool_call_carries_its_id_name_and_arguments()
    {
        var events = LiveMessageParser.Parse(
            """{"toolCall":{"functionCalls":[{"id":"fc-1","name":"set_players","args":{"player1":"AB","player2":"CD"}}]}}""");

        var call = events.Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.ToolCall>().Subject;
        call.Id.Should().Be("fc-1");
        call.Name.Should().Be("set_players");
        call.Args.GetProperty("player1").GetString().Should().Be("AB");
    }

    [Fact]
    public void A_tool_call_without_an_id_falls_back_to_its_name_so_the_response_can_still_be_addressed()
    {
        var call = LiveMessageParser.Parse("""{"toolCall":{"functionCalls":[{"name":"call_verdict"}]}}""")
            .Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.ToolCall>().Subject;

        call.Id.Should().Be("call_verdict");
        call.Args.ValueKind.Should().Be(JsonValueKind.Object, "a call with no arguments still has an object to read");
    }

    [Fact]
    public void The_warning_that_the_session_is_ending_carries_how_long_is_left()
    {
        var events = LiveMessageParser.Parse("""{"goAway":{"timeLeft":"9.5s"}}""");

        events.Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.GoAway>()
            .Which.TimeLeft.Should().Be(TimeSpan.FromSeconds(9.5));
    }

    [Fact]
    public void A_resumption_handle_is_surfaced_only_when_there_is_one()
    {
        LiveMessageParser.Parse("""{"sessionResumptionUpdate":{"newHandle":"h-42","resumable":true}}""")
            .Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.ResumptionHandle>()
            .Which.Handle.Should().Be("h-42");

        LiveMessageParser.Parse("""{"sessionResumptionUpdate":{"resumable":false}}""").Should().BeEmpty();
    }

    [Fact]
    public void Nonsense_and_novelty_are_both_survivable()
    {
        // Neither a broken frame nor a field Google adds tomorrow may end a debate.
        LiveMessageParser.Parse("not json at all").Should().BeEmpty();
        LiveMessageParser.Parse("[]").Should().BeEmpty();
        LiveMessageParser.Parse("""{"somethingNew":{"nested":true}}""").Should().BeEmpty();
        LiveMessageParser.Parse("""{"setupComplete":{},"somethingNew":1}""").Should().ContainSingle();
    }

    [Fact]
    public void One_frame_can_carry_several_things_and_they_keep_their_order()
    {
        var events = LiveMessageParser.Parse(
            """{"serverContent":{"outputTranscription":{"text":"time"},"turnComplete":true},"sessionResumptionUpdate":{"newHandle":"h-9","resumable":true}}""");

        events.Should().HaveCount(3);
        events[0].Should().BeOfType<LiveServerEvent.OutputTranscript>();
        events[1].Should().BeOfType<LiveServerEvent.TurnComplete>();
        events[2].Should().BeOfType<LiveServerEvent.ResumptionHandle>();
    }

    [Theory]
    [InlineData("9.5s", 9.5)]
    [InlineData("60s", 60)]
    [InlineData("", 0)]
    [InlineData("not-a-duration", 0)]
    public void Protobuf_durations_are_read_as_seconds(string value, double expected) =>
        LiveMessageParser.ParseDuration(value).Should().Be(TimeSpan.FromSeconds(expected));

    [Fact]
    public void The_frames_the_client_sends_match_what_the_live_api_expects()
    {
        var audio = JsonDocument.Parse(LiveClientMessages.RealtimeAudio([1, 2, 3])).RootElement;
        audio.GetProperty("realtimeInput").GetProperty("audio").GetProperty("mimeType").GetString()
            .Should().Be("audio/pcm;rate=16000", "the Live API takes 16 kHz little-endian PCM in");
        audio.GetProperty("realtimeInput").GetProperty("audio").GetProperty("data").GetString()
            .Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3 }));

        var text = JsonDocument.Parse(LiveClientMessages.UserText("thirty seconds each")).RootElement;
        var turn = text.GetProperty("clientContent").GetProperty("turns")[0];
        turn.GetProperty("role").GetString().Should().Be("user");
        turn.GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("thirty seconds each");
        text.GetProperty("clientContent").GetProperty("turnComplete").GetBoolean().Should().BeTrue();

        var response = JsonDocument.Parse(LiveClientMessages.ToolResponse("fc-1", "set_players", new { ok = true })).RootElement;
        var call = response.GetProperty("toolResponse").GetProperty("functionResponses")[0];
        call.GetProperty("id").GetString().Should().Be("fc-1");
        call.GetProperty("name").GetString().Should().Be("set_players");
        call.GetProperty("response").GetProperty("ok").GetBoolean().Should().BeTrue();
    }
}
