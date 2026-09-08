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
/// How a mood moved through a fight, and what to show when there is not enough of it to draw a line.
/// </summary>
public class EmotionChartTests : BunitContext
{
    public EmotionChartTests()
    {
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PlayerAssessmentDto With(params EmotionPointDto[] timeline) => new(
        "B2", "why", 0, [], 5, 5, 5, [], 5, 5, 5, 50, [],
        new EmotionProfileDto(10, 45, 25, 20, 0, 0), timeline,
        "peak", ["dry"], "p", "e", "v", 5, 5, 5, 5, "best", "worst", []);

    [Fact]
    public void With_enough_points_the_movement_is_worth_drawing()
    {
        // The chart itself measures the real DOM to lay itself out, so what is asserted here is the decision.
        EmotionChart.CanDraw(With(
            new EmotionPointDto(15, "calm", 3),
            new EmotionPointDto(45, "frustrated", 7))).Should().BeTrue();
    }

    [Fact]
    public void One_point_is_a_dot_pretending_to_be_a_trend_so_the_whole_shape_is_shown_instead()
    {
        var one = With(new EmotionPointDto(20, "calm", 4));
        EmotionChart.CanDraw(one).Should().BeFalse();

        var cut = Render<EmotionChart>(p => p.Add(c => c.Assessment, one));

        cut.Markup.Should().Contain("Not enough of the fight was read");
        cut.FindAll(".share").Should().HaveCount(4, "only the emotions that actually showed");
    }

    [Fact]
    public void With_nothing_at_all_the_fallback_still_says_something_true()
    {
        var cut = Render<EmotionChart>(p => p.Add(c => c.Assessment, With()));

        cut.Find(".share[data-emotion=confident]").TextContent.Should().Contain("45");
        cut.FindAll(".share[data-emotion=amused]").Should().BeEmpty("nobody was amused, and a zero is not a mood");
    }
}
