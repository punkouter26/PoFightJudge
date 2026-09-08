using System.Text;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>
/// Salvages a JSON document that was cut off mid-flight — a model that hit its output ceiling.
/// The report is dozens of fields per player, so losing the tail of one list is far better than losing all of it.
/// </summary>
public static class JsonRepair
{
    /// <summary>
    /// Trims back to the last position where a value had finished, then closes whatever is still open.
    /// Returns <c>null</c> when there is no such position — a fragment with nothing complete in it is not worth guessing at.
    /// </summary>
    public static string? TryClose(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var cut = LastCompleteValue(json);
        if (cut <= 0)
        {
            return null;
        }

        var head = json[..cut].TrimEnd();
        while (head.Length > 0 && head[^1] == ',')
        {
            head = head[..^1].TrimEnd();
        }

        var open = OpenContainers(head);
        if (head.Length == 0 || head[0] != '{')
        {
            return null;
        }

        var sb = new StringBuilder(head);
        for (var i = open.Count - 1; i >= 0; i--)
        {
            sb.Append(open[i] == '{' ? '}' : ']');
        }

        return sb.ToString();
    }

    /// <summary>Index just past the last point where a value was known to be complete: a separator or a closing bracket.</summary>
    private static int LastCompleteValue(string json)
    {
        var cut = 0;
        var inString = false;
        var escaped = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case ',':
                    cut = i;
                    break;
                case '}':
                case ']':
                    cut = i + 1;
                    break;
                default:
                    break;
            }
        }

        return cut;
    }

    /// <summary>The containers still open at the end of a (string-balanced) prefix, outermost first.</summary>
    private static List<char> OpenContainers(string json)
    {
        var open = new List<char>();
        var inString = false;
        var escaped = false;
        foreach (var c in json)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                    open.Add(c);
                    break;
                case '}':
                case ']':
                    if (open.Count > 0)
                    {
                        open.RemoveAt(open.Count - 1);
                    }

                    break;
                default:
                    break;
            }
        }

        return open;
    }
}
