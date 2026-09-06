using System.Net.Http.Json;
using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PoMarriedFight.Client;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Models;
using Radzen;
using Toolbelt.Blazor.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddRadzenComponents();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddHotKeys2();

// Environment split (SPEC §2): Production = Microsoft Entra ID via MSAL, API calls to our own origin carry the access
// token. Everything else = the guest cookie / FakeAuth header the API understands; auth state is whatever /auth/me says.
if (builder.HostEnvironment.IsProduction())
{
    builder.Services.AddMsalAuthentication(options =>
    {
        builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
        if (builder.Configuration["AzureAd:Scope"] is { Length: > 0 } scope)
        {
            options.ProviderOptions.DefaultAccessTokenScopes.Add(scope);
        }

        options.ProviderOptions.LoginMode = "redirect";
    });
    builder.Services.AddScoped<BaseAddressAuthorizationMessageHandler>();
    builder.Services.AddHttpClient(HttpClients.Api, c => c.BaseAddress = baseAddress)
        .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>();
    builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClients.Api));
}
else
{
    builder.Services.AddHttpClient(HttpClients.Api, c => c.BaseAddress = baseAddress);
    builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClients.Api));
    builder.Services.AddScoped<AuthenticationStateProvider, ApiMeAuthStateProvider>();
}

// No anonymous browsing: every page needs a signed-in (or guest) user unless it opts out.
builder.Services.AddAuthorizationCore(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

await builder.Build().RunAsync();

namespace PoMarriedFight.Client
{
    /// <summary>Named HttpClient registrations; the one place the name lives.</summary>
    public static class HttpClients
    {
        public const string Api = "PoMarriedFight.Api";
    }

    /// <summary>
    /// Development/Test auth state: asks the API who the cookie (or FakeAuth header) says we are. T09 replaces this
    /// with the ApiClient-backed provider once the typed client exists; the contract (/auth/me → claims) is identical.
    /// </summary>
    file sealed class ApiMeAuthStateProvider(HttpClient http) : AuthenticationStateProvider
    {
        private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var me = await http.GetFromJsonAsync<AuthMeDto>(ApiRoutes.Auth.Me);
                if (me is not { Authenticated: true })
                {
                    return Anonymous;
                }

                var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, me.UserId ?? string.Empty),
                new Claim(ClaimTypes.Name, me.Name ?? me.UserId ?? "user"),
            ], me.AuthType ?? "api");
                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
            catch (HttpRequestException)
            {
                return Anonymous;
            }
        }
    }
}
