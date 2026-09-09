using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PoFightJudge.Client.Components;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>The pieces of the live stage: where the fight is, how long is left, who is talking, and what was said.</summary>
/// <summary>The pieces of the live stage: where the fight is, how long is left, who is talking, and what was said.</summary>
public class FightLiveComponentTests : BunitContext
{
    public FightLiveComponentTests()
    {
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Captions_are_labelled_with_the_tag_that_said_them()
    {
        var log = new CaptionLog();
        log.Add(new CaptionDto(Speaker.Player1, "you never listen", true));
        log.Add(new CaptionDto(Speaker.Host, "one at a time", true));

        var cut = Render<CaptionFeed>(p => p
            .Add(f => f.Log, log)
            .Add(f => f.Player1Name, "AB")
            .Add(f => f.HostName, "The Referee"));

        var lines = cut.FindAll("article.caption");
        lines.Should().HaveCount(2);
        lines[0].TextContent.Should().Contain("AB").And.Contain("you never listen");
        lines[0].ClassList.Should().Contain("p1");
        lines[1].TextContent.Should().Contain("The Referee");
        lines[1].ClassList.Should().Contain("host");
    }
}

/// <summary>
/// A caption arrives in pieces and is rewritten as it grows, so the log has to replace rather than append — or a
/// sentence would appear a fragment at a time, each one a line of its own.
/// </summary>
/// <summary>
/// A caption arrives in pieces and is rewritten as it grows, so the log has to replace rather than append — or a
/// sentence would appear a fragment at a time, each one a line of its own.
/// </summary>
public class CaptionLogTests
{

    [Fact]
    public void A_long_fight_does_not_grow_the_page_without_limit()
    {
        var log = new CaptionLog();

        for (var i = 0; i < CaptionLog.MaxLines + 40; i++)
        {
            log.Add(new CaptionDto(Speaker.Player1, $"line {i}", true));
        }

        log.Lines.Should().HaveCount(CaptionLog.MaxLines);
        log.Lines[^1].Text.Should().Be($"line {CaptionLog.MaxLines + 39}", "it is the oldest lines that fall off");
    }
}
