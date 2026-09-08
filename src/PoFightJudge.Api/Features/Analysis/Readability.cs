namespace PoFightJudge.Api.Features.Analysis;

/// <summary>Syllable counting and Flesch formulas for English.</summary>
public static class Readability
{
    public static int Syllables(string word)
    {
        var w = word.ToLowerInvariant().Trim('\'');
        if (w.Length == 0)
        {
            return 0;
        }

        if (w.Length <= 3)
        {
            return 1;
        }

        if (w.EndsWith("es", StringComparison.Ordinal) && !w.EndsWith("ses", StringComparison.Ordinal) && !w.EndsWith("zes", StringComparison.Ordinal))
        {
            w = w[..^2];
        }
        else if (w.EndsWith("ed", StringComparison.Ordinal) && !w.EndsWith("ted", StringComparison.Ordinal) && !w.EndsWith("ded", StringComparison.Ordinal))
        {
            w = w[..^2];
        }
        else if (w.EndsWith('e') && !w.EndsWith("le", StringComparison.Ordinal))
        {
            w = w[..^1];
        }

        var count = 0;
        var previousVowel = false;
        foreach (var c in w)
        {
            var vowel = "aeiouy".Contains(c);
            if (vowel && !previousVowel)
            {
                count++;
            }

            previousVowel = vowel;
        }

        return Math.Max(1, count);
    }

    public static double FleschReadingEase(int words, int sentences, int syllables) =>
        words == 0 || sentences == 0 ? 0 : Math.Round(206.835 - 1.015 * ((double)words / sentences) - 84.6 * ((double)syllables / words), 1);

    public static double FleschKincaidGrade(int words, int sentences, int syllables) =>
        words == 0 || sentences == 0 ? 0 : Math.Round(0.39 * ((double)words / sentences) + 11.8 * ((double)syllables / words) - 15.59, 1);
}
