using Bunit;
using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using PoFightJudge.Shared.Validators;
using Radzen;

namespace PoFightJudge.Unit.Client;

public class ProfileEditDialogTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public ProfileEditDialogTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        Services.AddSingleton<IValidator<CreateProfileRequest>, CreateProfileRequestValidator>();

        // The dialog asks the gate which voice controls to draw. Unasked, it answers "everything off", which is the
        // Gemini voice — the same section these tests were written against.
        _api.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(new FeatureFlagsDto(false, false, false, FishVoices: false));
        Services.AddSingleton(new FeatureGate(_api));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CreateProfileRequest Valid(string initials = "ABC") => new()
    {
        Initials = initials,
        Role = ProfileRole.Wife,
        Name = "Alex",
        Likes = "tea",
        Dislikes = "noise",
        TtsSettings = new TtsSettingsDto { VoiceName = "Kore" },
    };

    private static ProfileDto Dto(CreateProfileRequest persona, bool hasFace = false) =>
        new() { Id = ProfileId.From(persona.Initials), HasFace = hasFace, Persona = persona };

    [Fact]
    public async Task A_valid_new_persona_is_posted_and_the_saved_profile_is_handed_back()
    {
        var persona = Valid("abc");
        _api.CreateProfileAsync(Arg.Any<CreateProfileRequest>(), Arg.Any<CancellationToken>()).Returns(Dto(Valid("ABC")));
        ProfileDto? saved = null;
        var cut = Render<ProfileEditDialog>(p => p
            .Add(x => x.Template, persona)
            .Add(x => x.OnSaved, dto => saved = dto));

        cut.Markup.Should().Contain("Create profile");
        await cut.Find("form").SubmitAsync();

        await cut.WaitForAssertionAsync(() => saved.Should().NotBeNull());
        saved!.Id.Should().Be(ProfileId.From("ABC"));
        await _api.Received(1).CreateProfileAsync(Arg.Is<CreateProfileRequest>(r => r.Initials == "abc" && r.Likes == "tea"), Arg.Any<CancellationToken>());
        // A Vogen id cannot be an NSubstitute arg spec (default(ProfileId) is not equal to itself), so match any args instead.
        await _api.DidNotReceiveWithAnyArgs().UploadFaceAsync(ProfileId.From("ABC"), [], string.Empty, CancellationToken.None);
    }

    [Fact]
    public async Task Editing_locks_the_initials_puts_the_change_through_update_and_never_mutates_the_original()
    {
        var original = Dto(Valid("MAH"), hasFace: true);
        _api.UpdateProfileAsync(ProfileId.From("MAH"), Arg.Any<CreateProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Dto(call.Arg<CreateProfileRequest>(), hasFace: true));
        var cut = Render<ProfileEditDialog>(p => p.Add(x => x.Existing, original));

        cut.Find("input[name=Initials]").HasAttribute("disabled").Should().BeTrue("initials are the key");
        cut.Markup.Should().Contain("Save changes");
        await cut.Find("textarea[name=Likes]").ChangeAsync(new ChangeEventArgs { Value = "coffee" });
        await cut.Find("form").SubmitAsync();

        await _api.Received(1).UpdateProfileAsync(ProfileId.From("MAH"), Arg.Is<CreateProfileRequest>(r => r.Likes == "coffee"), Arg.Any<CancellationToken>());
        original.Persona.Likes.Should().Be("tea", "the form edits a copy until the server accepts it");
    }
}
