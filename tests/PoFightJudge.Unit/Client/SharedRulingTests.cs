using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Pages;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The two halves of sharing a ruling: what a reader following the link sees, and what the owner can do about it.
/// The reader's half matters most — a link that is passed around must carry the result of the argument and not a
/// dossier on the two people who had it.
/// </summary>
public class SharedRulingTests : BunitContext
{
    private static readonly DateTimeOffset Night = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly MatchId _id = MatchId.New();

    public SharedRulingTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddScoped<SetupMemory>();
        Services.AddSingleton(_api);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static SharedMatchDto Shared() =>
        new(MatchMode.Fight, Night, "who forgot the bins", "AB", "CD", "AB", "He answered the point; she changed the subject.",
            IsFake: false, ["He stayed on the question.", "She moved the goalposts twice.", "Neither of them lied."]);

    [Fact]
    public void A_reader_following_the_link_gets_the_ruling()
    {
        _api.GetSharedAsync("tok", Arg.Any<CancellationToken>()).Returns(Shared());

        var cut = Render<SharedRuling>(p => p.Add(x => x.Token, "tok"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("AB took it."));
        cut.Markup.Should().Contain("who forgot the bins");
        cut.Markup.Should().Contain("She moved the goalposts twice.");
    }

    [Fact]
    public void A_link_that_was_taken_back_says_so_rather_than_showing_an_empty_page()
    {
        _api.GetSharedAsync("gone", Arg.Any<CancellationToken>()).Returns((SharedMatchDto?)null);

        var cut = Render<SharedRuling>(p => p.Add(x => x.Token, "gone"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("This link is not live"));
    }

    /// <summary>
    /// The reader is not signed in, so the page must never reach for anything that needs an account. Asking for the
    /// match itself would 401 in Production and show a stranger an error instead of the ruling they were sent.
    /// </summary>
    [Fact]
    public async Task Reading_a_shared_ruling_asks_for_nothing_that_needs_an_account()
    {
        _api.GetSharedAsync("tok", Arg.Any<CancellationToken>()).Returns(Shared());

        var cut = Render<SharedRuling>(p => p.Add(x => x.Token, "tok"));
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("AB took it."));

        // Asserted over every call the page made rather than method by method: an argument matcher over a Vogen id
        // is not a valid matcher, and "it asked for nothing else at all" is the stronger claim anyway.
        _api.ReceivedCalls().Select(c => c.GetMethodInfo().Name)
            .Should().Equal([nameof(IApiClient.GetSharedAsync)]);
    }

    [Fact]
    public async Task Nothing_is_shared_until_the_owner_presses_share()
    {
        _api.ShareMatchAsync(_id, Arg.Any<CancellationToken>()).Returns(new ShareResponse("tok", "/v/tok"));

        var cut = Render<ShareLink>(p => p.Add(x => x.Match, _id));

        _api.ReceivedCalls().Should().BeEmpty("nothing is shared by opening the page it sits on");
        cut.Markup.Should().NotContain("/v/tok");

        await cut.Find("button").ClickAsync(new());

        await _api.Received(1).ShareMatchAsync(_id, Arg.Any<CancellationToken>());
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("/v/tok"));
    }

    [Fact]
    public async Task The_owner_can_take_the_link_back()
    {
        var cut = Render<ShareLink>(p => p
            .Add(x => x.Match, _id)
            .Add(x => x.Token, "tok"));

        var stop = cut.FindAll("button").Single(b => b.TextContent.Contains("Stop sharing", StringComparison.Ordinal));
        await stop.ClickAsync(new());

        await _api.Received(1).UnshareMatchAsync(_id, Arg.Any<CancellationToken>());
        await cut.WaitForAssertionAsync(() => cut.Markup.Should().NotContain("/v/tok"));
    }
}
