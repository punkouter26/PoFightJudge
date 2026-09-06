using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Api.Features.Ai.Fakes;
using PoMarriedFight.Api.Features.Diagnostics;
using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Shared.Models;
using PoMarriedFight.Shared.Validators;

namespace PoMarriedFight.Unit.Profiles;

public class ProfileGeneratorTests
{
    private static ProfileGenerator Sut(IGeminiText gemini) => new(gemini, GeminiModelOptions.Defaults, NullLogger<ProfileGenerator>.Instance);

    [Fact]
    public void The_prompt_names_the_role_lists_the_roster_to_avoid_and_keeps_the_instructions_in_the_system_half()
    {
        var first = ProfileGenerator.BuildPrompt(ProfileRole.Wife, ["Matthew Herb — Software Engineer", "Kimberly Herb — CVS Pharmacy Scheduler"], attempt: 0);
        var retry = ProfileGenerator.BuildPrompt(ProfileRole.Husband, [], attempt: 1);

        first.User.Should().Contain("distinctive wife").And.Contain("Matthew Herb").And.Contain("Kimberly Herb");
        first.System.Should().Contain("Every field is required").And.NotContain("Matthew", "the roster changes; the instructions must not, so the prefix stays cacheable");
        retry.User.Should().Contain("distinctive husband").And.Contain("(none yet)").And.Contain("too thin");
        retry.System.Should().Be(first.System);
        ProfileGenerator.Schema["required"]!.AsArray().Should().HaveCount(19);
        ProfileGenerator.Schema["properties"]!["likes"]!["minLength"]!.GetValue<int>().Should().Be(ProfileGenerator.FieldMinLength);
    }

    [Fact]
    public void Parse_clamps_sliders_normalizes_enum_spellings_and_survives_fences_and_gaps()
    {
        const string raw = """
            ```json
            {"name":" Dot Marlowe ","age":150,"occupation":"Lighthouse auditor","likes":"fog","dislikes":"bells",
             "commonArguments":"who heard the horn","philosophy":"count the flashes","loveLanguage":"quality time",
             "attachmentStyle":"AVOIDANT","stressResponse":"fight-or-flight","logicVsEmotion":150,"patience":-5,"jealousy":40}
            ```
            """;

        var p = ProfileGenerator.Parse(raw, ProfileRole.Wife);

        p.Role.Should().Be(ProfileRole.Wife);
        p.Name.Should().Be("Dot Marlowe");
        p.Age.Should().Be(120, "age clamps into the validator range");
        p.LoveLanguage.Should().Be(LoveLanguage.QualityTime);
        p.AttachmentStyle.Should().Be(AttachmentStyle.Avoidant);
        p.StressResponse.Should().Be(StressResponse.Fight, "an unknown spelling falls back to the default");
        p.LogicVsEmotion.Should().Be(100);
        p.Patience.Should().Be(0);
        p.Jealousy.Should().Be(40);
        p.Punctuality.Should().Be(50, "missing sliders sit at neutral");
        ProfileGenerator.IsComplete(p).Should().BeTrue();

        var broken = ProfileGenerator.Parse("not json at all", ProfileRole.Husband);
        broken.Name.Should().Be("Unnamed Spouse");
        ProfileGenerator.IsComplete(broken).Should().BeFalse();
    }

    [Fact]
    public void Initials_come_from_the_name_and_step_aside_for_the_cast()
    {
        ProfileGenerator.MakeUniqueInitials("Kavya Priya Nair", []).Should().Be("KPN");
        ProfileGenerator.MakeUniqueInitials("Cher", []).Should().Be("CHE");
        ProfileGenerator.MakeUniqueInitials("Elena Rose Santiago", ["ers"]).Should().Be("ER2");
        ProfileGenerator.MakeUniqueInitials("Elena Rose Santiago", ["ERS", "ER2", "ER3"]).Should().Be("ER4");
        ProfileGenerator.MakeUniqueInitials(string.Empty, []).Should().Be("AI");
        ProfileGenerator.VoiceFor(ProfileRole.Wife, "Dot").VoiceName.Should().BeOneOf("Kore", "Zephyr");
        ProfileGenerator.VoiceFor(ProfileRole.Husband, "Dot").Should().BeEquivalentTo(ProfileGenerator.VoiceFor(ProfileRole.Husband, "Dot"), "the voice is derived from the name");
    }

    [Fact]
    public async Task Over_the_fake_the_draft_passes_the_shared_validator_and_avoids_existing_initials()
    {
        var sut = Sut(new FakeGeminiText(new AiLatencyTracker(), TimeSpan.Zero));
        var existing = new List<Profile>
        {
            Profile.Create("MAH", ProfileRole.Husband, "a", "b", "c", "d"),
        };

        var draft = await sut.GenerateAsync(ProfileRole.Wife, existing);

        (await new CreateProfileRequestValidator().ValidateAsync(draft)).IsValid.Should().BeTrue();
        draft.Role.Should().Be(ProfileRole.Wife);
        draft.Initials.Should().NotBe("MAH").And.MatchRegex("^[A-Z0-9]{1,3}$");
        draft.Likes.Length.Should().BeGreaterThanOrEqualTo(ProfileGenerator.FieldMinLength, "the fake honours minLength");
        draft.TtsSettings.VoiceName.Should().BeOneOf("Kore", "Zephyr");
    }

    [Fact]
    public async Task A_thin_first_answer_triggers_exactly_one_retry_with_the_nudge()
    {
        var gemini = Substitute.For<IGeminiText>();
        gemini.GenerateAsync(Arg.Any<GeminiTextRequest>(), Arg.Any<CancellationToken>())
            .Returns("{}", """{"name":"Ada Quill","age":40,"occupation":"Cartographer","likes":"maps","dislikes":"folds","commonArguments":"north","philosophy":"orient first"}""");

        var draft = await Sut(gemini).GenerateAsync(ProfileRole.Husband, []);

        draft.Name.Should().Be("Ada Quill");
        draft.Initials.Should().Be("AQD", "two words pad to three letters from the first word");
        await gemini.Received(2).GenerateAsync(Arg.Any<GeminiTextRequest>(), Arg.Any<CancellationToken>());
        await gemini.Received(1).GenerateAsync(Arg.Is<GeminiTextRequest>(r => r.Prompt.User.Contains("too thin", StringComparison.Ordinal) && r.Operation == ProfileGenerator.Operation), Arg.Any<CancellationToken>());
    }
}
