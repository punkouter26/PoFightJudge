using Bunit;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using PoFightJudge.Client.Services;

namespace PoFightJudge.Unit.Client;

/// <summary>The bridge between the fight page and the browser's audio: capture up, the host's voice down.</summary>
public class LiveAudioTests : BunitContext
{
    private sealed class RecordingSink : IAudioFrameSink
    {
        private readonly List<byte[]> _frames = [];

        public IReadOnlyList<byte[]> Frames => _frames;

        public Task OnAudioFrameAsync(byte[] pcm16k)
        {
            _frames.Add(pcm16k);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Starting_capture_reports_the_device_rate_and_hands_the_browser_something_to_call_back_on()
    {
        JSInterop.Setup<CaptureStart>(LiveAudio.StartCapture, _ => true).SetResult(new CaptureStart(48_000, string.Empty));
        JSInterop.SetupVoid(LiveAudio.Stop).SetVoidResult();
        await using var sut = new LiveAudio(JSInterop.JSRuntime);

        var started = await sut.StartCaptureAsync(new RecordingSink());

        started.Started.Should().BeTrue();
        started.SampleRate.Should().Be(48_000, "the worklet resamples from whatever the device runs at");
        JSInterop.VerifyInvoke(LiveAudio.StartCapture).Arguments.Should().ContainSingle()
            .Which.Should().BeAssignableTo<DotNetObjectReference<AudioCaptureBridge>>("frames come back through this");
    }

    [Fact]
    public async Task A_refused_microphone_comes_back_as_a_problem_rather_than_an_exception()
    {
        JSInterop.Setup<CaptureStart>(LiveAudio.StartCapture, _ => true).SetResult(new CaptureStart(0, "NotAllowedError"));
        JSInterop.SetupVoid(LiveAudio.Stop).SetVoidResult();
        await using var sut = new LiveAudio(JSInterop.JSRuntime);

        var started = await sut.StartCaptureAsync(new RecordingSink());

        started.Started.Should().BeFalse();
        started.Error.Should().Be("NotAllowedError");
    }

    [Fact]
    public async Task A_browser_that_throws_on_the_bridge_is_a_problem_not_a_crash()
    {
        JSInterop.Setup<CaptureStart>(LiveAudio.StartCapture, _ => true).SetException(new JSException("live-audio.js is not loaded"));
        JSInterop.SetupVoid(LiveAudio.Stop).SetVoidResult();
        await using var sut = new LiveAudio(JSInterop.JSRuntime);

        (await sut.StartCaptureAsync(new RecordingSink())).Error.Should().Contain("live-audio.js");
    }

    [Fact]
    public async Task The_hosts_voice_is_queued_as_raw_bytes_and_can_be_dropped_when_it_is_interrupted()
    {
        JSInterop.SetupVoid(LiveAudio.Play, _ => true).SetVoidResult();
        JSInterop.SetupVoid(LiveAudio.Clear).SetVoidResult();
        JSInterop.SetupVoid(LiveAudio.Stop).SetVoidResult();
        await using var sut = new LiveAudio(JSInterop.JSRuntime);

        await sut.PlayAsync([1, 2, 3, 4]);
        await sut.ClearPlaybackAsync();

        JSInterop.VerifyInvoke(LiveAudio.Play).Arguments[0].Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
        JSInterop.VerifyInvoke(LiveAudio.Clear);
    }

    [Fact]
    public async Task A_frame_from_the_browser_reaches_the_sink_that_sends_it_on()
    {
        var sink = new RecordingSink();
        var bridge = new AudioCaptureBridge(sink);

        await bridge.OnAudioFrameAsync([7, 7]);

        sink.Frames.Should().ContainSingle().Which.Should().Equal([7, 7]);
    }

    [Fact]
    public async Task Leaving_the_page_releases_the_microphone()
    {
        JSInterop.SetupVoid(LiveAudio.Stop).SetVoidResult();
        var sut = new LiveAudio(JSInterop.JSRuntime);

        await sut.DisposeAsync();

        JSInterop.VerifyInvoke(LiveAudio.Stop);
    }
}

/// <summary>
/// How long the room keeps trying to get back into a fight. The default policy gives up after half a minute, which
/// is less than a restarted API takes to come back.
/// </summary>
public class ReconnectPolicyTests
{
    [Fact]
    public void The_first_attempts_ramp_up_quickly_and_then_settle_into_a_steady_retry()
    {
        var policy = new ForeverRetryPolicy();

        var delays = Enumerable.Range(0, 8)
            .Select(i => policy.NextRetryDelay(new RetryContext { PreviousRetryCount = i, ElapsedTime = TimeSpan.FromSeconds(i) }))
            .ToList();

        delays[0].Should().Be(TimeSpan.Zero, "the first retry is immediate, because most drops are momentary");
        delays[1].Should().Be(TimeSpan.FromSeconds(2));
        delays[2].Should().Be(TimeSpan.FromSeconds(5));
        delays.Skip(3).Should().OnlyContain(d => d == ForeverRetryPolicy.SteadyInterval);
        delays.Should().OnlyContain(d => d.HasValue, "giving up would leave the page dead and say nothing");
    }
}
