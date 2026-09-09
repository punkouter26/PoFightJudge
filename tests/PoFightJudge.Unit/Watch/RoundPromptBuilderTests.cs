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
    public void The_human_opponents_block_says_the_profile_is_blank_on_purpose()
    {
        var user = Prompt(WatchRound.Husband, wife: Profile.CreateHuman(ProfileRole.Wife, "KD")).User;

        user.Should().Contain("REAL PERSON").And.Contain("intentionally empty",
            "a blank profile must read as 'you know nothing', not as 'invent something'");
        user.Should().Contain("Attack ONLY what they actually said",
            "the transcript is the only thing the model legitimately knows about the player");
        Prompt(WatchRound.Husband).User.Should().NotContain("REAL PERSON", "two personas need no such directive");
    }
}
