using Bunit;
using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoMarriedFight.Client.Components;
using PoMarriedFight.Client.Services;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;
using PoMarriedFight.Shared.Validators;
using Radzen;

namespace PoMarriedFight.Unit.Client;

public class ProfileEditDialogTests : BunitContext
{
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public ProfileEditDialogTests()
    {
        Services.AddRadzenComponents();
        Services.AddSingleton(_api);
        Services.AddSingleton<IValidator<CreateProfileRequest>, CreateProfileRequestValidator>();
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
    public async Task Submitting_an_empty_form_shows_the_shared_validation_messages_and_calls_nothing()
    {
        var cut = Render<ProfileEditDialog>();

        await cut.Find("form").SubmitAsync();

        await cut.WaitForAssertionAsync(() =>
        {
            var messages = cut.FindAll(".validation-message").Select(m => m.TextContent).ToList();
            messages.Should().Contain("Initials are required.");
            messages.Should().Contain(m => m.Contains("Likes", StringComparison.Ordinal));
        });
        await _api.DidNotReceive().CreateProfileAsync(Arg.Any<CreateProfileRequest>(), Arg.Any<CancellationToken>());
    }

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

    [Fact]
    public async Task A_server_rejection_is_shown_in_the_dialog_instead_of_closing_it()
    {
        _api.CreateProfileAsync(Arg.Any<CreateProfileRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(409, "A profile with initials ABC already exists."));
        var closed = false;
        var cut = Render<ProfileEditDialog>(p => p
            .Add(x => x.Template, Valid())
            .Add(x => x.OnSaved, _ => closed = true));

        await cut.Find("form").SubmitAsync();

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("already exists"));
        closed.Should().BeFalse();
    }
}
