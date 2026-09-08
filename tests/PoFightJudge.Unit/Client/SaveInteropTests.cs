using Bunit;
using PoFightJudge.Client.Services;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// Handing a file to the browser. The name matters more than it looks: a topic is whatever somebody typed, and a
/// saved clip lands in a real folder on a real filesystem.
/// </summary>
public class SaveInteropTests : BunitContext
{
    [Theory]
    [InlineData("the thermostat", "Best line", "the-thermostat-best-line.wav")]
    [InlineData("Who forgot the bins?!", "Worst moment", "who-forgot-the-bins-worst-moment.wav")]
    [InlineData("", "Best line", "argument-best-line.wav")]
    [InlineData("the bins", "", "the-bins.wav")]
    public void A_saved_clip_is_named_after_the_argument_it_came_from(string topic, string label, string expected) =>
        SaveInterop.FileName(topic, label, "wav").Should().Be(expected);

    [Theory]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("a/b\\c:d*e?f", "a-b-c-d-e-f")]
    [InlineData("!!!", "")]
    [InlineData("  spaced  out  ", "spaced-out")]
    public void Anything_that_is_not_a_letter_or_a_digit_becomes_a_dash(string text, string expected) =>
        SaveInterop.Slug(text).Should().Be(expected);

    [Fact]
    public void A_very_long_topic_is_cut_short_rather_than_carried_whole()
    {
        var slug = SaveInterop.Slug(new string('a', 200));

        slug.Length.Should().BeLessThanOrEqualTo(48);
    }

    [Fact]
    public async Task Nothing_is_handed_to_the_browser_when_there_are_no_bytes()
    {
        var save = new SaveInterop(JSInterop.JSRuntime);

        (await save.FileAsync("clip.wav", "audio/wav", [])).Should().BeFalse();
        JSInterop.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task The_bytes_go_over_as_base64_with_the_name_and_the_type()
    {
        JSInterop.Setup<bool>(SaveInterop.File, _ => true).SetResult(true);
        var save = new SaveInterop(JSInterop.JSRuntime);

        (await save.FileAsync("the-bins.wav", "audio/wav", [1, 2, 3])).Should().BeTrue();

        var call = JSInterop.Invocations.Single();
        call.Identifier.Should().Be(SaveInterop.File);
        call.Arguments.Should().Equal("the-bins.wav", "audio/wav", Convert.ToBase64String([1, 2, 3]));
    }

    /// <summary>A browser that refuses is not a crash: the page says so and offers the player's own menu instead.</summary>
    [Fact]
    public async Task A_browser_that_refuses_is_reported_rather_than_thrown()
    {
        JSInterop.Setup<bool>(SaveInterop.File, _ => true).SetException(new InvalidOperationException("blocked"));
        var save = new SaveInterop(JSInterop.JSRuntime);

        (await save.FileAsync("the-bins.wav", "audio/wav", [1])).Should().BeFalse();
    }
}
