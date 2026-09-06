using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Api.Features.Watch;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Watch;

public class AttitudeSelectorTests
{
    private static Profile Persona(
        int patience = 50,
        StressResponse? stress = null,
        int holdsGrudges = 50,
        bool stubborn = false,
        bool sarcastic = false,
        bool introvert = false,
        int logicVsEmotion = 50) => new CreateProfileRequest
        {
            Initials = "TST",
            Role = ProfileRole.Wife,
            Likes = "a",
            Dislikes = "b",
            Patience = patience,
            StressResponse = stress,
            HoldsGrudges = holdsGrudges,
            IsStubborn = stubborn,
            IsSarcastic = sarcastic,
            IsIntrovert = introvert,
            LogicVsEmotion = logicVsEmotion,
        }.ToDomain();

    [Fact]
    public void A_slap_overrides_every_personality_term()
    {
        var saint = Persona(patience: 100, stress: StressResponse.Fawn);

        AttitudeSelector.Select(saint, lastOpponentMood: "calm", roundNumber: 1, wasSlapped: true).Should().Be("furious");
    }

    [Fact]
    public void Patience_is_the_spine_so_two_personas_open_the_same_match_differently()
    {
        var saint = Persona(patience: 95, stress: StressResponse.Fawn);
        var hothead = Persona(patience: 5, stress: StressResponse.Fight);

        var saintly = AttitudeSelector.Select(saint, null, 1, false);
        var heated = AttitudeSelector.Select(hothead, null, 1, false);

        saintly.Should().BeOneOf("calm", "apologetic", "humble");
        heated.Should().BeOneOf("furious", "hostile", "hateful", "angry");
        saintly.Should().NotBe(heated, "this is the whole reason the selector exists — a hard-coded mood made the sliders mute");
    }

    [Fact]
    public void An_argument_escalates_by_round_and_at_the_opponent()
    {
        var persona = Persona(patience: 60);

        var opener = AttitudeSelector.Heat(persona, null, 1);
        var later = AttitudeSelector.Heat(persona, null, 3);
        var provoked = AttitudeSelector.Heat(persona, "furious", 1);
        var soothed = AttitudeSelector.Heat(persona, "apologetic", 1);

        later.Should().BeGreaterThan(opener);
        provoked.Should().BeGreaterThan(opener);
        soothed.Should().BeLessThan(opener);
        AttitudeSelector.Heat(persona, null, 1).Should().BeInRange(0, 100);
        AttitudeSelector.Heat(Persona(patience: 0, stress: StressResponse.Fight, holdsGrudges: 100, stubborn: true, logicVsEmotion: 100), "furious", 3).Should().Be(100, "the heat is clamped");
    }

    [Fact]
    public void Style_traits_only_show_in_the_bands_where_a_person_is_still_in_control()
    {
        var sarcastic = Persona(patience: 45, sarcastic: true);
        var plain = Persona(patience: 45);
        var shouting = Persona(patience: 0, stress: StressResponse.Fight, sarcastic: true);

        AttitudeSelector.Select(sarcastic, null, 2, false).Should().Be("sarcastic");
        AttitudeSelector.Select(plain, null, 2, false).Should().Be("angry");
        AttitudeSelector.Select(shouting, "furious", 3, false).Should().Be("furious", "past the top band everyone is simply shouting");
    }

    [Fact]
    public void The_quiet_ones_withdraw_where_the_loud_ones_get_smug()
    {
        // Each lands on heat 35 — the band where a person is annoyed but still in control. Freeze subtracts 10, so
        // the stonewaller needs less patience than the other two to arrive at the same temperature.
        var stonewaller = Persona(patience: 55, stress: StressResponse.Freeze);
        var introvert = Persona(patience: 65, introvert: true);
        var extrovert = Persona(patience: 65);

        AttitudeSelector.Heat(stonewaller, null, 1).Should().Be(35);
        AttitudeSelector.Select(stonewaller, null, 1, false).Should().Be("passive-aggressive");
        AttitudeSelector.Select(introvert, null, 1, false).Should().Be("passive-aggressive");
        AttitudeSelector.Select(extrovert, null, 1, false).Should().Be("smug");
    }

    [Fact]
    public void The_same_matchup_picks_the_same_attitudes_twice_so_a_prefetched_round_stays_valid()
    {
        var persona = Persona(patience: 40, holdsGrudges: 80);

        var first = AttitudeSelector.Select(persona, "angry", 2, false);
        var second = AttitudeSelector.Select(persona, "angry", 2, false);

        first.Should().Be(second);
        WatchRules.Moods.Should().Contain(first, "an attitude has to be a mood the rest of the app understands");
    }
}
