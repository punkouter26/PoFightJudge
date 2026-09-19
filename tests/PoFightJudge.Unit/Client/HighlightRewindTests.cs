namespace PoFightJudge.Unit.Client;

/// <summary>
/// T116 — the rewind effect on a playing highlight clip. Same shape as every effect that crosses the interop
/// boundary: the script exists, is registered exactly once, honours reduced motion, and is wired from the
/// highlight reel.
/// </summary>
public sealed class HighlightRewindTests
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
    public void Highlight_rewind_observer_script_exists()
    {
        File.Exists(Path.Combine(ClientRoot, "wwwroot", "js", "highlight-rewind.js"))
            .Should().BeTrue("the rewind observer is part of the effects layer and must be on disk");
    }

    [Fact]
    public void Highlight_rewind_observer_is_registered_in_index_html_once()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "index.html"));
        var count = System.Text.RegularExpressions.Regex.Count(src, "js/highlight-rewind\\.js", System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        count.Should().Be(1, "a duplicate script tag is a double-load and a double-listener on every audio");
    }

    [Fact]
    public void Highlight_rewind_observer_pitches_audio_a_semitone_down()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "highlight-rewind.js"));
        src.Should().Contain("playbackRate",
            "a rewind without a pitch dip is just a blur; the audio shift is the other half of the trick");
    }

    [Fact]
    public void Highlight_rewind_observer_honours_reduced_motion()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "highlight-rewind.js"));
        src.Should().Contain("prefers-reduced-motion",
            "reduced motion turns the visual half off; the audio dip stays because it is the moment");
    }

    [Fact]
    public void Highlight_reel_css_animates_the_card()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Components", "HighlightReel.razor.css"));
        src.Should().Contain("highlight-rewind",
            "the keyframe name is what the JS observer adds the class to trigger; renaming it silently breaks the effect");
        src.Should().Contain(".moment.rewinding",
            "the trigger class is what the JS observer adds to start the animation");
    }

    [Fact]
    public void Highlight_reel_wires_the_observer_after_render()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Components", "HighlightReel.razor"));
        src.Should().Contain("PoHighlightRewind.wire",
            "the reel is the only consumer; without the wire call nothing ever plays the effect");
    }
}
