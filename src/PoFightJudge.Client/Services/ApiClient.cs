using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The typed client over the API. Every path comes from <see cref="ApiRoutes"/>; pages never build URLs. An interface
/// so component tests substitute it instead of standing up HTTP. Grows one feature at a time.
/// </summary>
public interface IApiClient
{
    Task<AuthMeDto> GetMeAsync(CancellationToken ct = default);

    Task<AuthMeDto> GuestSignInAsync(CancellationToken ct = default);

    Task LogoutAsync(CancellationToken ct = default);

    Task<FeatureFlagsDto> GetFeaturesAsync(CancellationToken ct = default);

    Task<HealthReportDto?> GetHealthDetailsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken ct = default);

    Task<ProfileDto?> GetProfileAsync(ProfileId id, CancellationToken ct = default);

    /// <summary>Throws <see cref="ApiException"/> for a 400 (field errors) or 409 (initials taken).</summary>
    Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken ct = default);

    Task<ProfileDto> UpdateProfileAsync(ProfileId id, CreateProfileRequest request, CancellationToken ct = default);

    Task DeleteProfileAsync(ProfileId id, CancellationToken ct = default);

    /// <summary>Sends the raw image; the server re-encodes it to the 512 px PNG the cards show.</summary>
    Task<ProfileDto> UploadFaceAsync(ProfileId id, byte[] image, string contentType, CancellationToken ct = default);

    Task<SeedResultDto> SeedProfilesAsync(CancellationToken ct = default);

    /// <summary>An AI-invented, unsaved persona draft for the editor.</summary>
    Task<CreateProfileRequest> GenerateProfileAsync(ProfileRole role, CancellationToken ct = default);

    /// <summary>One in-character line spoken in the persona's own voice; nothing is saved.</summary>
    Task<PreviewLineResponse> PreviewLineAsync(CreateProfileRequest persona, CancellationToken ct = default);

    /// <summary>The next line of a WATCH match, for the persona whose turn it is.</summary>
    Task<GenerateRoundResponse> GenerateRoundAsync(GenerateRoundRequest request, CancellationToken ct = default);

    /// <summary>Speaks a line that has already been generated.</summary>
    Task<TtsAudioDto> RoundAudioAsync(RoundAudioRequest request, CancellationToken ct = default);

    /// <summary>Ends the match: the judge rules and the whole thing is recorded.</summary>
    Task<VerdictResponse> VerdictAsync(VerdictRequest request, CancellationToken ct = default);

    /// <summary>Turns one recorded spoken turn into words. An empty answer means nothing intelligible was said.</summary>
    Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken ct = default);

    /// <summary>Starts a live fight. Throws <see cref="ApiException"/> when the tags or the host are not usable.</summary>
    Task<CreateFightResponse> StartFightAsync(CreateFightRequest request, CancellationToken ct = default);

    Task EndFightAsync(MatchId id, CancellationToken ct = default);

    /// <summary>Everyone who has ever argued. Empty rather than an error, so a picker degrades to a plain box.</summary>
    Task<IReadOnlyList<FighterDto>> GetFightersAsync(CancellationToken ct = default);

    /// <summary>One fighter, or null when this tag is new.</summary>
    Task<FighterDto?> GetFighterAsync(FighterId tag, CancellationToken ct = default);

    /// <summary>Everyone who has argued, each with their record. What the roster page is made of.</summary>
    Task<IReadOnlyList<FighterStatsDto>> GetRosterAsync(CancellationToken ct = default);

    /// <summary>How the reading of a fight is going, and the report once there is one.</summary>
    Task<AnalysisResponse> GetAnalysisAsync(MatchId id, CancellationToken ct = default);

    /// <summary>Sends a fight back to be read again. Throws <see cref="ApiException"/> if one is already running.</summary>
    Task RetryAnalysisAsync(MatchId id, CancellationToken ct = default);

    /// <summary>One highlight, cut out of the recording. Null when there is nothing there to cut.</summary>
    Task<byte[]?> GetClipAsync(MatchId id, int index, CancellationToken ct = default);

    /// <summary>Everything the signed-in person has been part of, newest first.</summary>
    Task<IReadOnlyList<MatchDto>> GetMatchesAsync(MatchMode? mode = null, CancellationToken ct = default);

    /// <summary>One match, or null when it is not this account's. What a replay opens on.</summary>
    Task<MatchDto?> GetMatchAsync(MatchId id, CancellationToken ct = default);

    /// <summary>What was said in one match, in order. Empty when there is nothing stored.</summary>
    Task<IReadOnlyList<TurnDto>> GetMatchTurnsAsync(MatchId id, CancellationToken ct = default);

    /// <summary>One stored round, ready to hand to the player. Null when that round was never spoken.</summary>
    Task<TtsAudioDto?> GetRoundAudioAsync(MatchId id, int roundIndex, CancellationToken ct = default);

    Task DeleteMatchAsync(MatchId id, CancellationToken ct = default);

    /// <summary>The board for one mode. Empty rather than an error, because a board is never the point of the page.</summary>
    Task<IReadOnlyList<LeaderboardRowDto>> GetLeaderboardAsync(MatchMode mode, CancellationToken ct = default);

    /// <summary>One persona's record across the watches it has argued in.</summary>
    Task<ProfileRecordDto?> GetProfileRecordAsync(ProfileId id, CancellationToken ct = default);

    /// <summary>One person's page: their record, how they argue, and what they argued.</summary>
    Task<FighterProfileDto?> GetFighterProfileAsync(FighterId tag, CancellationToken ct = default);

    /// <summary>Changes a fighter's display name. The tag never changes.</summary>
    Task<FighterDto> RenameFighterAsync(FighterId tag, string? displayName, CancellationToken ct = default);

    Task DeleteFighterAsync(FighterId tag, CancellationToken ct = default);
}

