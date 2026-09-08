using PoFightJudge.Shared.Identifiers;

namespace PoFightJudge.Shared;

/// <summary>
/// The single source of truth for every HTTP path in the solution. Carter modules map from these constants and the
/// Blazor client builds its request URLs from the same ones, so a route can never drift between the two sides.
/// <c>*Segment</c> constants are relative and used inside a <c>MapGroup</c>; everything else is an absolute path.
/// </summary>
public static class ApiRoutes
{
    public const string ApiPrefix = "/api";

    public static class Health
    {
        /// <summary>Liveness + readiness (storage, configuration, AI reachability) — the deploy gate and the E2E probe.</summary>
        public const string Url = $"{ApiPrefix}/health";

        public const string DetailsSegment = "/details";
        public const string DetailsUrl = $"{Url}{DetailsSegment}";

        /// <summary>Client-side Blazor page. Owns bare <c>/health</c>; the machine probes live under <c>/healthz</c> so a server route never shadows it.</summary>
        public const string PageUrl = "/health";

        public const string ProbeUrl = "/healthz";
        public const string ReadyProbeUrl = "/healthz/ready";
    }

    public static class Diag
    {
        public const string Url = $"{ApiPrefix}/diag";
    }

    public static class Features
    {
        public const string Url = $"{ApiPrefix}/features";
    }

    /// <summary>BFF-facing auth routes, mounted at the root. Real sign-in goes through MSAL's <c>/authentication/*</c> client routes.</summary>
    public static class Auth
    {
        public const string Me = "/auth/me";
        public const string Guest = "/auth/guest";
        public const string Logout = "/auth/logout";
    }

    public static class Profiles
    {
        public const string Base = $"{ApiPrefix}/profiles";
        public const string ByIdSegment = "/{id}";
        public const string FaceSegment = "/{id}/face";
        public const string RecordSegment = "/{id}/record";
        public const string GenerateSegment = "/generate";
        public const string PreviewLineSegment = "/preview-line";

        public const string GenerateUrl = $"{Base}{GenerateSegment}";
        public const string PreviewLineUrl = $"{Base}{PreviewLineSegment}";

        public static string ById(ProfileId id) => $"{Base}/{Uri.EscapeDataString(id.Value)}";
        public static string Face(ProfileId id) => $"{ById(id)}/face";
        public static string Record(ProfileId id) => $"{ById(id)}/record";
        public static string Generate(string role) => $"{GenerateUrl}?role={Uri.EscapeDataString(role)}";
    }

    public static class Fighters
    {
        public const string Base = $"{ApiPrefix}/fighters";
        public const string ByTagSegment = "/{tag}";
        public const string ProfileSegment = "/{tag}/profile";

        /// <summary>The roster with each person's record. A tag is at most three characters, so this can never be one.</summary>
        public const string RosterSegment = "/roster";

        public const string RosterUrl = $"{Base}{RosterSegment}";

        public static string ByTag(FighterId tag) => $"{Base}/{Uri.EscapeDataString(tag.Value)}";

        /// <summary>Their record and how they argue, together — what one person's page is built from.</summary>
        public static string Profile(FighterId tag) => $"{ByTag(tag)}/profile";
    }

    public static class Seed
    {
        public const string ProfilesUrl = $"{ApiPrefix}/seed/profiles";
    }

    /// <summary>WATCH's client-driven round loop. Every route here sits behind the <c>ai-per-user</c> rate limit.</summary>
    public static class Watch
    {
        public const string Base = $"{ApiPrefix}/watch";
        public const string GenerateRoundSegment = "/generate-round";
        public const string GenerateRoundStreamSegment = "/generate-round-stream";
        public const string RoundAudioSegment = "/round-audio";
        public const string RoundAudioStreamSegment = "/round-audio-stream";
        public const string VerdictSegment = "/verdict";
        public const string TranscribeSegment = "/transcribe";

        public const string GenerateRoundUrl = $"{Base}{GenerateRoundSegment}";
        public const string GenerateRoundStreamUrl = $"{Base}{GenerateRoundStreamSegment}";
        public const string RoundAudioUrl = $"{Base}{RoundAudioSegment}";
        public const string RoundAudioStreamUrl = $"{Base}{RoundAudioStreamSegment}";
        public const string VerdictUrl = $"{Base}{VerdictSegment}";
        public const string TranscribeUrl = $"{Base}{TranscribeSegment}";

        // The two sides travel in the body rather than the path: a side is a persona or a live person, and the route
        // cannot say which without inventing an encoding for it.
    }

    /// <summary>Archived WATCH round audio, proxied through the API so it inherits the match's ownership check.</summary>
    public static class Audio
    {
        public const string Base = $"{ApiPrefix}/audio";
        public const string RoundSegment = "/{matchId}/{roundIndex:int}";

        public static string Round(MatchId matchId, int roundIndex) => $"{Base}/{matchId.Value}/{roundIndex}";
    }

    public static class Fights
    {
        public const string Base = $"{ApiPrefix}/fights";
        public const string ByIdSegment = "/{id}";
        public const string EndSegment = "/{id}/end";
        public const string TranscriptSegment = "/{id}/transcript";
        public const string AnalysisSegment = "/{id}/analysis";
        public const string AnalysisRetrySegment = "/{id}/analysis/retry";
        public const string ClipSegment = "/{id}/clips/{index:int}";

        public static string ById(MatchId id) => $"{Base}/{id.Value}";
        public static string End(MatchId id) => $"{ById(id)}/end";
        public static string Transcript(MatchId id) => $"{ById(id)}/transcript";
        public static string Analysis(MatchId id) => $"{ById(id)}/analysis";
        public static string AnalysisRetry(MatchId id) => $"{Analysis(id)}/retry";
        public static string Clip(MatchId id, int index) => $"{ById(id)}/clips/{index}";
    }

    /// <summary>Unified history: every match of the signed-in user, both modes.</summary>
    public static class Matches
    {
        public const string Base = $"{ApiPrefix}/matches";
        public const string ByIdSegment = "/{id}";

        /// <summary>What was actually said, in order — the transcript a WATCH replay is built from.</summary>
        public const string TurnsSegment = "/{id}/turns";

        public static string ById(MatchId id) => $"{Base}/{id.Value}";

        public static string Turns(MatchId id) => $"{ById(id)}/turns";
    }

    public static class Leaderboard
    {
        public const string Base = $"{ApiPrefix}/leaderboard";
        public const string WatchSegment = "/watch";
        public const string FightSegment = "/fight";

        public const string WatchUrl = $"{Base}{WatchSegment}";
        public const string FightUrl = $"{Base}{FightSegment}";
    }

    public static class Hubs
    {
        public const string Live = "/hubs/live";
    }
}
