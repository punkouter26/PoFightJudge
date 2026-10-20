using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// A cast card is a glance: who, what they do, how they lean and their line. The four labelled scales are one
/// press away rather than two thirds of the card.
/// </summary>
public sealed class ProfileCardTests : BunitContext, IAsyncLifetime
{
    public ProfileCardTests()
    {
        Services.AddSingleton(Substitute.For<IApiClient>());
        Services.AddScoped<AudioInterop>();
        Services.AddScoped<SfxInterop>();
        Services.AddSingleton(Substitute.For<Blazored.LocalStorage.ILocalStorageService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>The card holds an AudioInterop, which is released asynchronously; bunit's teardown has to be awaited.</summary>
    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public void The_four_scales_are_folded_away_behind_a_strip_that_still_shows_their_shape()
    {
        var profile = new ProfileDto
        {
            Id = ProfileId.From("MAH"),
            Persona = new CreateProfileRequest
            {
                Initials = "MAH",
                Name = "Matthew",
                Role = ProfileRole.Husband,
                LogicVsEmotion = 20,
                Patience = 70,
                HoldsGrudges = 40,
                Jealousy = 90,
                Philosophy = "Stay chill.",
            },
        };

        var cut = Render<ProfileCard>(p => p.Add(c => c.Profile, profile));

        var traits = cut.Find("details.traits");
        traits.HasAttribute("open").Should().BeFalse("the card opens folded");
        traits.QuerySelectorAll(".stat").Should().HaveCount(4, "every scale is still there when it is opened");
        cut.FindAll("summary .mini-fill").Select(f => f.GetAttribute("style"))
            .Should().Equal("--stat-value:20%", "--stat-value:70%", "--stat-value:40%", "--stat-value:90%");
        cut.Find(".quote").TextContent.Should().Contain("Stay chill.");
    }
}
