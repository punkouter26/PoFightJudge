using System.Diagnostics;
using System.Text.RegularExpressions;
using PoFightJudge.Shared;

namespace PoFightJudge.Unit.Infra;

/// <summary>
/// The templates and the pipeline, checked the way everything else here is checked. None of this deploys anything:
/// it compiles the Bicep and reads the workflow, so a template that cannot build or a pipeline that names a
/// resource nothing creates is caught on this machine rather than half way through a release.
/// </summary>
public partial class InfraTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PoFightJudge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The solution root was not found from the test binary.");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string Main => Read("infra", "main.bicep");

    private static string Resources => Read("infra", "resources.bicep");

    private static string Workflow => Read(".github", "workflows", "deploy.yml");

    [SkippableFact]
    public void The_templates_compile()
    {
        var az = FindAz();
        Skip.If(az is null, "The Azure CLI is not on this machine; the templates are compiled in CI instead.");

        using var process = Process.Start(new ProcessStartInfo(az!, ["bicep", "build", "--file", Path.Combine(Root, "infra", "main.bicep"), "--stdout"])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var json = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit(milliseconds: 120_000).Should().BeTrue("az bicep build should not hang");

        // Warnings go to stderr too, so the exit code is what says whether it built.
        process.ExitCode.Should().Be(0, $"the templates must compile:\n{errors}");
        json.Should().Contain("Microsoft.Web/sites", "the compiled template is what would actually be deployed");
    }

    private static string? FindAz()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        var names = OperatingSystem.IsWindows() ? new[] { "az.cmd", "az.exe", "az" } : ["az"];
        return paths
            .SelectMany(p => names.Select(n => Path.Combine(p, n)))
            .FirstOrDefault(File.Exists);
    }

    [Fact]
    public void Everything_is_named_for_this_solution_and_nothing_is_left_over_from_the_one_it_was_ported_from()
    {
        var everything = Main + Resources + Workflow;

        everything.Should().NotContainEquivalentOf("poarguejudge").And.NotContainEquivalentOf("pomarriedlife");
        Main.Should().Contain("var resourceGroupName = 'PoFightJudge'")
            .And.Contain("var storageAccountName = 'stpofightjudge'")
            .And.Contain("var webAppName = 'app-pofightjudge'");
    }

    [Fact]
    public void The_pipeline_deploys_the_site_the_templates_create()
    {
        var webApp = WebAppName().Match(Main).Groups["name"].Value;

        webApp.Should().NotBeEmpty();
        Workflow.Should().Contain($"AZURE_WEBAPP_NAME: {webApp}")
            .And.Contain($"AZURE_WEBAPP_URL: https://{webApp}.azurewebsites.net");
    }

    [GeneratedRegex(@"var webAppName = '(?<name>[^']+)'", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WebAppName();

    [Fact]
    public void The_site_is_told_which_assembly_to_start_because_two_runtimeconfigs_ship_side_by_side()
    {
        // Oryx cannot infer a startup DLL when the publish folder carries the API's and the client's runtimeconfig.
        Resources.Should().Contain("appCommandLine: 'dotnet PoFightJudge.Api.dll'");
        Resources.Should().Contain("webSocketsEnabled: true", "the live fight is a SignalR hub carrying audio");
    }

    [Fact]
    public void Storage_is_reachable_only_by_the_identity_and_the_app_is_only_given_endpoints()
    {
        Resources.Should().Contain("allowSharedKeyAccess: false", "there is no connection-string path in this solution at all");
        Resources.Should().Contain("type: 'SystemAssigned'");
        Resources.Should().Contain("PoFightJudge__TableStorageEndpoint").And.Contain("PoFightJudge__BlobStorageEndpoint");
        Resources.Should().NotContainEquivalentOf("AccountKey=").And.NotContainEquivalentOf("listKeys(");
    }

    [Fact]
    public void The_free_tier_is_the_default_and_moving_off_it_is_a_deliberate_edit()
    {
        Main.Should().Contain("param appServicePlanSku string = 'F1'");
        Resources.Should().Contain("alwaysOn: !isFreePlan", "ARM rejects the write when Always On is set on F1");
    }

    [Fact]
    public void The_pipeline_signs_in_with_a_federated_credential_and_stores_no_secret()
    {
        Workflow.Should().Contain("id-token: write").And.Contain("azure/login@v2");
        Workflow.Should().NotContain("AZURE_CLIENT_SECRET").And.NotContain("creds:");
        Workflow.Should().Contain("cancel-in-progress: false", "cancelling a half-uploaded package is worse than queueing");
    }

    [Fact]
    public void The_deploy_is_not_believed_until_the_site_answers_for_itself()
    {
        Workflow.Should().Contain("expect /api/matches 401").And.Contain("expect /api/no-such-route 404");
        Workflow.Should().Contain("DeployedSiteTests", "status codes cannot see a WASM app that failed to boot");

        // Every route the smoke test asks about has to be a route this solution actually serves.
        foreach (var route in new[] { ApiRoutes.Health.Url, ApiRoutes.Matches.Base })
        {
            Workflow.Should().Contain(route);
        }
    }
}
