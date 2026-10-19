using PoFightJudge.Client.Stats;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Where an Elo rating sits on the ladder. Everybody starts at 1000 — Silver, the middle of it — so one fight either
/// way leaves them there and it takes a run to move; nobody is ranked until they have argued at all.
/// </summary>
public class RankTierTests
{
    [Theory]
    [InlineData(1000, 0, Tier.Unranked)]
    [InlineData(950, 4, Tier.Bronze)]
    [InlineData(975, 1, Tier.Silver)]
    [InlineData(1016, 1, Tier.Silver)]
    [InlineData(1025, 3, Tier.Gold)]
    [InlineData(1100, 9, Tier.Platinum)]
    [InlineData(1125, 12, Tier.Diamond)]
    public void A_rating_has_a_tier(int rating, int fights, Tier tier) =>
        RankTiers.For(rating, fights).Should().Be(tier);

    [Theory]
    [InlineData(null, Tier.Gold, false)]
    [InlineData(Tier.Silver, Tier.Gold, true)]
    [InlineData(Tier.Unranked, Tier.Silver, true)]
    [InlineData(Tier.Gold, Tier.Gold, false)]
    [InlineData(Tier.Gold, Tier.Silver, false)]
    public void Only_a_climb_since_last_time_is_celebrated(Tier? before, Tier now, bool promoted) =>
        RankTiers.Promoted(before, now).Should().Be(promoted);
}
