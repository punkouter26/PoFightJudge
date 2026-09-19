using PoFightJudge.Client.Services;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// T113 — the WebGL2 aurora behind the history list. The shape is the same as every effect that crosses the
/// interop boundary: the C# constant and the JS shader must agree, the shader must be in the list the existing
/// compile-and-mount test enumerates, and the page must own a single host element with the right class.
/// </summary>
public sealed class AuroraTests
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
    public void Shader_catalogue_includes_aurora()
    {
        Shaders.All.Should().Contain("aurora",
            "the history page asks for Shaders.Aurora — a missing entry is a silent no-mount");
    }

    [Fact]
    public void Aurora_shader_is_defined_in_gfx_js()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "gfx.js"));
        src.Should().Contain("\"aurora\"",
            "the JS catalogue is the source of truth for what mounts; a missing key is a silent canvas");
    }

    [Fact]
    public void History_page_declares_an_aurora_host_element()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Pages", "History.razor"));
        src.Should().Contain("aurora-host",
            "the page is the only consumer; without the host the selector has nothing to mount on");
    }

    [Fact]
    public void History_css_makes_the_host_pass_through_clicks()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Pages", "History.razor.css"));
        src.Should().Contain("pointer-events: none",
            "the canvas lives behind the controls and must not intercept them");
    }

    [Fact]
    public void History_page_computes_streak_length()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "Pages", "History.razor"));
        src.Should().Contain("ComputeStreak",
            "the aurora is driven by the streak length; the computation lives in the page, not the shader");
    }
}
