using Microsoft.Extensions.Http.Resilience;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Shared.Configuration;
using Polly;

namespace PoFightJudge.Api.Features.Voice;

/// <summary>
/// Registers the voice chain: the routing service plus whichever extra providers are configured. Gemini TTS (or the
/// fake) is already registered by <c>AddPoAi</c> and is the terminal provider; Fish and Azure join ahead of it when
/// their keys are present. Budgets here are deliberately tight — every provider sits in a fallback chain, so time
/// spent retrying a dead one is time not spent succeeding on the next.
/// </summary>
public static class VoiceServiceExtensions
{
    public const int FishAttemptSeconds = 25;
    public const int FishTotalSeconds = 55;

    /// <summary>Azure Speech answers in ~1–2 s. Anything past ten seconds is a stall, and the chain has a working fallback.</summary>
    public const int AzureAttemptSeconds = 10;
    public const int AzureTotalSeconds = 22;

    /// <summary>Returns the Azure Speech settings it resolved, because the analysis slice picks its transcriber from the same ones.</summary>
    public static AzureSpeechOptions AddPoVoice(this IServiceCollection services, IConfiguration configuration, AiMode mode)
    {
        var fishKey = configuration[ConfigKeys.Ai.FishAudioApiKey];
        var fish = new FishAudioOptions(!string.IsNullOrWhiteSpace(fishKey), configuration[ConfigKeys.Ai.FishDefaultReferenceId]);
        var azure = new AzureSpeechOptions(
            !string.IsNullOrWhiteSpace(configuration[ConfigKeys.Ai.AzureSpeechKey]) && !string.IsNullOrWhiteSpace(configuration[ConfigKeys.Ai.AzureSpeechRegion]),
            configuration[ConfigKeys.Ai.AzureSpeechKey] ?? string.Empty,
            configuration[ConfigKeys.Ai.AzureSpeechRegion] ?? string.Empty);

        services.AddSingleton(fish);
        services.AddSingleton(azure);
        services.AddSingleton(new TtsRoutingOptions(
            PreferFastVoice: configuration.GetValue<bool>($"{Flags.Section}:{Flags.PreferFastVoice}"),
            WireFormat: TtsAudioFormats.Normalize(configuration[ConfigKeys.Ai.TtsWireFormat]),
            CacheEnabled: configuration.GetValue<bool>($"{Flags.Section}:{Flags.TtsCacheEnabled}")));

        // Caching is a flag: off means every line is synthesized fresh, which is what a prompt-tuning session wants.
        if (configuration.GetValue<bool>($"{Flags.Section}:{Flags.TtsCacheEnabled}"))
        {
            services.AddSingleton<ITtsCache, BlobTtsCache>();
        }
        else
        {
            services.AddSingleton<ITtsCache, NullTtsCache>();
        }

        if (fish.Enabled)
        {
            services.AddHttpClient(FishAudioService.ClientName, client => client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fishKey))
                .AddVoicePipeline(FishAttemptSeconds, FishTotalSeconds, baseDelayMs: 1500);
            services.AddSingleton<ITtsProvider, FishAudioService>();
        }

        if (azure.Enabled)
        {
            services.AddHttpClient(AzureSpeechService.ClientName)
                .AddVoicePipeline(AzureAttemptSeconds, AzureTotalSeconds, baseDelayMs: 300);
            services.AddSingleton<ITtsProvider, AzureSpeechService>();
        }

        services.AddSingleton<ITtsService, RoutingTtsService>();

        // Transcription has no fallback chain: one provider is chosen up front, best first. Azure fast transcription
        // wins when it is configured because a SELF turn is the one place a player sits waiting; the fake stands in
        // whenever the AI mode is fake, so the whole turn works with no key at all.
        if (mode.UseFakes)
        {
            services.AddSingleton<ITranscriptionService, FakeTranscription>();
        }
        else if (azure.Enabled)
        {
            services.AddSingleton<ITranscriptionService, AzureTranscriptionService>();
        }
        else
        {
            services.AddSingleton<ITranscriptionService, GeminiTranscriptionService>();
        }

        return azure;
    }

    private static IHttpClientBuilder AddVoicePipeline(this IHttpClientBuilder builder, int attemptSeconds, int totalSeconds, int baseDelayMs)
    {
        builder.AddStandardResilienceHandler(o =>
        {
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(totalSeconds);
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(attemptSeconds);

            o.Retry.MaxRetryAttempts = 1;
            o.Retry.BackoffType = DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;
            o.Retry.Delay = TimeSpan.FromMilliseconds(baseDelayMs);

            o.CircuitBreaker.FailureRatio = 0.5;
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(attemptSeconds * 4);
            o.CircuitBreaker.MinimumThroughput = 10;
            o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        });
        return builder;
    }
}
