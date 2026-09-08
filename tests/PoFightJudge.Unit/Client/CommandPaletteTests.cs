using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;
using Radzen;
using Toolbelt.Blazor.Extensions.DependencyInjection;
using Toolbelt.Blazor.HotKeys2;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The palette. HotKeys2 was added to the solution in T01 and registered in Program.cs, and nothing had used it
/// since: every shortcut in the app was a click.
/// </summary>
public class CommandPaletteTests : BunitContext, IAsyncLifetime
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public CommandPaletteTests()
    {
        Services.AddRadzenComponents();
        Services.AddHotKeys2();
        Services.AddSingleton(_api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetFightersAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new FighterDto("AB", "Alex", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        ]);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>The palette's hot-key context disposes asynchronously, so bunit's teardown has to be awaited.</summary>
    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    /// <summary>
    /// Nothing is rendered until it is opened. A palette that is always in the DOM is markup on every page for a
    /// feature nobody has asked for yet.
    /// </summary>
    [Fact]
    public void The_palette_is_not_in_the_page_until_it_is_opened()
    {
        var cut = Render<CommandPalette>();

        cut.FindAll(".palette").Should().BeEmpty();
        cut.FindAll("input").Should().BeEmpty();
    }

    [Fact]
    public void Nothing_is_asked_of_the_api_just_by_mounting_it()
    {
        Render<CommandPalette>();

        _api.ReceivedCalls().Should().BeEmpty("a shortcut nobody has pressed must not cost a request");
    }

    [Fact]
    public void The_shortcut_sheet_stays_out_of_the_page_until_it_is_asked_for()
    {
        var cut = Render<ShortcutSheet>();

        cut.FindAll(".sheet").Should().BeEmpty();
        cut.Markup.Trim().Should().BeEmpty();
    }
}
