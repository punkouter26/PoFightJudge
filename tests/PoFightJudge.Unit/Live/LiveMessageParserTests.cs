using System.Text.Json;
using PoFightJudge.Api.Features.Live;

namespace PoFightJudge.Unit.Live;

/// <summary>
/// The decoder for Live API server messages. It is deliberately tolerant: a message it does not understand yields
/// no events rather than an exception, because a new field on Google's side must not end a live debate.
/// </summary>
/// <summary>
/// The decoder for Live API server messages. It is deliberately tolerant: a message it does not understand yields
/// no events rather than an exception, because a new field on Google's side must not end a live debate.
/// </summary>
public class LiveMessageParserTests
{

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
    public void Nonsense_and_novelty_are_both_survivable()
    {
        // Neither a broken frame nor a field Google adds tomorrow may end a debate.
        LiveMessageParser.Parse("not json at all").Should().BeEmpty();
        LiveMessageParser.Parse("[]").Should().BeEmpty();
        LiveMessageParser.Parse("""{"somethingNew":{"nested":true}}""").Should().BeEmpty();
        LiveMessageParser.Parse("""{"setupComplete":{},"somethingNew":1}""").Should().ContainSingle();
    }
}
