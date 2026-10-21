namespace PoFightJudge.Client.Services;

/// <summary>
/// The browser's name for a microphone that would not open, turned into what to do about it. One table for every
/// page that opens one (the mic check, a live fight, a spoken 1P turn): a refused permission, a missing device, a
/// device somebody else holds and a page that did not load its audio code are different problems, and the advice
/// for one is useless for another.
/// </summary>
public static class MicError
{
    public static string Describe(string error) => error switch
    {
        "NotAllowedError" or "SecurityError" =>
            "The browser is blocking the microphone. Allow it for this site in the address bar, then try again.",
        "NotFoundError" or "DevicesNotFoundError" =>
            "No microphone was found. Plug one in, or check that the one you have is not switched off.",
        "NotReadableError" or "TrackStartError" =>
            "Something else is holding the microphone. Close whatever is using it and try again.",
        // Chrome on Windows, mostly: the device was found and allowed but would not start (another app has it in
        // exclusive mode, or it glitched). Trying again after freeing it is what works.
        "AbortError" =>
            "The microphone would not start. Close any other app or call that is using it, or unplug it and plug it back in, then try again.",
        // Not the microphone at all: the page could not fetch its audio code, usually because the connection dropped.
        "AudioLoadError" =>
            "The page could not load its audio code. Reload the page and try again.",
        "NotSupportedError" =>
            "This browser will not give a page the microphone. Try Chrome or Edge over https.",
        _ => $"The microphone could not be opened ({error}).",
    };
}
