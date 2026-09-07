using System.Diagnostics;

namespace PoMarriedFight.Unit.Infra;

/// <summary>
/// The scripts, read the way the templates are. Two of them touch a Key Vault and an app registration, so the
/// properties that matter are that they parse, that they say what they will do before doing it, and that a secret
/// never reaches a console, a log or a command line.
/// </summary>
public class ScriptTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PoMarriedFight.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The solution root was not found from the test binary.");
    }

    private static string Script(string name) => File.ReadAllText(Path.Combine(Root, "SCRIPTS", name));

    private static string Setup => Script("setup.ps1");

    private static string Seed => Script("seed-secrets.ps1");

    [SkippableFact]
    public void Every_script_parses()
    {
        var pwsh = FindPwsh();
        Skip.If(pwsh is null, "PowerShell 7 is not on this machine.");

        var command =
            "$bad = $false; " +
            "Get-ChildItem SCRIPTS -Filter *.ps1 | ForEach-Object { " +
            "  $errors = $null; " +
            "  [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$errors) | Out-Null; " +
            "  if ($errors.Count -gt 0) { $bad = $true; Write-Host \"$($_.Name): $($errors[0].Message)\" } }; " +
            "if ($bad) { exit 1 }";

        using var process = Process.Start(new ProcessStartInfo(pwsh!, ["-NoProfile", "-NonInteractive", "-Command", command])
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(milliseconds: 60_000).Should().BeTrue();
        process.ExitCode.Should().Be(0, $"every script must parse:\n{output}");
    }

    private static string? FindPwsh()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        var names = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "pwsh" } : ["pwsh"];
        return paths.SelectMany(p => names.Select(n => Path.Combine(p, n))).FirstOrDefault(File.Exists);
    }

    [Fact]
    public void Seeding_secrets_does_nothing_until_it_is_told_to()
    {
        Seed.Should().Contain("[switch]$Apply", "the write is opt-in, not opt-out");
        Seed.Should().Contain("if (-not $Apply)", "there is a guard before every write");
        Seed.Should().Contain("DRY RUN");

        // Every write in the script is inside an -Apply branch; none is reachable from a bare run.
        var lines = Seed.Split('\n');
        var writes = lines.Where(l => l.Contains("az keyvault secret set", StringComparison.Ordinal) || l.Contains("--method PATCH", StringComparison.Ordinal)).ToList();
        writes.Should().NotBeEmpty("the script does write, when asked");
        foreach (var write in writes)
        {
            var index = Array.IndexOf(lines, write);
            lines.Take(index).Should().Contain(l => l.Contains("if (-not $Apply)", StringComparison.Ordinal),
                "a write must sit after a guard that returns on a dry run");
        }
    }

    [Fact]
    public void A_secret_value_never_reaches_a_console_or_a_command_line()
    {
        // Downloaded to a file and set from that file: an argument is visible in a process list, and a value
        // written to a console is a value in a scrollback buffer.
        Seed.Should().Contain("az keyvault secret download").And.Contain("--file $temp.FullName");
        Seed.Should().NotContain("--value ");

        foreach (var line in Seed.Split('\n').Where(l => l.Contains("Write-Host", StringComparison.Ordinal) || l.Contains("Write-Ok", StringComparison.Ordinal)))
        {
            line.Should().NotContain("$value", "only names and decisions are printed");
        }

        // The temporary file is overwritten before it is deleted.
        Seed.Should().Contain("Set-Content -Path $temp.FullName").And.Contain("Remove-Item $temp.FullName");
    }

    [Fact]
    public void Setting_a_machine_up_reads_from_azure_and_writes_nothing_to_it()
    {
        Setup.Should().NotContain("az keyvault secret set")
            .And.NotContain("az deployment")
            .And.NotContain("az ad app");
        Setup.Should().Contain("az keyvault secret list", "it checks that the names are there, and nothing more");
        Setup.Should().Contain("--query '[].name'", "names, never values");
    }

    [Fact]
    public void Setup_covers_the_things_a_clean_machine_actually_lacks()
    {
        Setup.Should().Contain("dotnet dev-certs https --check --trust", "Azurite will not speak HTTPS without it, and the SDK will not send a token without HTTPS");
        Setup.Should().Contain("azurite.ps1").And.Contain("playwright.ps1");
        Setup.Should().Contain("dotnet test PoMarriedFight.slnx");

        // --no-launch-profile drops ASPNETCORE_ENVIRONMENT with the rest of launchSettings, and the app that comes
        // up is a degraded Production one. The variable has to be set, and an --environment argument does not do it.
        Setup.Should().Contain("$env:ASPNETCORE_ENVIRONMENT = 'Development'");
    }

    [Fact]
    public void The_redirect_uri_belongs_to_the_site_the_templates_create()
    {
        var bicep = File.ReadAllText(Path.Combine(Root, "infra", "main.bicep"));

        bicep.Should().Contain("var webAppName = 'app-pomarriedfight'");
        Seed.Should().Contain("$SiteUrl = 'https://app-pomarriedfight.azurewebsites.net'")
            .And.Contain("$RedirectUri = \"$SiteUrl/authentication/login-callback\"",
                "MSAL's WASM client redeems the code at /authentication/login-callback");
        Seed.Should().Contain("spa = @{ redirectUris", "the WASM client redeems the code itself, so it is a SPA URI, not a Web one");
    }
}
