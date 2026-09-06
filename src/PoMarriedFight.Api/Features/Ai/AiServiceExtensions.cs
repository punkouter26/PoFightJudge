using PoMarriedFight.Api.Features.Diagnostics;
using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.Api.Features.Ai;

/// <summary>
/// The one decision every AI seam consults: real providers or the deterministic fakes. Made once at startup from
/// the environment, the Gemini key and the <c>UseFakeAi</c> flag; the reason is logged so a "why is it fake" is a
/// one-line grep.
/// </summary>
public sealed record AiMode(bool UseFakes, bool HasGeminiKey, string Reason);

public static class AiServiceExtensions
{
    /// <summary>
    /// Registers the mode, the model ids and the named Gemini clients. The typed clients (text, TTS, live, analysis)
    /// and their fakes register in their own slices and read <see cref="AiMode"/> to pick a side; the transport is
    /// always there so a real call can be made the moment a key appears.
    /// </summary>
    public static AiMode AddPoAi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var apiKey = configuration[ConfigKeys.Ai.GeminiApiKey];
        var hasKey = !string.IsNullOrWhiteSpace(apiKey);
        var forced = configuration.GetValue<bool>($"{Flags.Section}:{Flags.UseFakeAi}");
        var useFakes = DiagEndpoints.IsFakeAi(environment, configuration, forced);

        var reason = (useFakes, hasKey, forced) switch
        {
            (true, _, true) => $"{Flags.Section}:{Flags.UseFakeAi} is on",
            (true, _, _) => "no Gemini key configured outside Production",
            (false, true, _) => "Gemini key present",
            (false, false, _) => "Production without a Gemini key — AI calls fail until the secret is set (degraded mode)",
        };

        var mode = new AiMode(useFakes, hasKey, reason);
        services.AddSingleton(mode);
        services.AddSingleton(GeminiModelOptions.FromConfiguration(configuration));
        services.AddGeminiClients(apiKey);
        return mode;
    }
}
