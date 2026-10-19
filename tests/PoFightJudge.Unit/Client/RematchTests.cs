using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// A one-tap rematch is this fight again: the same two tags, the same topic, the same host. The record keeps those;
/// it does not keep the seats or the microphone, which come from the last fight set up on this device — the seats
/// only when that was these two.
/// </summary>
public class RematchTests
{
    private static readonly MatchDto Fought = new(
        MatchId.From("0123456789abcdef0123456789abcdef"), "user", MatchMode.Fight, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        "the thermostat", MatchSide.Human("AL"), MatchSide.Human("SM"), SessionPhase.Done, SessionStatus.Ready, "AL", null, false)
    {
        Persona = nameof(HostPersonaId.Roastmaster),
    };

    [Fact]
    public void It_is_the_same_fight_again()
    {
        var card = Verdict.RematchCard(Fought, last: null);

        card.Should().Be(new FightCard("AL", "SM", "the thermostat", ProfileRole.Husband, ProfileRole.Wife, HostPersonaId.Roastmaster));
    }

    [Fact]
    public void The_seats_and_the_microphone_come_from_the_last_setup_of_these_two()
    {
        var last = new FightCard("sm", "al", "something else", ProfileRole.Wife, ProfileRole.Husband, HostPersonaId.Coach) { Device = "usb-mic" };

        var card = Verdict.RematchCard(Fought, last);

        card.OneRole.Should().Be(ProfileRole.Husband, "AL sat as the husband last time, whichever box the tag was typed in");
        card.TwoRole.Should().Be(ProfileRole.Wife);
        card.Persona.Should().Be(HostPersonaId.Roastmaster, "the host is this fight's, not the last one set up");
        card.Topic.Should().Be("the thermostat");
        card.Device.Should().Be("usb-mic");
    }

    [Fact]
    public void Somebody_elses_seats_are_not_borrowed()
    {
        var last = new FightCard("KD", "SM", "x", ProfileRole.Wife, ProfileRole.Husband, HostPersonaId.Coach) { Device = "usb-mic" };

        var card = Verdict.RematchCard(Fought, last);

        (card.OneRole, card.TwoRole).Should().Be((ProfileRole.Husband, ProfileRole.Wife));
        card.Device.Should().Be("usb-mic", "the microphone belongs to the device, not to the pair");
    }
}
