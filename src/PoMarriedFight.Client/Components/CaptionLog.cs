using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Client.Components;

/// <summary>
/// The running caption of a fight. A speaker's line arrives in pieces and is rewritten as it grows, so the log
/// replaces the line still being spoken rather than appending each fragment; a final caption closes that line and
/// the next one starts fresh.
/// </summary>
public sealed class CaptionLog
{
    /// <summary>Older lines fall off the top: a long fight would otherwise grow the page without limit.</summary>
    public const int MaxLines = 120;

    private readonly List<CaptionDto> _lines = [];

    public IReadOnlyList<CaptionDto> Lines => _lines;

    public void Add(CaptionDto caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        if (string.IsNullOrWhiteSpace(caption.Text))
        {
            return;
        }

        // Still being spoken by whoever spoke last: this is the same line, longer.
        if (_lines.Count > 0 && _lines[^1] is { Final: false } open && open.Speaker == caption.Speaker)
        {
            _lines[^1] = caption;
        }
        else
        {
            _lines.Add(caption);
        }

        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
    }

    public void Clear() => _lines.Clear();
}
