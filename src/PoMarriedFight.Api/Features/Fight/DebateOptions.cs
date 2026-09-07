using PoMarriedFight.Shared.Configuration;

namespace PoMarriedFight.Api.Features.Fight;

/// <summary>Timing rules for a fight. Bound from <c>PoMarriedFight:Debate</c>.</summary>
public sealed class DebateOptions
{
    public const string Section = ConfigKeys.Debate.Section;

    public int MaxDebateSeconds { get; set; } = 180;

    /// <summary>
    /// How long the show may sit between the two of them being named and the argument formally starting. It exists
    /// because a real fight sat there for eight minutes: the couple simply started arguing, the host followed the
    /// argument instead of the script, and nothing was ever silent enough to nudge it.
    /// </summary>
    public int MaxSetupSeconds { get; set; } = 45;

    public int LongTalkerSeconds { get; set; } = 45;

    public int SilenceSeconds { get; set; } = 10;

    public int ProbeWindowStartSeconds { get; set; } = 60;

    public int ProbeWindowEndSeconds { get; set; } = 120;

    /// <summary>Hard stop on the questioning, so a chatty host cannot run forever.</summary>
    public int MaxProbeSeconds { get; set; } = 120;

    /// <summary>After the verdict is called, the show ends at the next host turn end — or after this long regardless.</summary>
    public int VerdictGraceSeconds { get; set; } = 45;

    /// <summary>Wall-clock ceiling on a live session. Bounds both memory and Gemini spend.</summary>
    public int MaxSessionSeconds { get; set; } = 900;

    /// <summary>
    /// Store the two recordings as Ogg/Opus rather than WAV. A fight is speech, and speech costs about a tenth as
    /// much in Opus; everything that reads a recording back decodes it to PCM first, so this is a storage choice
    /// and nothing else. Set it false to keep the WAV — useful when something needs to be listened to by hand.
    /// </summary>
    public bool StoreOpus { get; set; } = true;

    /// <summary>A session with no browser attached for this long is ended and persisted.</summary>
    public int ClientGraceSeconds { get; set; } = 30;

    /// <summary>
    /// Thin the microphone stream sent to Gemini during sustained silence. Input audio bills by the second whether
    /// anyone is speaking or not, and for much of a show nobody is — the host is. The recording is untouched: every
    /// frame still lands in the WAV, so offsets, metrics and clips are unaffected. Turn off if turn-taking suffers.
    /// </summary>
    public bool SilenceGating { get; set; } = true;

    /// <summary>
    /// How long a silence runs before frames start being dropped. The first loud frame is always forwarded, so
    /// speech onset — and with it barge-in — is never delayed; this grace only covers the tail of a phrase.
    /// </summary>
    public int SilenceGraceMilliseconds { get; set; } = 800;

    /// <summary>Past the grace, one frame in this many is still forwarded so the stream never goes fully quiet.</summary>
    public int SilenceKeepAliveEvery { get; set; } = 4;
}
