using Microsoft.Extensions.Time.Testing;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Live;

namespace PoFightJudge.Unit.Fight;

/// <summary>
/// The scripted host that stands in when there is no key. It has to run a whole fight on its own, in the order the
/// rules accept, or the offline path is not really end to end.
/// </summary>
public class FakeLiveClientTests
{
    private static readonly byte[] Frame = new byte[1_600];

    private static async Task<FakeLiveClient> ConnectedAsync()
    {
        var client = new FakeLiveClient(new FakeTimeProvider());
        await client.ConnectAsync(new LiveSessionConfig("host the fight"), CancellationToken.None);
        return client;
    }

    private static async Task<List<LiveServerEvent>> DrainAsync(FakeLiveClient client)
    {
        var seen = new List<LiveServerEvent>();
        while (client.Events.TryRead(out var received))
        {
            seen.Add(received);
        }

        await Task.CompletedTask;
        return seen;
    }

    private static async Task SpeakAsync(FakeLiveClient client, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            await client.SendAudioAsync(Frame, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Connecting_is_acknowledged_so_the_client_does_not_time_out_waiting()
    {
        await using var client = await ConnectedAsync();

        var events = await DrainAsync(client);

        events.Should().ContainSingle().Which.Should().BeOfType<LiveServerEvent.SetupComplete>();
    }

    [Fact]
    public async Task Nothing_happens_until_somebody_speaks_into_it()
    {
        await using var client = await ConnectedAsync();
        await DrainAsync(client);

        await SpeakAsync(client, FakeLiveClient.FramesPerBeat - 1);

        (await DrainAsync(client)).Should().BeEmpty("the show is driven by the microphone, not by a timer");
        client.Beat.Should().Be(0);
    }

    [Fact]
    public async Task Each_beat_calls_its_tool_before_it_speaks_and_then_ends_its_turn()
    {
        await using var client = await ConnectedAsync();
        await DrainAsync(client);

        await SpeakAsync(client, FakeLiveClient.FramesPerBeat);

        var events = await DrainAsync(client);
        events[0].Should().BeOfType<LiveServerEvent.ToolCall>().Which.Name.Should().Be(ToolDeclarations.SetPlayers);
        events[1].Should().BeOfType<LiveServerEvent.OutputTranscript>().Which.Text.Should().Contain("rehearsal");
        events[2].Should().BeOfType<LiveServerEvent.AudioOut>().Which.Pcm24k.Length.Should().BeGreaterThan(0);
        events[3].Should().BeOfType<LiveServerEvent.TurnComplete>();
    }

    [Fact]
    public async Task A_whole_fight_plays_out_in_the_order_the_rules_accept()
    {
        await using var client = await ConnectedAsync();
        await DrainAsync(client);

        await SpeakAsync(client, FakeLiveClient.FramesPerBeat * 10);

        var calls = (await DrainAsync(client)).OfType<LiveServerEvent.ToolCall>().Select(c => c.Name).ToList();
        calls.Should().Equal([
            ToolDeclarations.SetPlayers,
            ToolDeclarations.StartTurn,
            ToolDeclarations.StartTurn,
            ToolDeclarations.EndDebate,
            ToolDeclarations.AskProbe,
            ToolDeclarations.AskProbe,
            ToolDeclarations.DeliverVerdict,
        ]);
    }

    [Fact]
    public async Task The_ruling_it_gives_is_one_the_rules_can_read()
    {
        await using var client = await ConnectedAsync();
        await SpeakAsync(client, FakeLiveClient.FramesPerBeat * 10);

        var verdict = (await DrainAsync(client)).OfType<LiveServerEvent.ToolCall>()
            .Single(c => string.Equals(c.Name, ToolDeclarations.DeliverVerdict, StringComparison.Ordinal));

        verdict.Args.GetProperty("overall").GetString().Should().BeOneOf("player1", "player2");
        verdict.Args.GetProperty("reasons").GetArrayLength().Should().Be(3, "three reasons is the format the tool declares");
    }

    [Fact]
    public async Task The_show_stops_when_the_script_runs_out_rather_than_looping()
    {
        await using var client = await ConnectedAsync();
        await SpeakAsync(client, FakeLiveClient.FramesPerBeat * 20);
        await DrainAsync(client);

        var beatAtEnd = client.Beat;
        await SpeakAsync(client, FakeLiveClient.FramesPerBeat * 5);

        (await DrainAsync(client)).Should().BeEmpty();
        client.Beat.Should().Be(beatAtEnd);
    }

    [Fact]
    public async Task Being_told_time_is_up_moves_the_show_on_the_way_a_real_host_would()
    {
        await using var client = await ConnectedAsync();
        await DrainAsync(client);

        await client.SendTextAsync("SYSTEM: Time is up. Stop the argument immediately (call end_debate).", CancellationToken.None);

        (await DrainAsync(client)).Should().Contain(e => e is LiveServerEvent.ToolCall, "a producer's instruction is obeyed, not ignored");
    }

    [Fact]
    public async Task Closing_completes_the_stream_so_the_orchestrator_stops_reading()
    {
        await using var client = await ConnectedAsync();
        await DrainAsync(client);

        await client.CloseAsync(CancellationToken.None);

        client.Events.Completion.IsCompleted.Should().BeTrue("a drained and closed channel is finished, which is what stops the event loop");
        await SpeakAsync(client, FakeLiveClient.FramesPerBeat);
        client.Beat.Should().Be(0, "a closed client does not carry on with the show");
    }
}
