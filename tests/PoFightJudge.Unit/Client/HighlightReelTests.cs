using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The clips. They have been cut on demand since T40 and played in the page since T44, and there was no way to keep
/// one — which is the natural thing to want from a ten-second clip of somebody losing an argument.
/// </summary>
public class HighlightReelTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly MatchId _id = MatchId.New();

    public HighlightReelTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        Services.AddScoped<SaveInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.GetClipAsync(_id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([1, 2, 3, 4]);
    }

    private IRenderedComponent<HighlightReel> RenderReel() =>
        Render<HighlightReel>(p => p
            .Add(x => x.MatchId, _id)
            .Add(x => x.Topic, "who forgot the bins")
            .Add(x => x.Highlights, [new HighlightDto(Speaker.Player1, "Best line", "You always do this.", 12.5, 21.5)]));

    private static IEnumerable<AngleSharp.Dom.IElement> Buttons(IRenderedComponent<HighlightReel> cut) => cut.FindAll("button");

    [Fact]
    public async Task A_clip_can_be_kept_and_lands_named_after_the_argument()
    {
        JSInterop.Setup<bool>(SaveInterop.File, _ => true).SetResult(true);
        var cut = RenderReel();

        await Buttons(cut).Single(b => b.TextContent.Contains("Save it", StringComparison.Ordinal)).ClickAsync(new());

        await _api.Received(1).GetClipAsync(_id, 0, Arg.Any<CancellationToken>());
        var call = JSInterop.Invocations.Single(i => string.Equals(i.Identifier, SaveInterop.File, StringComparison.Ordinal));
        call.Arguments[0].Should().Be("who-forgot-the-bins-best-line.wav");
        call.Arguments[1].Should().Be("audio/wav");
    }

    /// <summary>
    /// The bytes are fetched with the caller's credentials rather than linked: in Production a download link to the
    /// clip route would get a 401, because the recording is in a private container.
    /// </summary>
    [Fact]
    public void The_clip_is_never_offered_as_a_plain_link()
    {
        var cut = RenderReel();

        cut.FindAll("a[download]").Should().BeEmpty();
        cut.FindAll("a[href*='/api/']").Should().BeEmpty();
    }

    [Fact]
    public async Task A_browser_that_refuses_to_save_says_so_rather_than_doing_nothing()
    {
        JSInterop.Setup<bool>(SaveInterop.File, _ => true).SetException(new InvalidOperationException("blocked"));
        var cut = RenderReel();

        await Buttons(cut).Single(b => b.TextContent.Contains("Save it", StringComparison.Ordinal)).ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("would not save the clip"));
    }

    [Fact]
    public async Task A_moment_that_cannot_be_cut_out_says_so_and_saves_nothing()
    {
        _api.GetClipAsync(_id, 0, Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        var cut = RenderReel();

        await Buttons(cut).Single(b => b.TextContent.Contains("Save it", StringComparison.Ordinal)).ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("could not be cut out"));
        JSInterop.Invocations.Should().NotContain(i => string.Equals(i.Identifier, SaveInterop.File, StringComparison.Ordinal));
    }

    private static HighlightDto Moment(string quote) => new(Speaker.Player1, "Best moment", quote, 12, 18);

    [Fact]
    public void Nothing_worth_keeping_says_so_rather_than_showing_an_empty_list()
    {
        var cut = Render<HighlightReel>(p => p.Add(r => r.MatchId, _id));

        cut.Markup.Should().Contain("No moment stood out");
    }

    [Fact]
    public async Task A_moment_is_fetched_rather_than_linked_because_the_recording_is_private()
    {
        _api.GetClipAsync(_id, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult<byte[]?>([1, 2, 3, 4]));
        var cut = Render<HighlightReel>(p => p
            .Add(r => r.MatchId, _id)
            .Add(r => r.Highlights, [Moment("you never listen")]));

        await cut.Find("button").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("audio").Should().ContainSingle());
        cut.Find("audio").GetAttribute("src").Should().StartWith("data:audio/wav;base64,");
    }

    [Fact]
    public async Task A_moment_that_cannot_be_cut_says_so_and_leaves_the_rest_alone()
    {
        _api.GetClipAsync(_id, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult<byte[]?>(null));
        var cut = Render<HighlightReel>(p => p
            .Add(r => r.MatchId, _id)
            .Add(r => r.Highlights, [Moment("you never listen"), Moment("that is not what I said")]));

        await cut.FindAll("button")[0].ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("could not be cut"));
        cut.FindAll("article.moment").Should().HaveCount(2);
    }
}
