namespace PoFightJudge.Unit.Client;

/// <summary>
/// T115 — the particle vortex behind the mic-check panel. Same contract as every other effect that
/// crosses the interop boundary: the script exists, is registered exactly once, honours reduced motion,
/// and reads the same --po-mic-level the existing meter reads.
/// </summary>
public sealed class MicVortexTests
{
    private static string ClientRoot => Path.Combine(RepositoryRoot(), "src", "PoFightJudge.Client");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PoFightJudge.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the solution root from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Mic_vortex_observer_script_exists()
    {
        File.Exists(Path.Combine(ClientRoot, "wwwroot", "js", "mic-vortex.js"))
            .Should().BeTrue("the vortex observer is part of the effects layer and must be on disk");
    }

    [Fact]
    public void Mic_vortex_observer_is_registered_in_index_html_once()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "index.html"));
        var count = System.Text.RegularExpressions.Regex.Count(src, "js/mic-vortex\\.js", System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        count.Should().Be(1, "a duplicate script tag is a double-load and a double-emit on every level update");
    }

    [Fact]
    public void Mic_vortex_observer_honours_reduced_motion()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "mic-vortex.js"));
        src.Should().Contain("prefers-reduced-motion",
            "reduced motion is the app's one switch; the vortex must no-op rather than throw");
    }

    [Fact]
    public void Mic_vortex_observer_reads_the_published_level()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "mic-vortex.js"));
        src.Should().Contain("--po-mic-level",
            "the vortex must read the same property the meter reads; any other source is a different signal");
    }

    [Fact]
    public void Mic_check_component_renders_a_vortex_host()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Components", "MicCheck.razor"));
        src.Should().Contain("class=\"vortex\"",
            "the panel is the only consumer; without the host the selector has nothing to mount on");
    }

    [Fact]
    public void Mic_check_css_makes_the_vortex_round()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Components", "MicCheck.razor.css"));
        src.Should().Contain("border-radius: 50%",
            "the vortex is a circle, not a box; the CSS is what makes that read");
    }
}