/// <summary>A non-success answer from the API, carrying the problem details so a form can show the server's words.</summary>
public sealed class ApiException : Exception
{
    public ApiException()
        : this(0, "The request failed.")
    {
    }

    public ApiException(string message)
        : this(0, message)
    {
    }

    public ApiException(string message, Exception innerException)
        : base(message, innerException)
    {
        Title = message;
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

    public ApiException(int status, string title, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(title)
    {
        Status = status;
        Title = title;
        Errors = errors ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

    public int Status { get; }

    public string Title { get; }

    /// <summary>
    /// How long the server asked us to wait, from a 429's <c>Retry-After</c>. Null when it did not say — which is
    /// every status but that one, and a throttle from something in front of the API that does not set the header.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>True when this was the per-user AI limit rather than a fault: the same request will work later.</summary>
    public bool IsThrottled => Status == 429;

    /// <summary>Field → messages, from a validation problem; empty otherwise.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>Every message the server sent, in one line — what a dialog shows when it has no field to attach them to.</summary>
    public string Summary => Errors.Count == 0 ? Title : string.Join(" ", Errors.Values.SelectMany(v => v));

    public static async Task<ApiException> FromAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ProblemDto? problem = null;
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ProblemDto>(ct);
            }
            catch (System.Text.Json.JsonException)
            {
                // A non-JSON body (a proxy page, an empty 500): the status line below is all we have.
            }
        }

        // A throttle is not a fault, and "The API answered 429 Too Many Requests." is not something to show anybody.
        // The wait comes from the header as a delta; some servers send a date instead, so both forms are read.
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Only the delta form: turning the date form into a wait needs a clock, and a static factory has none.
            // Our own limiter sends the delta, and a page falls back to a window of its own when there is nothing.
            var wait = response.Headers.RetryAfter?.Delta;
            return new ApiException(
                (int)response.StatusCode,
                problem?.Title ?? "That was a lot of arguing at once. Give it a moment.",
                problem?.Errors)
            {
                RetryAfter = wait,
            };
        }

