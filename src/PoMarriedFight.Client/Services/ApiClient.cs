using System.Net.Http.Headers;
using System.Net.Http.Json;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Client.Services;

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

public sealed class ApiClient(HttpClient http) : IApiClient
{
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
        await http.GetFromJsonAsync<FeatureFlagsDto>(ApiRoutes.Features.Url, ct) ?? new FeatureFlagsDto(false, false, false, false);

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
