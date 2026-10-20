using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace PoFightJudge.Client.Services;

/// <summary>
/// Development's API handler, where two doors are open: a Microsoft sign-in's access token goes on the request when
/// there is one, and when there is not (a guest) the request goes as it is and the guest cookie speaks for it.
/// Production uses <see cref="BaseAddressAuthorizationMessageHandler"/>, which sends somebody without a token to sign in.
/// </summary>
public sealed class OptionalTokenHandler(IAccessTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null && (await tokens.RequestAccessToken()).TryGetToken(out var token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
