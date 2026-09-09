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

    /// <summary>
    /// Every <c>var(--po-*)</c> must name a token that exists. A <c>var()</c> on an undefined property drops the
    /// whole declaration silently, so a typo is invisible in review and shows up as a square corner or, in the one
    /// that prompted this test, white text on the yellow degraded banner.
    /// </summary>
    [Fact]
    public void Every_design_token_reference_resolves()
    {
        var cssFiles = StyleSheets();
        var defined = cssFiles
            .SelectMany(f => TokenDefinition().Matches(File.ReadAllText(f)).Select(m => m.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);
        defined.Should().NotBeEmpty();

        var unresolved = new List<string>();
        foreach (var file in cssFiles)
        {
            var name = Path.GetRelativePath(ClientRoot, file);
            foreach (Match m in TokenReference().Matches(File.ReadAllText(file)))
            {
                var token = m.Groups["name"].Value;
                if (!defined.Contains(token) && !PublishedAtRuntime.Contains(token))
                {
                    unresolved.Add($"{name}: var({token})");
                }
            }
        }

        unresolved.Should().BeEmpty("a var() on an undefined token drops the declaration without an error");
    }

    /// <summary>
    /// A phone's viewport is not 100vh. Mobile browsers size vh against the toolbar-less viewport, so an element
    /// asked for 100vh is taller than the screen the moment the toolbar is showing — which is a scrollbar on a page
    /// that fits. dvh is the unit that tracks the toolbar; vh is kept only as the line above it, for a browser that
    /// does not know dvh yet.
    /// </summary>
    [Fact]
    public void Full_height_is_measured_in_dynamic_viewport_units()
    {
        var problems = new List<string>();
        foreach (var file in StyleSheets())
        {
            var lines = File.ReadAllLines(file);
            var name = Path.GetRelativePath(ClientRoot, file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("100vh", StringComparison.Ordinal))
                {
                    continue;
                }

                // The fallback is the declaration immediately above its dvh twin; on its own it is the bug.
                var next = i + 1 < lines.Length ? lines[i + 1] : string.Empty;
                if (!next.Contains("100dvh", StringComparison.Ordinal))
                {
                    problems.Add($"{name}:{i + 1}: 100vh with no 100dvh on the line below it");
                }
            }
        }

        problems.Should().BeEmpty("vh overflows a phone's viewport by the height of its toolbar");
    }

    /// <summary>
    /// Tokens no stylesheet declares because JavaScript sets them on an element every frame. They are listed rather
    /// than pattern-matched so that adding one is a decision somebody makes.
    /// </summary>
    private static readonly HashSet<string> PublishedAtRuntime = new(StringComparer.Ordinal)
    {
        "--po-speak-level",
        "--po-mic-level",
        "--po-host-level",
        "--po-band-0", "--po-band-1", "--po-band-2", "--po-band-3",
        "--po-band-4", "--po-band-5", "--po-band-6", "--po-band-7",
    };

    private static List<string> StyleSheets() =>
        [.. Directory.EnumerateFiles(ClientRoot, "*.css", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

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

    [GeneratedRegex(@"(?<name>--po-[a-z0-9-]+)\s*:", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex TokenDefinition();

    [GeneratedRegex(@"var\(\s*(?<name>--po-[a-z0-9-]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex TokenReference();
}
