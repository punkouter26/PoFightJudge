namespace PoFightJudge.Unit.Client;

/// <summary>
/// T112 — the lightning-rod thread above the WATCH topic field. The component is a thin .NET wrapper around a
/// tiny JS observer, so the contract is: the script exists, is registered exactly once in <c>index.html</c>,
/// honours reduced motion, and is invoked from the topic input.
/// </summary>
public sealed class TopicThreadTests
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
    public void Topic_thread_observer_script_exists()
    {
        File.Exists(Path.Combine(ClientRoot, "wwwroot", "js", "topic-thread.js"))
            .Should().BeTrue("the topic-thread observer is part of the effects layer and must be on disk");
    }

    [Fact]
    public void Topic_thread_observer_is_registered_in_index_html_once()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "index.html"));
        var count = System.Text.RegularExpressions.Regex.Count(src, "js/topic-thread\\.js", System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        count.Should().Be(1,
            "a duplicate script tag is a double-load and a double-update on every keystroke");
    }

    [Fact]
    public void Topic_thread_observer_honours_reduced_motion()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "topic-thread.js"));
        src.Should().Contain("prefers-reduced-motion",
            "reduced motion turns the bolt off but the arc still draws; both halves must agree");
    }

    [Fact]
    public void Topic_thread_observer_sizes_the_arc_with_length()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "topic-thread.js"));
        src.Should().Contain("pathFor",
            "the arc must actually depend on the topic length; a constant path is a decoration, not a thread");
    }

    [Fact]
    public void Watch_page_uses_the_topic_thread_component()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Pages", "Watch.razor"));
        src.Should().Contain("<TopicThread",
            "the topic field is the only place the thread belongs; the page is the only consumer");
    }

    [Fact]
    public void Topic_thread_component_has_a_css_file()
    {
        File.Exists(Path.Combine(ClientRoot, "Components", "TopicThread.razor.css"))
            .Should().BeTrue("the bolt animation lives in scoped CSS so it does not leak into other components");
    }
}
