using PoFightJudge.Shared.Configuration;

namespace PoFightJudge.Api.Features.Live;

/// <summary>
/// What the Live socket needs beyond the model ids, which <see cref="Ai.GeminiModelOptions"/> already owns: where to
/// connect, the key to connect with, and how long to wait on the two moments that can hang.
/// </summary>
/// <remarks>
/// The endpoint is a constant rather than a config knob. Model ids move independently of this codebase and are
/// config-driven for that reason, but the BidiGenerateContent address is part of the protocol version: a change
/// there is a change to the frames as well, and repointing it alone would connect and then fail on the setup.
/// </remarks>
public sealed record LiveOptions(Uri Endpoint, string ApiKey, TimeSpan SetupTimeout, TimeSpan CloseTimeout)
{
    /// <summary>Verified against the Live API docs on 2026-09-06.</summary>
    public static Uri BidiEndpoint { get; } =
        new("wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent");

    public static LiveOptions Defaults { get; } = new(
        Endpoint: BidiEndpoint,
        ApiKey: string.Empty,
        SetupTimeout: TimeSpan.FromSeconds(15),
        CloseTimeout: TimeSpan.FromSeconds(5));

    public static LiveOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Defaults with { ApiKey = configuration[ConfigKeys.Ai.GeminiApiKey]?.Trim() ?? string.Empty };
    }
}