        var title = problem?.Title ?? problem?.Detail ?? $"The API answered {(int)response.StatusCode} {response.ReasonPhrase}.";
        return new ApiException((int)response.StatusCode, title, problem?.Errors);
    }

    /// <summary>The subset of RFC 9457 problem details the client reads.</summary>
    private sealed class ProblemDto
    {
        public string? Title { get; set; }

        public string? Detail { get; set; }

        public int? Status { get; set; }

        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class ApiClient(IHttpClientFactory clients) : IApiClient
{
    // The authorized client carries the MSAL token in Production; the anonymous one is for the calls made before
    // anyone is signed in (the feature flags the sign-in page and the banner read).
    private readonly HttpClient http = clients.CreateClient(HttpClients.Api);
    private readonly HttpClient anonymous = clients.CreateClient(HttpClients.Anonymous);

    public async Task<AuthMeDto> GetMeAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<AuthMeDto>(ApiRoutes.Auth.Me, ct) ?? AuthMeDto.Anonymous;

    public async Task<AuthMeDto> GuestSignInAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync(Relative(ApiRoutes.Auth.Guest), null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthMeDto>(ct) ?? AuthMeDto.Anonymous;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync(Relative(ApiRoutes.Auth.Logout), null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<FeatureFlagsDto> GetFeaturesAsync(CancellationToken ct = default) =>
        await anonymous.GetFromJsonAsync<FeatureFlagsDto>(ApiRoutes.Features.Url, ct) ?? new FeatureFlagsDto(false, false, false, false);

    public Task<HealthReportDto?> GetHealthDetailsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<HealthReportDto>(ApiRoutes.Health.DetailsUrl, ct);

    public async Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ProfileDto>>(ApiRoutes.Profiles.Base, ct) ?? [];

    public async Task<ProfileDto?> GetProfileAsync(ProfileId id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Profiles.ById(id)), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadAsync<ProfileDto>(response, ct);
    }

    public async Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Relative(ApiRoutes.Profiles.Base), request, ct);
        return await ReadAsync<ProfileDto>(response, ct);
    }

    public async Task<ProfileDto> UpdateProfileAsync(ProfileId id, CreateProfileRequest request, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync(Relative(ApiRoutes.Profiles.ById(id)), request, ct);
        return await ReadAsync<ProfileDto>(response, ct);
    }

    public async Task DeleteProfileAsync(ProfileId id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync(Relative(ApiRoutes.Profiles.ById(id)), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromAsync(response, ct);
        }
    }

    public async Task<ProfileDto> UploadFaceAsync(ProfileId id, byte[] image, string contentType, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await http.PostAsync(Relative(ApiRoutes.Profiles.Face(id)), content, ct);
        return await ReadAsync<ProfileDto>(response, ct);
    }

    public async Task<SeedResultDto> SeedProfilesAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync(Relative(ApiRoutes.Seed.ProfilesUrl), null, ct);
        return await ReadAsync<SeedResultDto>(response, ct);
    }

    public async Task<CreateProfileRequest> GenerateProfileAsync(ProfileRole role, CancellationToken ct = default)
    {
        using var response = await http.PostAsync(Relative(ApiRoutes.Profiles.Generate(role.ToString())), null, ct);
        return await ReadAsync<CreateProfileRequest>(response, ct);
    }

    public async Task<PreviewLineResponse> PreviewLineAsync(CreateProfileRequest persona, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Relative(ApiRoutes.Profiles.PreviewLineUrl), persona, ct);
        return await ReadAsync<PreviewLineResponse>(response, ct);
    }

    public async Task<GenerateRoundResponse> GenerateRoundAsync(GenerateRoundRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Relative(ApiRoutes.Watch.GenerateRoundUrl), request, ct);
        return await ReadAsync<GenerateRoundResponse>(response, ct);
    }

    public async Task<TtsAudioDto> RoundAudioAsync(RoundAudioRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Relative(ApiRoutes.Watch.RoundAudioUrl), request, ct);
        return await ReadAsync<TtsAudioDto>(response, ct);
    }

    public async Task<VerdictResponse> VerdictAsync(VerdictRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Relative(ApiRoutes.Watch.VerdictUrl), request, ct);
        return await ReadAsync<VerdictResponse>(response, ct);
    }

    public async Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Relative(ApiRoutes.Watch.TranscribeUrl), request, ct);
        return await ReadAsync<TranscribeResponse>(response, ct);
    }

    public async Task<CreateFightResponse> StartFightAsync(CreateFightRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Relative(ApiRoutes.Fights.Base), request, ct);
        return await ReadAsync<CreateFightResponse>(response, ct);
    }

    public async Task EndFightAsync(MatchId id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync(Relative(ApiRoutes.Fights.End(id)), null, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromAsync(response, ct);
        }
    }

    /// <summary>
    /// The roster, for suggesting a tag somebody has used before. A failure here is not worth showing: the tag can
    /// always be typed, so an empty list simply means no suggestions.
    /// </summary>
    public async Task<IReadOnlyList<FighterDto>> GetFightersAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(Relative(ApiRoutes.Fighters.Base), ct);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<IReadOnlyList<FighterDto>>(ct) ?? []
                : [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<FighterDto?> GetFighterAsync(FighterId tag, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(Relative(ApiRoutes.Fighters.ByTag(tag)), ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<FighterDto>(ct) : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<FighterStatsDto>> GetRosterAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Fighters.RosterUrl), ct);
        return await ReadAsync<IReadOnlyList<FighterStatsDto>>(response, ct);
    }

    public async Task<AnalysisResponse> GetAnalysisAsync(MatchId id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Fights.Analysis(id)), ct);
        return await ReadAsync<AnalysisResponse>(response, ct);
    }

    public async Task RetryAnalysisAsync(MatchId id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync(Relative(ApiRoutes.Fights.AnalysisRetry(id)), null, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromAsync(response, ct);
        }
    }

    public async Task<byte[]?> GetClipAsync(MatchId id, int index, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Fights.Clip(id, index)), ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    public async Task<IReadOnlyList<MatchDto>> GetMatchesAsync(MatchMode? mode = null, CancellationToken ct = default)
    {
        var path = mode is null ? ApiRoutes.Matches.Base : $"{ApiRoutes.Matches.Base}?mode={mode}";
        using var response = await http.GetAsync(Relative(path), ct);
        return await ReadAsync<IReadOnlyList<MatchDto>>(response, ct);
    }

    public async Task<MatchDto?> GetMatchAsync(MatchId id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Matches.ById(id)), ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<MatchDto>(ct) : null;
    }

    public async Task<IReadOnlyList<TurnDto>> GetMatchTurnsAsync(MatchId id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Matches.Turns(id)), ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IReadOnlyList<TurnDto>>(ct) ?? []
            : [];
    }

    /// <summary>
    /// The archived round comes back as bytes with its own content type, because the container is private and a
    /// replay cannot be a blob URL. The player wants base64 and a format name, so the conversion happens here rather
    /// than in a page: the format is whatever the server actually stored, not whatever was asked for at the time.
    /// </summary>
    public async Task<TtsAudioDto?> GetRoundAudioAsync(MatchId id, int roundIndex, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Audio.Round(id, roundIndex)), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var mime = response.Content.Headers.ContentType?.MediaType;
        var format = string.Equals(mime, "audio/mpeg", StringComparison.OrdinalIgnoreCase) ? "mp3" : "pcm";
        return bytes.Length == 0 ? null : new TtsAudioDto(Convert.ToBase64String(bytes), format);
    }

    public async Task DeleteMatchAsync(MatchId id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync(Relative(ApiRoutes.Matches.ById(id)), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromAsync(response, ct);
        }
    }

    public async Task<IReadOnlyList<LeaderboardRowDto>> GetLeaderboardAsync(MatchMode mode, CancellationToken ct = default)
    {
        var url = mode == MatchMode.Watch ? ApiRoutes.Leaderboard.WatchUrl : ApiRoutes.Leaderboard.FightUrl;
        using var response = await http.GetAsync(Relative(url), ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IReadOnlyList<LeaderboardRowDto>>(ct) ?? []
            : [];
    }

    public async Task<ProfileRecordDto?> GetProfileRecordAsync(ProfileId id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Profiles.Record(id)), ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ProfileRecordDto>(ct) : null;
    }

    public async Task<FighterProfileDto?> GetFighterProfileAsync(FighterId tag, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Relative(ApiRoutes.Fighters.Profile(tag)), ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<FighterProfileDto>(ct) : null;
    }

    public async Task<FighterDto> RenameFighterAsync(FighterId tag, string? displayName, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync(Relative(ApiRoutes.Fighters.ByTag(tag)), new RenameFighterRequest(displayName), ct);
        return await ReadAsync<FighterDto>(response, ct);
    }

    public async Task DeleteFighterAsync(FighterId tag, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync(Relative(ApiRoutes.Fighters.ByTag(tag)), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromAsync(response, ct);
        }
    }

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new ApiException((int)response.StatusCode, "The API returned an empty body.");
    }
}
