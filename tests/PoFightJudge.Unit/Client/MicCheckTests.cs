using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using Radzen;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The microphone check at setup. A fight that dies on a muted headset is three minutes nobody gets back, and until
/// this the first time anybody found out was when the host asked a question into silence.
/// </summary>
public class MicCheckTests : BunitContext, IAsyncLifetime
{
    public MicCheckTests()
    {
        Services.AddRadzenComponents();
        Services.AddScoped<MicInterop>();
        // The check pings when the level is pinned at the top, and it needs a clock to space the pings out.
        Services.AddSingleton(Substitute.For<ILocalStorageService>());
        Services.AddSingleton(TimeProvider.System);
        Services.AddScoped<SfxInterop>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>The check owns a microphone, which is released asynchronously; bunit's teardown has to be awaited.</summary>
    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);

    [Fact]
    public void The_microphone_is_not_opened_just_by_looking_at_the_setup_screen()
    {
        Render<MicCheck>();

        JSInterop.Invocations.Should().NotContain(i => string.Equals(i.Identifier, MicInterop.Start, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Testing_opens_the_microphone_and_shows_the_meter()
    {
        JSInterop.Setup<string>(MicInterop.Start, _ => true).SetResult(string.Empty);
        JSInterop.Setup<MicDevice[]>(MicInterop.Devices, _ => true).SetResult([]);
        var cut = Render<MicCheck>();

        await cut.Find("button").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindComponents<LevelMeter>().Should().ContainSingle());
    }

    /// <summary>
    /// A refused permission and a missing device are different problems, and the advice for one is useless for the
    /// other. The browser's own error name is what tells them apart.
    /// </summary>
    [Theory]
    [InlineData("NotAllowedError", "blocking the microphone")]
    [InlineData("NotFoundError", "No microphone was found")]
    [InlineData("NotReadableError", "holding the microphone")]
    [InlineData("NotSupportedError", "will not give a page the microphone")]
    public async Task A_microphone_that_will_not_open_says_what_to_do_about_it(string error, string advice)
    {
        JSInterop.Setup<string>(MicInterop.Start, _ => true).SetResult(error);
        var cut = Render<MicCheck>();

        await cut.Find("button").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain(advice));
        cut.FindComponents<LevelMeter>().Should().BeEmpty("there is nothing to meter");
    }

    /// <summary>
    /// Labels are blank until permission has been given once, so the list is asked for after the microphone opens.
    /// Offering it before that is a list of opaque device ids.
    /// </summary>
    [Fact]
    public async Task The_list_of_microphones_is_only_asked_for_once_permission_exists()
    {
        JSInterop.Setup<string>(MicInterop.Start, _ => true).SetResult(string.Empty);
        JSInterop.Setup<MicDevice[]>(MicInterop.Devices, _ => true).SetResult(
        [
            new("one", "Headset"),
            new("two", "Webcam"),
        ]);
        var cut = Render<MicCheck>();

        JSInterop.Invocations.Should().NotContain(i => string.Equals(i.Identifier, MicInterop.Devices, StringComparison.Ordinal));

        await cut.Find("button").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.Should().Contain("Which microphone"));
    }

    [Fact]
    public async Task Only_one_microphone_is_not_a_choice_worth_offering()
    {
        JSInterop.Setup<string>(MicInterop.Start, _ => true).SetResult(string.Empty);
        JSInterop.Setup<MicDevice[]>(MicInterop.Devices, _ => true).SetResult([new("one", "Headset")]);
        var cut = Render<MicCheck>();

        await cut.Find("button").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindComponents<LevelMeter>().Should().ContainSingle());
        cut.Markup.Should().NotContain("Which microphone");
    }
}
