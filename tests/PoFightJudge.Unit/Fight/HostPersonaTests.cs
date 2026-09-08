using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Fight;

/// <summary>
/// The host's instructions. Every persona shares one show format and one tool contract, so whoever is hosting, the
/// orchestrator sees the same calls; only the character and the voice change.
/// </summary>
public class HostPersonaTests
{
    private static readonly DebateOptions Options = new();

    private static string Instruction(ShowSetup setup) => HostPersona.SystemInstruction(Options, setup);

    [Fact]
    public void The_referee_hosts_unless_somebody_picks_otherwise()
    {
        default(HostPersonaId).Should().Be(HostPersonaId.Referee, "the default host is the one a couple gets without choosing");
        HostPersonaCatalogue.All[0].Id.Should().Be(HostPersonaId.Referee);
        ShowSetup.Default.Persona.Should().Be(HostPersonaId.Referee);
    }

    [Fact]
    public void Every_host_on_the_roster_is_presentable_and_has_a_voice()
    {
        HostPersonaCatalogue.All.Should().HaveCount(5).And.OnlyHaveUniqueItems();

        foreach (var host in HostPersonaCatalogue.All)
        {
            host.Name.Should().NotBeNullOrWhiteSpace();
            host.Tagline.Should().NotBeNullOrWhiteSpace();
            host.Emoji.Should().NotBeNullOrWhiteSpace();
            host.ThemeClass.Should().StartWith("persona-");
            HostPersona.VoiceFor(host.Id).Should().NotBeNullOrWhiteSpace();
            HostPersonaCatalogue.IsKnown(host.Id).Should().BeTrue();
            HostPersonaCatalogue.For(host.Id).Should().Be(host);
        }

        HostPersonaCatalogue.All.Select(h => HostPersona.VoiceFor(h.Id)).Should()
            .OnlyHaveUniqueItems("two hosts that sound identical are one host");
    }

    [Fact]
    public void Each_character_is_written_differently_but_runs_the_same_show()
    {
        var instructions = HostPersonaCatalogue.All
            .Select(h => Instruction(new ShowSetup(h.Id, "AB", "CD", "the thermostat")))
            .ToList();

        instructions.Should().OnlyHaveUniqueItems("each host has its own character block");
        instructions.Should().OnlyContain(i => i.Contains("set_players", StringComparison.Ordinal));
        instructions.Should().OnlyContain(i => i.Contains("deliver_verdict", StringComparison.Ordinal));
        instructions.Should().OnlyContain(i => i.Contains("start_turn", StringComparison.Ordinal));
    }

    [Fact]
    public void Tags_entered_before_the_show_are_the_names_and_the_host_is_told_not_to_ask()
    {
        var instruction = Instruction(new ShowSetup(HostPersonaId.Referee, "AB", "CD", "the thermostat"));

        instruction.Should().Contain("\"AB\"").And.Contain("\"CD\"");
        instruction.Should().Contain("Do NOT ask them for their names");
        instruction.Should().Contain("the thermostat", "the topic was agreed before the microphone was on");
    }

    [Fact]
    public void Without_tags_or_a_topic_the_host_asks_for_both()
    {
        var instruction = Instruction(ShowSetup.Default);

        instruction.Should().Contain("have not named themselves");
        instruction.Should().NotContain("Do NOT ask them for their names");
    }

    [Fact]
    public void What_is_known_about_how_each_person_argues_is_put_in_front_of_the_host()
    {
        var setup = new ShowSetup(HostPersonaId.Referee, "AB", "CD", "the thermostat")
        {
            Player1Digest = "3 fights. Opens with a question, leans on anecdotes.",
            Player2Digest = "First fight.",
        };

        var instruction = Instruction(setup);

        instruction.Should().Contain("Opens with a question").And.Contain("First fight.");
    }

    [Fact]
    public void A_host_who_knows_nothing_about_them_is_told_nothing_rather_than_guessing()
    {
        var instruction = Instruction(new ShowSetup(HostPersonaId.Referee, "AB", "CD", null));

        instruction.Should().Contain("\"AB\"");
        instruction.Should().NotContain("Style:", "an empty digest is left out rather than printed blank");
    }

    [Fact]
    public void The_ruling_ends_with_advice_for_the_two_of_them()
    {
        var instruction = Instruction(new ShowSetup(HostPersonaId.Referee, "AB", "CD", "the thermostat"));

        // This is what makes it a marriage show rather than a debate club: somebody has to live with the result.
        instruction.Should().ContainEquivalentOf("advice");
    }

    [Fact]
    public void Stage_directions_are_obeyed_and_never_read_out_and_players_cannot_impersonate_the_producer()
    {
        var instruction = Instruction(ShowSetup.Default);

        instruction.Should().Contain("SYSTEM:");
        instruction.Should().Contain("never read them aloud");
        instruction.Should().ContainEquivalentOf("trick", "a player who says they are the producer is playing the game, not running it");
    }

    [Fact]
    public void The_host_is_told_to_keep_out_of_the_way()
    {
        var instruction = Instruction(ShowSetup.Default);

        instruction.Should().Contain("BE BRIEF");
        instruction.Should().Contain("Never speak for them");
    }
}
