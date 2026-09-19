using System.Text.RegularExpressions;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The UI rules from SPEC §5 that a reviewer would otherwise have to eyeball on every change: Radzen everywhere (no
/// raw form controls), one theme switch (no prefers-color-scheme), three breakpoints and no third.
/// </summary>
public partial class DesignRulesTests
{
    private static string ClientRoot => Path.Combine(RepositoryRoot(), "src", "PoFightJudge.Client");

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
    /// Scoped CSS that styles nothing. A rule in <c>Foo.razor.css</c> can only reach markup in <c>Foo.razor</c> —
    /// that is what the scope attribute is for — so a class named there and nowhere in the component is a rule that
    /// has stopped being about anything, and the next person to read it has to work that out for themselves.
    ///
    /// <c>::deep</c> rules are exempt: they deliberately reach a child component's markup. So are the classes
    /// JavaScript puts on an element, which are listed by name.
    /// </summary>
    [Fact]
    public void No_scoped_rule_styles_a_class_that_no_longer_exists()
    {
        var dead = new List<string>();
        foreach (var css in StyleSheets().Where(f => f.EndsWith(".razor.css", StringComparison.Ordinal)))
        {
            var component = css[..^4];
            if (!File.Exists(component))
            {
                continue;
            }

            var markup = File.ReadAllText(component)
                + (File.Exists(component + ".cs") ? File.ReadAllText(component + ".cs") : string.Empty);
            var name = Path.GetRelativePath(ClientRoot, css);

            foreach (var line in File.ReadAllLines(css))
            {
                if (line.Contains("::deep", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match m in ClassSelector().Matches(line))
                {
                    var css_class = m.Groups["name"].Value;
                    // Case-insensitively: PageShell builds its width class from an enum name it lower-cases, so
                    // ".narrow" is written "Narrow" in the markup that produces it.
                    if (!AddedByScript.Contains(css_class) && !markup.Contains(css_class, StringComparison.OrdinalIgnoreCase))
                    {
                        dead.Add($"{name}: .{css_class}");
                    }
                }
            }
        }

        dead.Distinct(StringComparer.Ordinal).Should().BeEmpty("a scoped rule can only reach its own component's markup");
    }

    /// <summary>Classes the JavaScript layer adds to an element, which therefore never appear in the markup.</summary>
    private static readonly HashSet<string> AddedByScript = new(StringComparer.Ordinal)
    {
        "fx-slap",
        "po-particles",
        "stamp-in",
        "thread-bolt--on",
    };

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

    [GeneratedRegex(@"(?<=^|[\s,>+~])\.(?<name>[a-z][a-z0-9-]*)(?![a-z0-9-]*\s*[:(])", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ClassSelector();
}
