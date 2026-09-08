using System.Text.Json;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The MSAL settings the deployed client reads at boot. They live in a static file no test used to touch, and the
/// first release shipped them empty — the site served 200s while the app sat on a blank authority and never reached
/// the sign-in page. The authority is deliberately <c>/common</c>: anyone with a Microsoft account signs in, which is
/// what the API's own <c>TenantId</c> ("common") already accepts. Pinning it to one tenant turns every account
/// outside that directory away ("Selected user account does not exist in tenant 'Default Directory'").
/// </summary>
public class ClientAuthSettingsTests
{
    [Fact]
    public void Production_msal_settings_are_filled_in_and_open_to_any_microsoft_account()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "PoFightJudge.Client", "wwwroot", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var azureAd = document.RootElement.GetProperty("AzureAd");

        var clientId = azureAd.GetProperty("ClientId").GetString();
        Guid.TryParse(clientId, out _).Should().BeTrue("MSAL needs the Entra registration's client id, not a placeholder");

        azureAd.GetProperty("Authority").GetString()
            .Should().Be("https://login.microsoftonline.com/common", "every Microsoft account may sign in, not one tenant's members");

        // Same app as client and as resource, so the scope resolves in whatever tenant the user comes from.
        azureAd.GetProperty("Scope").GetString().Should().Be($"api://{clientId}/access_as_user");
        azureAd.GetProperty("ValidateAuthority").GetBoolean().Should().BeTrue();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PoFightJudge.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run from inside the repository");
        return directory!.FullName;
    }
}
