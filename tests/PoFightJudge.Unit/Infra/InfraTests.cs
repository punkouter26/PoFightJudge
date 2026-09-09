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
    public void Storage_is_reachable_only_by_the_identity_and_the_app_is_only_given_endpoints()
    {
        Resources.Should().Contain("allowSharedKeyAccess: false", "there is no connection-string path in this solution at all");
        Resources.Should().Contain("type: 'SystemAssigned'");
        Resources.Should().Contain("PoFightJudge__TableStorageEndpoint").And.Contain("PoFightJudge__BlobStorageEndpoint");
        Resources.Should().NotContainEquivalentOf("AccountKey=").And.NotContainEquivalentOf("listKeys(");
    }
}
