using System.Text.RegularExpressions;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The UI rules from SPEC §5 that a reviewer would otherwise have to eyeball on every change: Radzen everywhere (no
/// raw form controls), one theme switch (no prefers-color-scheme), three breakpoints and no third.
/// </summary>
public partial class DesignRulesTests
{
    private static string ClientRoot => Path.Combine(RepositoryRoot(), "src", "PoFightJudge.Client");

    [Fact]
    public void No_raw_form_controls_in_any_razor_file()
    {
        var offenders = Directory.EnumerateFiles(ClientRoot, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (File: Path.GetRelativePath(ClientRoot, f), Hits: RawControl().Matches(File.ReadAllText(f)).Select(m => m.Value).ToList()))
            .Where(x => x.Hits.Count > 0)
            .Select(x => $"{x.File}: {string.Join(", ", x.Hits)}")
            .ToList();

        offenders.Should().BeEmpty("UI controls are Radzen components; InputFile is the one framework exception");
    }

    [Fact]
    public void Theming_has_one_switch_and_three_breakpoints()
    {
        var cssFiles = Directory.EnumerateFiles(ClientRoot, "*.css", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        cssFiles.Should().NotBeEmpty();

        var problems = new List<string>();
        foreach (var file in cssFiles)
        {
            var css = File.ReadAllText(file);
            var name = Path.GetRelativePath(ClientRoot, file);
            if (css.Contains("prefers-color-scheme", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{name}: prefers-color-scheme (the toggle is the only theme switch)");
            }

            foreach (Match m in MediaWidth().Matches(css))
            {
                var value = m.Groups["value"].Value;
                if (value is not ("40rem" or "48rem" or "64rem"))
                {
                    problems.Add($"{name}: breakpoint {value} (only 40rem / 48rem / 64rem exist)");
                }
            }
        }

        problems.Should().BeEmpty();
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

    [GeneratedRegex(@"<(?:button|input|select|textarea)\b", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex RawControl();

    [GeneratedRegex(@"@media[^{]*?\((?:max|min)-width:\s*(?<value>[\d.]+(?:rem|px|em))\)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex MediaWidth();
}
