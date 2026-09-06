using System.Net.Http.Json;
using PoMarriedFight.Shared;
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
}

public sealed class ApiClient(HttpClient http) : IApiClient
{
    public async Task<AuthMeDto> GetMeAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<AuthMeDto>(ApiRoutes.Auth.Me, ct) ?? AuthMeDto.Anonymous;

    public async Task<AuthMeDto> GuestSignInAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync(new Uri(ApiRoutes.Auth.Guest, UriKind.Relative), null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthMeDto>(ct) ?? AuthMeDto.Anonymous;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync(new Uri(ApiRoutes.Auth.Logout, UriKind.Relative), null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<FeatureFlagsDto> GetFeaturesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<FeatureFlagsDto>(ApiRoutes.Features.Url, ct) ?? new FeatureFlagsDto(false, false, false, false);

    public Task<HealthReportDto?> GetHealthDetailsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<HealthReportDto>(ApiRoutes.Health.DetailsUrl, ct);
}
