using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Watch;

/// <summary>
/// Pins the argument prompt's structure, as opposed to its wording. Roughly 1,200 tokens — both profile blocks and
/// the whole rule set — repeat on every turn, and a provider only bills that once if the repeated part is a genuine
/// shared prefix. An earlier layout led with a speaker/opponent pair that swapped every turn, so consecutive calls
/// diverged in their first hundred characters and none of the boilerplate could be cached. The failure is invisible:
/// the prompt reads correctly, the model answers correctly, and the only symptom is the bill.
/// </summary>
public class RoundPromptBuilderTests
{
    private static Profile Husband() => Profile.Create("HUS", ProfileRole.Husband, "vintage synthesizers", "wet towels on the bed", "who lost the good scissors", "measure twice, cut once");

    private static Profile Wife() => Profile.Create("WIF", ProfileRole.Wife, "competitive orchid growing", "unlabelled leftovers", "the thermostat", "if it needs a manual it is badly made");

    private static GeminiPrompt Prompt(string speaker, IReadOnlyList<RoundContext>? history = null, string attitude = "frustrated", string? topic = null, Profile? wife = null) =>
        RoundPromptBuilder.Build(Husband(), wife ?? Wife(), history ?? [], speaker, attitude, wasSlapped: false, interjection: null, topic: topic);

    private static int CommonPrefixLength(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
        {
            i++;
        }

        return i;
    }

    [Fact]
    public void The_user_half_is_identical_up_to_current_state_for_both_speakers()
    {
        var husbandTurn = Prompt(WatchRound.Husband).User;
        var wifeTurn = Prompt(WatchRound.Wife).User;

        var shared = CommonPrefixLength(husbandTurn, wifeTurn);
        var divergesAt = husbandTurn.IndexOf("CURRENT STATE:", StringComparison.Ordinal);

        divergesAt.Should().BeGreaterThan(0, "CURRENT STATE is where per-turn data belongs");
        shared.Should().BeGreaterThanOrEqualTo(divergesAt,
            "everything before CURRENT STATE must be byte-identical between the two speakers, or the profile boilerplate cannot be served from the provider's prompt cache");
    }

    [Fact]
    public void Participants_render_husband_first_whoever_is_speaking()
    {
        foreach (var speaker in (string[])[WatchRound.Husband, WatchRound.Wife])
        {
            var user = Prompt(speaker).User;
            var husbandAt = user.IndexOf("HUSBAND:", StringComparison.Ordinal);
            var wifeAt = user.IndexOf("WIFE:", StringComparison.Ordinal);

            husbandAt.Should().BeGreaterThan(-1);
            wifeAt.Should().BeGreaterThan(husbandAt, $"the husband block leads regardless of who speaks (speaker={speaker})");
        }
    }

    [Fact]
    public void The_speaker_is_named_in_current_state_never_at_the_top()
    {
        var user = Prompt(WatchRound.Wife).User;

        var currentStateAt = user.IndexOf("CURRENT STATE:", StringComparison.Ordinal);
        var speakerCueAt = user.IndexOf("YOU ARE SPEAKING AS:", StringComparison.Ordinal);

        speakerCueAt.Should().BeGreaterThan(currentStateAt, "naming the speaker before the profiles is exactly what used to break the shared prefix");
        user.Should().Contain("YOU ARE SPEAKING AS: the wife");
    }

    [Fact]
    public void The_topic_sits_with_the_stable_block_and_the_attitude_with_the_turn()
    {
        var user = Prompt(WatchRound.Husband, topic: "the thermostat", attitude: "smug").User;

        var topicAt = user.IndexOf("THE FIGHT IS ABOUT:", StringComparison.Ordinal);
        var currentStateAt = user.IndexOf("CURRENT STATE:", StringComparison.Ordinal);

        topicAt.Should().BeGreaterThan(0).And.BeLessThan(currentStateAt, "the topic is constant for the match, so it belongs in the cacheable half");
        user.Should().Contain("Attitude injected this turn: smug");
        Prompt(WatchRound.Husband).User.Should().NotContain("THE FIGHT IS ABOUT:", "no topic, no block");
    }

    [Fact]
    public void The_word_cap_and_the_mood_vocabulary_are_stated_in_the_output_contract()
    {
        var system = Prompt(WatchRound.Husband).System;

        system.Should().Contain($"{RoundPromptBuilder.MaxLineWords} WORDS",
            "asked only for sentences the model returned 66-97 words a line, which is 22-26 seconds of speech each");
        foreach (var mood in WatchRules.Moods)
        {
            system.Should().Contain(mood);
        }
    }

