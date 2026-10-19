using System.Text.RegularExpressions;
using PoFightJudge.Client.Services;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The effect catalogues are spelled twice — the names in C#, the voices and shaders in JavaScript — and a name only
/// one side knows is silence or a blank canvas rather than an error. So the two lists are read and held together.
/// </summary>
public partial class EffectCatalogueTests
{
    private static string Js(string file) =>
        File.ReadAllText(Path.Combine(DesignRulesTests.ClientRoot, "wwwroot", "js", file));

    [Fact]
    public void Every_sound_named_in_csharp_has_a_voice_in_the_browser_and_no_more()
    {
        var script = Js("sfx.js");
        var voices = script[script.IndexOf("const VOICES = {", StringComparison.Ordinal)..];
        voices = voices[..voices.IndexOf("\n  };", StringComparison.Ordinal)];

        CatalogueKey().Matches(voices).Select(m => m.Groups["name"].Value)
            .Should().BeEquivalentTo(Sfx.All);
    }

    [Fact]
    public void Every_shader_named_in_csharp_is_one_the_browser_can_draw_and_no_more()
    {
        var script = Js("gfx.js");
        var shaders = script[script.IndexOf("const SHADERS = {", StringComparison.Ordinal)..];
        shaders = shaders[..shaders.IndexOf('}', StringComparison.Ordinal)];

        ShaderKey().Matches(shaders).Select(m => m.Groups["name"].Value)
            .Should().BeEquivalentTo(Shaders.All);
    }

    /// <summary>A voice is a quoted name at the start of a line, then a colon.</summary>
    [GeneratedRegex("""^\s+"(?<name>[a-z0-9-]+)":""", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CatalogueKey();

    [GeneratedRegex("""["'](?<name>[a-z0-9-]+)["']\s*:""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ShaderKey();
}
