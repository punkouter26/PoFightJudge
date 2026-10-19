using System.Globalization;
using System.Text;
using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// The .NET side of <c>PoSave</c>: hands bytes the app already has to the browser as a file. Pages fetch through the
/// API client first, because the recording is in a private container and a link element would not carry the caller's
/// credentials in Production.
/// </summary>
/// <summary>
/// A ruling as a story-sized picture (1080 by 1920): what it was about, who took it, how the room split, what was said
/// about it, and the worst slip. Drawn by <c>PoSave.story</c>; built by the page that has the report.
/// </summary>
public sealed record StoryCard(
    string Headline,
    string Topic,
    string One,
    string Two,
    double OneShare,
    double TwoShare,
    string Summary,
    string? Fallacy,
    string? FallacyQuote,
    bool Rehearsal,
    string When,
    string FileName);

public sealed class SaveInterop(IJSRuntime js)
{
    public const string File = "PoSave.file";
    public const string Story = "PoSave.story";

    /// <summary>
    /// Draws the card and hands it to the share sheet, or saves it where there is none. Says which: <c>shared</c>,
    /// <c>saved</c>, <c>cancelled</c> (somebody closed the sheet) or <c>failed</c>.
    /// </summary>
    public async Task<string> StoryAsync(StoryCard card, CancellationToken ct = default)
    {
        try
        {
            return await js.InvokeAsync<string>(Story, ct, card);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            return "failed";
        }
    }

    /// <summary>Saves one file. Returns false when the browser refused, so a page can say so rather than look broken.</summary>
    public async Task<bool> FileAsync(string name, string mime, byte[] bytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            return false;
        }

        try
        {
            return await js.InvokeAsync<bool>(File, ct, name, mime, Convert.ToBase64String(bytes));
        }
        catch (JSException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // No JS runtime — prerendering, or a test host without one.
            return false;
        }
    }

    /// <summary>A file name built from free text: <c>the-bins-best-line.wav</c>. Either part may be empty.</summary>
    public static string FileName(string subject, string suffix, string extension)
    {
        var name = Slug(subject);
        if (name.Length == 0)
        {
            name = "argument";
        }

        var tail = Slug(suffix);
        return tail.Length == 0 ? $"{name}.{extension}" : $"{name}-{tail}.{extension}";
    }

    /// <summary>
    /// Free text as one path-safe word. Anything that is not a letter or a digit becomes a dash, because a topic is
    /// whatever somebody typed and a saved file lands in a real folder on a real filesystem.
    /// </summary>
    public static string Slug(string text)
    {
        var slug = new StringBuilder(text.Length);
        var dash = false;
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                dash = false;
            }
            else if (!dash && slug.Length > 0)
            {
                slug.Append('-');
                dash = true;
            }
        }

        var name = slug.ToString().Trim('-');

        // Long enough to be recognisable, short enough that two of them still fit a filesystem's name limit.
        return name.Length > 48 ? name[..48].TrimEnd('-') : name;
    }
}
