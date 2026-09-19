using PoFightJudge.Client.Services;



namespace PoFightJudge.Unit.Client;



/// <summary>

/// T114 — a three-note tuning chord that lands before the persona preview line. Same shape as T110/T111:

/// C# catalogue name must agree with the JS voice, and the call site is the persona card's preview button.

/// </summary>

public sealed class TuningChordTests

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

    public void Sfx_catalogue_includes_chord()

    {

        Sfx.All.Should().Contain("chord",

            "the preview button fires Sfx.Chord before the TTS line — the C# name must match the JS voice");

    }



    [Fact]

    public void Chord_voice_is_defined_in_sfx_js()

    {

        var src = File.ReadAllText(Path.Combine(ClientRoot, "wwwroot", "js", "sfx.js"));

        src.Should().Contain("\"chord\"",

            "the JS catalogue is the source of truth; a missing voice is silence, not error");

    }



    [Fact]

    public void Profile_card_preview_plays_the_chord_before_the_line()

    {

        var src = File.ReadAllText(Path.Combine(ClientRoot, "Components", "ProfileCard.razor"));

        src.Should().Contain("Sfx.Chord",

            "the chord lives on the preview button; without it the tuning is gone");

    }

}
