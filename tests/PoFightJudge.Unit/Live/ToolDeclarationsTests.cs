using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Live;

namespace PoFightJudge.Unit.Live;

/// <summary>
/// The tools the host drives the show with. The argument types are as tight as the schema allows on purpose: a
/// rejected tool call costs a whole model round trip, which the room hears as the host going quiet mid-show.
/// </summary>
public class ToolDeclarationsTests
{
    private static readonly JsonArray Declared = ToolDeclarations.FunctionDeclarations();

    private static JsonObject Tool(string name) =>
        Declared.OfType<JsonObject>().Single(d => string.Equals(d["name"]!.GetValue<string>(), name, StringComparison.Ordinal));

    private static JsonObject Parameter(string tool, string parameter) =>
        (JsonObject)Tool(tool)["parameters"]!["properties"]![parameter]!;

    [Fact]
    public void Every_tool_the_orchestrator_handles_is_declared_and_described()
    {
        Declared.Should().HaveCount(ToolDeclarations.AllNames.Count);

        foreach (var name in ToolDeclarations.AllNames)
        {
            var tool = Tool(name);
            tool["description"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace($"{name} needs to tell the host when to call it");
        }
    }

    [Fact]
    public void Whichever_player_is_named_it_can_only_be_one_of_the_two_in_the_room()
    {
        foreach (var (tool, parameter) in new[] { (ToolDeclarations.StartTurn, "player"), (ToolDeclarations.AskProbe, "player") })
        {
            var values = Parameter(tool, parameter)["enum"]!.AsArray().Select(v => v!.GetValue<string>());
            values.Should().Equal(["player1", "player2"], $"{tool} must not be able to hand the floor to somebody who is not here");
        }
    }

    [Fact]
    public void The_ruling_names_a_winner_on_each_count_and_gives_exactly_three_reasons()
    {
        foreach (var field in new[] { "winner_logic", "winner_correct", "overall" })
        {
            Parameter(ToolDeclarations.DeliverVerdict, field)["enum"]!.AsArray().Should().HaveCount(2);
        }

        var reasons = Parameter(ToolDeclarations.DeliverVerdict, "reasons");
        reasons["type"]!.GetValue<string>().Should().Be("ARRAY");
        reasons["minItems"]!.GetValue<int>().Should().Be(3);
        reasons["maxItems"]!.GetValue<int>().Should().Be(3, "three crisp reasons is the format, not a suggestion");
    }

    [Fact]
    public void Recording_the_players_takes_the_topic_and_both_names()
    {
        var properties = Tool(ToolDeclarations.SetPlayers)["parameters"]!["properties"]!.AsObject();

        properties.Select(p => p.Key).Should().BeEquivalentTo(["topic", "player1", "player2"]);
    }

    [Fact]
    public void Every_argument_a_tool_declares_is_required_so_a_half_filled_call_cannot_arrive()
    {
        foreach (var tool in Declared.OfType<JsonObject>().Where(d => d["parameters"] is not null))
        {
            var parameters = tool["parameters"]!;
            parameters["type"]!.GetValue<string>().Should().Be("OBJECT");
            var declared = parameters["properties"]!.AsObject().Select(p => p.Key);
            var required = parameters["required"]!.AsArray().Select(v => v!.GetValue<string>());
            required.Should().BeEquivalentTo(declared, $"{tool["name"]} would otherwise accept a call with nothing in it");
        }
    }

    [Fact]
    public void Ending_the_debate_takes_no_arguments_at_all()
    {
        Tool(ToolDeclarations.EndDebate).Should().NotContainKey("parameters", "there is nothing to say about stopping");
    }

    [Fact]
    public void The_declarations_are_built_fresh_so_a_setup_frame_cannot_be_edited_from_under_another()
    {
        var first = ToolDeclarations.FunctionDeclarations();
        var second = ToolDeclarations.FunctionDeclarations();

        first.Should().NotBeSameAs(second, "a JSON node belongs to one document, and the setup builder attaches it to one");
        first.ToJsonString().Should().Be(second.ToJsonString());
    }
}
