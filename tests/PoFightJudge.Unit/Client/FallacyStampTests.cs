using PoFightJudge.Client.Services;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// T111 — the wax-seal stamp on a fallacy. The contract is the same shape as T110: a C# catalogue name must
/// exist as a JS voice, and the script that drives the slam must be registered exactly once.
/// </summary>
public sealed class FallacyStampTests
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
    public void Sfx_catalogue_includes_thump()
    {
        Sfx.All.Should().Contain("thump",
            "the fallacy stamp fires Sfx.Thump per seal — the C# name must match the JS voice");
    }

    [Fact]
    public void Thump_voice_is_defined_in_sfx_js()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "sfx.js"));
        src.Should().Contain("\"thump\"",
            "the JS catalogue is the source of truth for what plays; a missing entry is silent, not loud");
    }

    [Fact]
    public void Fallacy_stamp_observer_script_exists()
    {
        var path = Path.Combine(ClientRoot, "wwwroot", "js", "fallacy-stamp.js");
        File.Exists(path).Should().BeTrue("the fallacy stamp observer is part of the effects layer and must be on disk");
    }

    [Fact]
    public void Fallacy_stamp_observer_is_registered_in_index_html_once()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "index.html"));
        var count = System.Text.RegularExpressions.Regex.Count(src, "js/fallacy-stamp\\.js", System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        count.Should().Be(1,
            "a duplicate script tag is a double-load and a double-fire on every stamp");
    }

    [Fact]
    public void Fallacy_stamp_observer_honours_reduced_motion()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "fallacy-stamp.js"));
        src.Should().Contain("prefers-reduced-motion",
            "reduced motion turns the thump off but the seals still appear; both halves must agree");
    }

    [Fact]
    public void Verdict_page_wraps_fallacies_with_the_stamp_component()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Pages", "Verdict.razor"));
        src.Should().Contain("<FallacyStamp",
            "every fallacy the verdict lists must be wrapped, or the slam only fires on whatever is wrapped");
    }

    [Fact]
    public void Verdict_css_marks_stamps_with_stamp_in_keyframe()
    {
        var path = Path.Combine(ClientRoot, "Components", "FallacyStamp.razor.css");
        File.Exists(path).Should().BeTrue("the slam animation is CSS, scoped to the component");
        var src = File.ReadAllText(path);
        src.Should().Contain("fallacy-slam",
            "the keyframe name is what the .stamp-in class triggers; renaming it silently breaks the slam");
        src.Should().Contain("stamp-in",
            "the trigger class is what the JS observer adds to start the animation");
    }
}
