using System.Globalization;

namespace PoMarriedFight.TestSupport;

/// <summary>
/// Fighter tags for tests that write to real storage. A tag is at most three characters, which is 46,656 of them —
/// so they are handed out in sequence from a random start rather than picked at random each time. Two tests in one
/// run can never collide, and a run that starts somewhere else in the space is very unlikely to meet what an
/// earlier run left in the table.
/// </summary>
/// <remarks>
/// This exists because the old generator picked from eighty-nine tags, and tests that assert things like "a tag
/// nobody has argued under cannot be renamed" were failing roughly one run in three — the table outlives the run,
/// so the tag had argued, in a run an hour ago.
/// </remarks>
public static class TestTags
{
    private const int Space = 36 * 36 * 36;

    private static int _next = Random.Shared.Next(Space);

    /// <summary>The next tag, in base 36 and three characters wide.</summary>
    public static string Next()
    {
        var value = ((uint)Interlocked.Increment(ref _next)) % Space;
        return string.Create(3, value, static (span, v) =>
        {
            const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            for (var i = 2; i >= 0; i--)
            {
                span[i] = Alphabet[(int)(v % 36)];
                v /= 36;
            }
        });
    }

    /// <summary>The same idea for a persona: initials nobody else in the run is using.</summary>
    public static string NextInitials() => Next();
}
