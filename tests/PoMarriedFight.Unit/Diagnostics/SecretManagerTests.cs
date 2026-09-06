using PoMarriedFight.Api.Common;

namespace PoMarriedFight.Unit.Diagnostics;

public class SecretManagerTests
{
    [Fact]
    public void MapKey_translates_double_dash_to_config_path()
    {
        (string Secret, string Expected)[] cases =
        [
            ("PoMarriedFight--GeminiApiKey", "PoMarriedFight:GeminiApiKey"),
            ("PoMarriedFight--AzureAd--ClientId", "PoMarriedFight:AzureAd:ClientId"),
            ("PoMarriedFight--Ai--RoundModel", "PoMarriedFight:Ai:RoundModel"),
        ];

        cases.Select(c => PoMarriedFightSecretManager.MapKey(c.Secret)).Should().Equal(cases.Select(c => c.Expected));
    }

    [Fact]
    public void Only_this_solutions_secrets_are_loaded_from_the_shared_vault()
    {
        PoMarriedFightSecretManager.Prefix.Should().Be("PoMarriedFight--");
        PoMarriedFightSecretManager.ShouldLoad("PoMarriedFight--GeminiApiKey").Should().BeTrue();
        PoMarriedFightSecretManager.ShouldLoad("pomarriedfight--GeminiApiKey").Should().BeTrue("Key Vault names are case-insensitive");
        PoMarriedFightSecretManager.ShouldLoad("PoMarriedLife--GeminiApiKey").Should().BeFalse("the sibling apps share kv-poshared");
        PoMarriedFightSecretManager.ShouldLoad("PoArgueJudge--GeminiApiKey").Should().BeFalse();
    }
}
