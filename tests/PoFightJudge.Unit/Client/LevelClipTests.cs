using System.Text.RegularExpressions;
using PoFightJudge.Client.Services;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// T110 — sparks on a clipping level meter. The contract is two halves that must stay in step:
/// <list type="bullet">
///   <item>The <c>Sfx.Clipping</c> name is in the C# catalogue (<see cref="Sfx.All"/>).</item>
///   <item>The <c>sparks</c> kind name is a constant the JS side knows about (see
///   <see cref="ParticleInterop.Sparks"/>); the same constant is what <c>js/level-clip.js</c> calls.</item>
///   <item><c>js/level-clip.js</c> exists and reads <c>--po-mic-level</c>, the property the recorders publish.</item>
/// </list>
/// A name that has drifted on one side is a silent failure in the other, so this test asserts all three.
/// </summary>
public sealed class LevelClipTests
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
    public void Sfx_catalogue_includes_clipping()
    {
        Sfx.All.Should().Contain("clipping",
            "MicCheck already plays Sfx.Clipping and T110 pairs it with sparks — the name must stay in the catalogue");
    }

    [Fact]
    public void ParticleInterop_exposes_a_sparks_kind()
    {
        ParticleInterop.Sparks.Should().Be("sparks",
            "the JS side calls PoParticles.burst('.meter', ParticleInterop.Sparks, '.lamp') — the value must match");
    }

    [Fact]
    public void Level_clip_observer_script_exists_and_watches_the_meter_property()
    {
        var path = Path.Combine(ClientRoot, "wwwroot", "js", "level-clip.js");
        File.Exists(path).Should().BeTrue("the level-clip observer is part of the effects layer and must be on disk");

        var src = File.ReadAllText(path);
        src.Should().Contain("--po-mic-level",
            "the observer must read the property the recorders actually publish");
        src.Should().Contain("sparks",
            "the observer must throw the same kind constant ParticleInterop.Sparks names");
        src.Should().Contain("PoParticles.burst",
            "the observer hands off to the existing particle layer, not a parallel one");
    }

    [Fact]
    public void Level_clip_observer_is_registered_in_index_html()
    {
        var path = Path.Combine(ClientRoot, "wwwroot", "index.html");
        var src = File.ReadAllText(path);
        Regex.Count(src, "js/level-clip\\.js", RegexOptions.NonBacktracking).Should().Be(1,
            "the script tag must appear once and only once — a duplicate is a double-load and an event-listener leak");
    }

    [Fact]
    public void Level_clip_observer_honours_reduced_motion()
    {
        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "level-clip.js"));
        src.Should().Contain("prefers-reduced-motion",
            "reduced motion is the app's one motion switch; the observer must no-op rather than throw");
    }
}
