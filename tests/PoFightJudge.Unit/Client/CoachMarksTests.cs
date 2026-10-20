using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The first-visit tour: one thing at a time, in order, and once it is done or skipped it is not shown again. A
/// step whose target is not on the page is passed over rather than pointed at nothing.
/// </summary>
public sealed class CoachMarksTests : BunitContext
{
    private static readonly IReadOnlyList<CoachMarks.Step> Steps =
    [
        new("[data-channel='CPU']", "Watch", "Two of them argue."),
        new("[data-channel='missing']", "Gone", "Not on this page."),
        new("[data-channel='2P']", "Fight", "The two of you."),
    ];

    private readonly ILocalStorageService _storage = Substitute.For<ILocalStorageService>();

    public CoachMarksTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_storage);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var box = new CoachMarks.SpotBox(100, 40, 300, 200, 1440, 900);
        JSInterop.Setup<CoachMarks.SpotBox?>("PoFx.spot", call => call.Arguments[0] is string s && !s.Contains("missing", StringComparison.Ordinal)).SetResult(box);
        JSInterop.Setup<CoachMarks.SpotBox?>("PoFx.spot", call => call.Arguments[0] is string s && s.Contains("missing", StringComparison.Ordinal)).SetResult(null);
    }

    [Fact]
    public async Task It_walks_the_steps_skips_one_with_nothing_to_point_at_and_once_done_is_not_shown_again()
    {
        var cut = Render<CoachMarks>(p => p.Add(c => c.Steps, Steps));

        await cut.WaitForAssertionAsync(() => cut.Find(".coach h2").TextContent.Should().Be("Watch"));
        cut.Find(".coach .po-eyebrow").TextContent.Should().Be("1 of 3");

        await cut.FindAll(".coach .rz-button").Single(b => b.TextContent.Contains("Next", StringComparison.Ordinal)).ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Find(".coach h2").TextContent.Should().Be("Fight"));

        await cut.FindAll(".coach .rz-button").Single(b => b.TextContent.Contains("Done", StringComparison.Ordinal)).ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll(".coach").Should().BeEmpty());
        await _storage.Received().SetItemAsStringAsync("po-tour", "done", Arg.Any<CancellationToken>());

        // Somebody who has seen it is not shown it again.
        _storage.GetItemAsStringAsync("po-tour", Arg.Any<CancellationToken>()).Returns("done");
        var again = Render<CoachMarks>(p => p.Add(c => c.Steps, Steps));

        again.FindAll(".coach").Should().BeEmpty();
    }

    [Fact]
    public void The_demo_is_the_first_husband_against_the_first_wife_in_the_cast()
    {
        ProfileDto Persona(string initials, ProfileRole role) => new()
        {
            Id = ProfileId.From(initials),
            Persona = new CreateProfileRequest { Initials = initials, Name = initials + " Name", Role = role },
        };

        var pair = Home.DemoPair([Persona("WWW", ProfileRole.Wife), Persona("HHH", ProfileRole.Husband), Persona("HH2", ProfileRole.Husband)]);

        pair.Should().NotBeNull();
        pair!.Value.Husband.Id.Should().Be("HHH");
        pair.Value.Wife.Id.Should().Be("WWW");
        Home.DemoPair([Persona("HHH", ProfileRole.Husband)]).Should().BeNull("a demo needs both seats filled");
    }
}
