using Bunit;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

public class AudioInteropTests : BunitContext
{
    private static readonly TtsAudioDto Mp3 = new("QUJD", "mp3");
    private static readonly TtsAudioDto Pcm = new("UENN", "pcm");

    [Fact]
    public async Task Play_and_enqueue_pass_the_bytes_with_the_format_they_actually_came_back_in()
    {
        JSInterop.SetupVoid(AudioInterop.Stop).SetVoidResult();
        JSInterop.Setup<double>(AudioInterop.Play, Mp3.Base64, Mp3.Format).SetResult(1.5);
        JSInterop.Setup<double>(AudioInterop.Enqueue, Pcm.Base64, Pcm.Format).SetResult(0.75);
        await using var sut = new AudioInterop(JSInterop.JSRuntime);

        (await sut.PlayAsync(Mp3)).Should().Be(TimeSpan.FromSeconds(1.5));
        (await sut.EnqueueAsync(Pcm)).Should().Be(TimeSpan.FromSeconds(0.75), "a chunk can fall back to another format mid-line");

        JSInterop.VerifyInvoke(AudioInterop.Play).Arguments.Should().Equal(Mp3.Base64, Mp3.Format);
        JSInterop.VerifyInvoke(AudioInterop.Enqueue).Arguments.Should().Equal(Pcm.Base64, Pcm.Format);
    }

    [Fact]
    public async Task Silence_never_reaches_the_browser_and_stop_and_pending_are_passed_through()
    {
        JSInterop.SetupVoid(AudioInterop.Stop).SetVoidResult();
        JSInterop.Setup<double>(AudioInterop.Pending).SetResult(2.25);
        await using var sut = new AudioInterop(JSInterop.JSRuntime);

        (await sut.PlayAsync(TtsAudioDto.None)).Should().Be(TimeSpan.Zero);
        (await sut.EnqueueAsync(TtsAudioDto.None)).Should().Be(TimeSpan.Zero);
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == AudioInterop.Play || i.Identifier == AudioInterop.Enqueue, "an empty payload is not worth a round trip");

        await sut.StopAsync();
        (await sut.PendingAsync()).Should().Be(TimeSpan.FromSeconds(2.25));
        JSInterop.VerifyInvoke(AudioInterop.Stop);
    }

    [Fact]
    public async Task The_end_of_playback_is_reported_back_to_the_page()
    {
        JSInterop.SetupVoid("PoAudio.onEnded", _ => true).SetVoidResult();
        JSInterop.SetupVoid(AudioInterop.Stop).SetVoidResult();
        await using var sut = new AudioInterop(JSInterop.JSRuntime);
        var ended = 0;
        sut.PlaybackEnded += (_, _) => ended++;

        await sut.WatchForEndAsync();
        sut.OnPlaybackEnded();

        ended.Should().Be(1);
        JSInterop.VerifyInvoke("PoAudio.onEnded");
    }
}
