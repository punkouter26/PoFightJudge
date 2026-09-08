using PoFightJudge.Api.Common;

namespace PoFightJudge.Unit.Diagnostics;

public class SecretManagerTests
{
    [Fact]
    public void MapKey_translates_double_dash_to_config_path()
    {
        (string Secret, string Expected)[] cases =
        [
            ("PoFightJudge--GeminiApiKey", "PoFightJudge:GeminiApiKey"),
            ("PoFightJudge--AzureAd--ClientId", "PoFightJudge:AzureAd:ClientId"),
            ("PoFightJudge--Ai--RoundModel", "PoFightJudge:Ai:RoundModel"),
        ];

        cases.Select(c => PoFightJudgeSecretManager.MapKey(c.Secret)).Should().Equal(cases.Select(c => c.Expected));
    }

    [Fact]
    public void Only_this_solutions_secrets_are_loaded_from_the_shared_vault()
    {
        PoFightJudgeSecretManager.Prefix.Should().Be("PoFightJudge--");
        PoFightJudgeSecretManager.ShouldLoad("PoFightJudge--GeminiApiKey").Should().BeTrue();
        PoFightJudgeSecretManager.ShouldLoad("pofightjudge--GeminiApiKey").Should().BeTrue("Key Vault names are case-insensitive");
        PoFightJudgeSecretManager.ShouldLoad("PoMarriedLife--GeminiApiKey").Should().BeFalse("the sibling apps share kv-poshared");
        PoFightJudgeSecretManager.ShouldLoad("PoArgueJudge--GeminiApiKey").Should().BeFalse();
    }
}
