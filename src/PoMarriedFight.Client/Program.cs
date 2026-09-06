using Blazored.LocalStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PoMarriedFight.Client;
using PoMarriedFight.Client.Services;
using Radzen;
using Toolbelt.Blazor.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddRadzenComponents();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddHotKeys2();
builder.Services.AddScoped<ThemeInterop>();
builder.Services.AddScoped<IApiClient, ApiClient>();

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
    builder.Services.AddScoped<ApiAuthStateProvider>();
    builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ApiAuthStateProvider>());
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
}
