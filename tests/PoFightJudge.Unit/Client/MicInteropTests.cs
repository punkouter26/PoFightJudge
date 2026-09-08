using Bunit;
using Microsoft.JSInterop;
using PoFightJudge.Client.Services;

namespace PoFightJudge.Unit.Client;

public class MicInteropTests : BunitContext
{
    public MicInteropTests()
    {
        // Disposing the bridge always releases the microphone, so every test needs that leg configured.
        JSInterop.SetupVoid(MicInterop.Cancel).SetVoidResult();
    }

    [Fact]
    public async Task A_microphone_that_starts_reports_no_problem_and_hands_back_the_clip_it_recorded()
    {
        JSInterop.Setup<string>(MicInterop.Start, _ => true).SetResult(string.Empty);
        JSInterop.Setup<string>(MicInterop.Stop).SetResult("UklGRiQ=");
        await using var sut = new MicInterop(JSInterop.JSRuntime);

        var problem = await sut.StartAsync();
        var clip = await sut.StopAsync();

        problem.Should().BeNull("an empty error name means the microphone is live");
        clip.Should().Be("UklGRiQ=");
    }

    [Fact]
    public async Task A_refused_microphone_comes_back_as_the_browsers_own_error_name()
    {
        JSInterop.Setup<string>(MicInterop.Start, _ => true).SetResult("NotAllowedError");
        await using var sut = new MicInterop(JSInterop.JSRuntime);

        var problem = await sut.StartAsync();

        problem.Should().Be("NotAllowedError", "the page turns the name into advice, so the name has to survive the bridge");
    }

    [Fact]
    public async Task A_browser_that_throws_on_the_bridge_is_a_problem_not_a_crash()
    {
        JSInterop.Setup<string>(MicInterop.Start, _ => true).SetException(new JSException("mic.js is not loaded"));
        JSInterop.Setup<string>(MicInterop.Stop).SetException(new JSException("mic.js is not loaded"));
        await using var sut = new MicInterop(JSInterop.JSRuntime);

        (await sut.StartAsync()).Should().Contain("mic.js");
        (await sut.StopAsync()).Should().BeEmpty("a clip that could not be produced is no clip, not an exception the page has to catch");
    }

    [Fact]
    public async Task Leaving_the_page_releases_the_microphone()
    {
        JSInterop.SetupVoid(MicInterop.Cancel).SetVoidResult();
        var sut = new MicInterop(JSInterop.JSRuntime);

        await sut.DisposeAsync();

        JSInterop.VerifyInvoke(MicInterop.Cancel);
    }
}
