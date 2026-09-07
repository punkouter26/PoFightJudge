using Microsoft.Extensions.Logging.Abstractions;
using PoMarriedFight.Api.Features.Ai;
using PoMarriedFight.Api.Features.Live;
using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.Integration;

/// <summary>
/// The only test that proves the Live wire format against the real endpoint. Everything else in Phase E is written
/// against a fake socket, so if Google moves the setup frame or the way the key is passed, this is what says so.
/// It skips without a key, which is the normal state on a development machine.
/// </summary>
public class GeminiLiveSmokeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(45);

    private static string? Key => Environment.GetEnvironmentVariable(ConfigKeys.Ai.GeminiApiKeyEnvVar);

    [SkippableFact]
    public async Task The_live_endpoint_accepts_our_setup_and_answers_out_loud()
    {
        Skip.If(string.IsNullOrWhiteSpace(Key), $"{ConfigKeys.Ai.GeminiApiKeyEnvVar} is not set, so there is no live session to open.");

        using var giveUp = new CancellationTokenSource(Patience);
        var client = new GeminiLiveClient(
            LiveSockets.ConnectAsync,
            LiveOptions.Defaults with { ApiKey = Key! },
            GeminiModelOptions.Defaults,
            NullLogger<GeminiLiveClient>.Instance);
        await using var _ = client;

        await client.ConnectAsync(
            new LiveSessionConfig("You are hosting a debate. Answer in one short sentence."),
            giveUp.Token);

        await client.SendTextAsync("Say hello to Alex and Sam and tell them the first round is thirty seconds.", giveUp.Token);

        var spokenBytes = 0;
        var heard = string.Empty;
        await foreach (var received in client.Events.ReadAllAsync(giveUp.Token))
        {
            switch (received)
            {
                case LiveServerEvent.AudioOut audio:
                    spokenBytes += audio.Pcm24k.Length;
                    break;
                case LiveServerEvent.OutputTranscript transcript:
                    heard += transcript.Text;
                    break;
                case LiveServerEvent.TurnComplete:
                case LiveServerEvent.Closed:
                    goto done;
                default:
                    break;
            }
        }

    done:
        await client.CloseAsync(CancellationToken.None);

        spokenBytes.Should().BeGreaterThan(0, "the host answers in audio, which is the whole point of the live session");
        heard.Should().NotBeNullOrWhiteSpace("output transcription was asked for in the setup frame");
    }
}