    [Fact]
    public void The_human_opponents_block_says_the_profile_is_blank_on_purpose()
    {
        var user = Prompt(WatchRound.Husband, wife: Profile.CreateHuman(ProfileRole.Wife, "KD")).User;

        user.Should().Contain("REAL PERSON").And.Contain("intentionally empty",
            "a blank profile must read as 'you know nothing', not as 'invent something'");
        user.Should().Contain("Attack ONLY what they actually said",
            "the transcript is the only thing the model legitimately knows about the player");
        Prompt(WatchRound.Husband).User.Should().NotContain("REAL PERSON", "two personas need no such directive");
    }

    [Fact]
    public void The_line_rules_switch_with_the_opponent_and_a_slap_is_announced()
    {
        Prompt(WatchRound.Husband).System.Should().Contain("slider MUST visibly affect the line");
        Prompt(WatchRound.Husband, wife: Profile.CreateHuman(ProfileRole.Wife, "KD")).System.Should().Contain("ANSWER THEIR LAST LINE");

        var slapped = RoundPromptBuilder.Build(Husband(), Wife(), [], WatchRound.Wife, "furious", wasSlapped: true).User;
        slapped.Should().Contain("SLAPPED");

        var heckled = RoundPromptBuilder.Build(Husband(), Wife(), [], WatchRound.Wife, "furious", wasSlapped: false, interjection: Interjections.SlapKey).User;
        heckled.Should().Contain("SLAPPED", "an interjection key carries the same directive");
    }

    [Fact]
    public void The_history_reads_as_a_transcript_and_an_empty_one_says_so()
    {
        Prompt(WatchRound.Husband).User.Should().Contain("This is the opening statement.");

        var user = Prompt(WatchRound.Wife, history: [new RoundContext(WatchRound.Husband, "You left the freezer open.", "angry")]).User;

        user.Should().Contain("[HUSBAND]: You left the freezer open.");
    }

    [Fact]
    public void Sliders_render_in_the_direction_the_interface_draws_them()
    {
        RoundPromptBuilder.SliderLabel("logic", 90).Should().Be("very emotional", "the bar puts Logic left and Emotion right, and an inverted table once called a fully emotional persona coldly logical");
        RoundPromptBuilder.SliderLabel("logic", 10).Should().Be("coldly logical");
        RoundPromptBuilder.SliderLabel("patience", 95).Should().Be("saintly patience");
        RoundPromptBuilder.SliderLabel("patience", 5).Should().Be("hair-trigger temper");
        RoundPromptBuilder.SliderLabel("unknown-axis", 50).Should().Be("neutral");
        RoundPromptBuilder.FormatTraits(Husband()).Should().Be("—", "no traits set reads as an em dash, not an empty line");
        RoundPromptBuilder.OrDash("  ").Should().Be("—");
    }
}

public class JudgePromptBuilderTests
{
    private static Profile Husband() => Profile.Create("HUS", ProfileRole.Husband, "synths", "wet towels", "the scissors", "measure twice");

    private static Profile Wife() => Profile.Create("WIF", ProfileRole.Wife, "orchids", "leftovers", "the thermostat", "keep it simple");

    [Fact]
    public void The_judge_rules_on_the_merits_and_never_on_persona_loyalty()
    {
        var prompt = JudgePromptBuilder.Build(Husband(), Wife(), [new RoundContext(WatchRound.Husband, "You never label anything.", "angry")]);

        prompt.System.Should().Contain("ON THE LOGICAL MERITS")
            .And.Contain("persona consistency are explicitly NOT criteria",
                "a judge that rewarded persona loyalty once declared a winner for staying true to his profile rather than for his argument");
        prompt.System.Should().ContainEquivalentOf("tiebreaker", "an even argument still has to resolve");
        prompt.System.Should().Contain("Logical validity").And.Contain("Rebuttal quality");
    }

    [Fact]
    public void The_transcript_and_a_persona_digest_are_the_only_per_match_content()
    {
        var rounds = new List<RoundContext>
        {
            new(WatchRound.Husband, "You never label anything.", "angry"),
            new(WatchRound.Wife, "You never look before you throw it out.", "hostile"),
        };

        var prompt = JudgePromptBuilder.Build(Husband(), Wife(), rounds);

        prompt.User.Should().Contain("[HUSBAND]: You never label anything.").And.Contain("[WIFE]: You never look before you throw it out.");
        prompt.User.Should().Contain("Husband HUS").And.Contain("Wife WIF");
        prompt.System.Should().Be(JudgePromptBuilder.Build(Wife(), Husband(), []).System, "the criteria are identical for every match ever judged, so they stay in the cacheable half");
    }

    [Fact]
    public void The_persona_digest_names_who_the_spouse_is_without_the_whole_profile_block()
    {
        var digest = JudgePromptBuilder.Persona(Wife());

        digest.Should().Contain("WIF").And.Contain("keep it simple");
        digest.Should().NotContain("Personality sliders", "the digest is a line, not the block");
    }
}
