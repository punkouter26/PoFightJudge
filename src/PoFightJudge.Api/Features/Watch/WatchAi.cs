using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Profiles;

namespace PoFightJudge.Api.Features.Watch;

/// <summary>
/// The real <see cref="IWatchAi"/>: builds a prompt, asks <see cref="IGeminiText"/> for schema-constrained JSON, and
/// parses it back into the game's vocabulary. Parsing is deliberately forgiving — a refusal or an unfenced answer
/// still has to become a line, because a match that stops mid-argument is worse than one odd line.
/// </summary>
public sealed class WatchAi(IGeminiText gemini, GeminiModelOptions models) : IWatchAi
{
    public const string RoundOperation = "round";

    public const string JudgeOperation = "judge";

    public const int RoundMaxOutputTokens = 512;

    public const int JudgeMaxOutputTokens = 1024;

    public bool IsFake => gemini.IsFake;

    public static JsonObject ArgumentSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["line"] = new JsonObject { ["type"] = "string" },
            ["mood"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. WatchRules.Moods.Select(m => (JsonNode)m)]) },
        },
        ["required"] = new JsonArray("line", "mood"),
    };

    /// <summary>
    /// The winner is an enum of exactly the two sides plus <see cref="WatchRules.NoWinner"/>, so the judge cannot
    /// name somebody who was not in the match. The draw is a word, not a blank: an empty enum value is rejected.
    /// </summary>
    public static JsonObject JudgeSchema(string husbandInitials, string wifeInitials) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["winner"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(husbandInitials, wifeInitials, WatchRules.NoWinner) },
            ["verdict"] = new JsonObject { ["type"] = "string" },
            ["husbandScore"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 100 },
            ["wifeScore"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 100 },
        },
        ["required"] = new JsonArray("winner", "verdict", "husbandScore", "wifeScore"),
    };

    public async Task<ArgumentResult> GenerateArgumentAsync(
        Profile husband,
        Profile wife,
        IReadOnlyList<RoundContext> history,
        string speaker,
        string attitude,
        bool wasSlapped,
        string? interjection = null,
        string? topic = null,
        CancellationToken ct = default) =>
        ParseArgument(await gemini.GenerateAsync(RoundRequest(husband, wife, history, speaker, attitude, wasSlapped, interjection, topic), ct));

    public async IAsyncEnumerable<ArgumentStreamPart> StreamArgumentAsync(
        Profile husband,
        Profile wife,
        IReadOnlyList<RoundContext> history,
        string speaker,
        string attitude,
        bool wasSlapped,
        string? interjection = null,
        string? topic = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var raw = new StringBuilder();
        var sent = 0;

        await foreach (var fragment in gemini.StreamAsync(RoundRequest(husband, wife, history, speaker, attitude, wasSlapped, interjection, topic), ct))
        {
            raw.Append(fragment);

            // The model is streaming JSON, so what the viewer may see is the decoded prefix of the "line" value —
            // never the braces, and never a half-written escape sequence.
            var visible = ExtractLinePrefix(raw.ToString());
            if (visible.Length > sent)
            {
                var delta = visible[sent..];
                sent = visible.Length;
                yield return new ArgumentStreamPart(delta);
            }
        }

        yield return new ArgumentStreamPart(null, ParseArgument(raw.ToString()));
    }

    public async Task<JudgeVerdictResult> JudgeAsync(Profile husband, Profile wife, IReadOnlyList<RoundContext> rounds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(husband);
        ArgumentNullException.ThrowIfNull(wife);

        var request = new GeminiTextRequest(
            JudgePromptBuilder.Build(husband, wife, rounds),
            models.Judge,
            JudgeOperation,
            JudgeSchema(husband.Initials, wife.Initials),
            Temperature: 0.4,
            MaxOutputTokens: JudgeMaxOutputTokens,
            ThinkingLevel: "low");

        return ParseVerdict(await gemini.GenerateAsync(request, ct), husband.Initials, wife.Initials);
    }

    private GeminiTextRequest RoundRequest(
        Profile husband,
        Profile wife,
        IReadOnlyList<RoundContext> history,
        string speaker,
        string attitude,
        bool wasSlapped,
        string? interjection,
        string? topic) =>
        new(
            RoundPromptBuilder.Build(husband, wife, history, speaker, attitude, wasSlapped, interjection, topic),
            models.Round,
            RoundOperation,
            ArgumentSchema,
            Temperature: 0.9,
            MaxOutputTokens: RoundMaxOutputTokens,
            ThinkingLevel: "minimal");

    /// <summary>A line, whatever came back. An unparseable answer becomes the line itself rather than an error the match cannot survive.</summary>
    public static ArgumentResult ParseArgument(string raw)
    {
        try
        {
            if (JsonNode.Parse(IsolateJsonObject(raw)) is JsonObject obj && obj["line"]?.GetValue<string>() is { } line && !string.IsNullOrWhiteSpace(line))
            {
                var mood = obj["mood"]?.GetValue<string>();
                return new ArgumentResult(line.Trim(), WatchRules.Moods.Contains(mood ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? mood!.ToLowerInvariant() : InferMood(line));
            }
        }
        catch (JsonException)
        {
            // Not JSON — an older model habit, or a refusal. Fall through and treat the answer as the line.
        }

        var text = string.IsNullOrWhiteSpace(raw) ? "…" : raw.Trim();
        return new ArgumentResult(text, InferMood(text));
    }

    public static JudgeVerdictResult ParseVerdict(string raw, string husbandInitials, string wifeInitials)
    {
        try
        {
            // Literal newlines inside a multi-line JSON string would break the parse; the verdict is prose, so
            // collapsing them costs nothing.
            var cleaned = IsolateJsonObject(raw).Replace("\r\n", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
            if (JsonNode.Parse(cleaned) is JsonObject obj)
            {
                var winner = obj["winner"]?.GetValue<string>()?.Trim() ?? string.Empty;
                return new JudgeVerdictResult(
                    NormalizeWinner(winner, husbandInitials, wifeInitials),
                    obj["verdict"]?.GetValue<string>()?.Trim() ?? "The judge did not explain the ruling.",
                    Score(obj["husbandScore"]),
                    Score(obj["wifeScore"]));
            }
        }
        catch (JsonException)
        {
            // Fall through to the no-ruling result below.
        }

        return new JudgeVerdictResult(string.Empty, string.IsNullOrWhiteSpace(raw) ? "The judge could not reach a ruling." : raw.Trim(), 50, 50);
    }

    /// <summary>Only one of the two sides can win. Anything else — a name, a role word, a blank — is a draw.</summary>
    public static string NormalizeWinner(string? winner, string husbandInitials, string wifeInitials)
    {
        var cleaned = winner?.Trim() ?? string.Empty;
        if (string.Equals(cleaned, husbandInitials, StringComparison.OrdinalIgnoreCase))
        {
            return husbandInitials;
        }

        return string.Equals(cleaned, wifeInitials, StringComparison.OrdinalIgnoreCase) ? wifeInitials : string.Empty;
    }

    /// <summary>Strips code fences and isolates the outermost object, so a fenced or chatty answer still parses.</summary>
    public static string IsolateJsonObject(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = cleaned.IndexOf('\n', StringComparison.Ordinal);
            if (newline >= 0)
            {
                cleaned = cleaned[(newline + 1)..];
            }

            var fence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                cleaned = cleaned[..fence];
            }
        }

        var start = cleaned.IndexOf('{', StringComparison.Ordinal);
        var end = cleaned.LastIndexOf('}');
        return start >= 0 && end > start ? cleaned[start..(end + 1)] : cleaned;
    }

    /// <summary>
    /// The decoded prefix of the "line" value from a possibly incomplete JSON buffer. A trailing incomplete escape is
    /// held back rather than mis-decoded; the final parse replaces the whole text anyway, so this only has to be
    /// clean enough to display.
    /// </summary>
    public static string ExtractLinePrefix(string raw)
    {
        var marker = raw.IndexOf("\"line\"", StringComparison.Ordinal);
        if (marker < 0)
        {
            return string.Empty;
        }

        var colon = raw.IndexOf(':', marker + 6);
        if (colon < 0)
        {
            return string.Empty;
        }

        var quote = raw.IndexOf('"', colon + 1);
        if (quote < 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        for (var i = quote + 1; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '"')
            {
                break;
            }

            if (c != '\\')
            {
                text.Append(c);
                continue;
            }

            if (i + 1 >= raw.Length)
            {
                break; // incomplete escape at the buffer edge — hold it back
            }

            i++;
            var escape = raw[i];
            if (escape == 'u')
            {
                if (i + 4 >= raw.Length)
                {
                    break; // incomplete \uXXXX — hold it back
                }

                if (ushort.TryParse(raw.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    text.Append((char)code);
                }

                i += 4;
            }
            else
            {
                text.Append(escape switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'b' => '\b', 'f' => '\f', _ => escape });
            }
        }

        return text.ToString();
    }

    private static int Score(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var score) ? Math.Clamp(score, 0, 100) : 50;

    /// <summary>Reads the register out of the words when the model did not label it — the stage cue at the end of a line is usually the tell.</summary>
    private static string InferMood(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower switch
        {
            _ when Any(lower, "furious", "rage", "venom", "seething") => "furious",
            _ when Any(lower, "angry", "mad", "snaps", "bitter") => "angry",
            _ when Any(lower, "sad", "upset", "tearful") => "sad",
            _ when Any(lower, "apologetic", "sorry") => "apologetic",
            _ when Any(lower, "smug", "scoff", "dry") => "smug",
            _ when Any(lower, "sarcastic") => "sarcastic",
            _ when Any(lower, "calm", "peaceful", "quiet") => "calm",
            _ when Any(lower, "cold", "chilling", "narrowing") => "hostile",
            _ => "angry",
        };

        static bool Any(string text, params string[] markers) => markers.Any(m => text.Contains(m, StringComparison.Ordinal));
    }
}
