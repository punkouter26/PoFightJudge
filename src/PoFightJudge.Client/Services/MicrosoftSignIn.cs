using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace PoFightJudge.Client.Services;

/// <summary>
/// Starting a Microsoft sign-in, and saying what went wrong with one. Anybody's Microsoft account may sign in, and
/// a browser that is already signed in to somebody's employer will offer that account first without asking — which
/// fails, because an organisation has to allow an app before its people can use it.
/// </summary>
public static class MicrosoftSignIn
{
    /// <summary>
    /// Sends the browser to Microsoft, and always asks which account: the one the browser would have picked by
    /// itself is very often a work account this app cannot use.
    /// </summary>
    public static void Start(NavigationManager nav, string returnUrl)
    {
        ArgumentNullException.ThrowIfNull(nav);
        var request = new InteractiveRequestOptions { Interaction = InteractionType.SignIn, ReturnUrl = returnUrl };
        request.TryAddAdditionalParameter("prompt", "select_account");
        nav.NavigateToLogin("authentication/login", request);
    }

    /// <summary>
    /// Microsoft's error codes for "this account's organisation has not allowed this app": the app is not installed
    /// in that tenant, nobody there may consent to it, or an administrator has to.
    /// </summary>
    private static readonly string[] NotAllowedThere = ["AADSTS500011", "AADSTS65001", "AADSTS90094", "AADSTS700016", "AADSTS50020"];

    /// <summary>What happened, in a sentence somebody can act on. Microsoft's own text is kept for whoever needs it.</summary>
    public static string Explain(string? error) =>
        error is not null && NotAllowedThere.Any(code => error.Contains(code, StringComparison.Ordinal))
            ? "That account belongs to an organisation that has not allowed this app, so Microsoft would not sign it in. "
              + "Choose a personal Microsoft account, or a different work account."
            : "Microsoft could not sign you in. Try again, or choose a different account.";
}
