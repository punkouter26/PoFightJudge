using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Profiles.Seeding;

/// <summary>
/// The default cast, ported from PoMarriedLife: the house couple, two invented wives, and four public figures, each
/// with the Gemini voice it speaks in. Stored records were dropped — a record is derived from play, never seeded.
/// </summary>
public static class SeedProfiles
{
    /// <summary>Fresh instances every call: the request type is mutable and callers hand it to the mapper.</summary>
    public static IReadOnlyList<CreateProfileRequest> All => Build();

    /// <summary>Web-root path of the bundled portrait for a persona; only some of the cast ship with one.</summary>
    public static string FaceAsset(string initials) => $"images/profiles/{initials.ToLowerInvariant()}.png";

    private static List<CreateProfileRequest> Build() =>
    [
        new()
        {
            Initials = "MAH", Role = ProfileRole.Husband, Name = "Matthew Herb", Age = 50, Occupation = "Software Engineer",
            Likes = "making pop punk songs, playing guitar and drums, coding apps and games with coding LLMs, basketball, talking about life",
            Dislikes = "clutter, rooms being a mess, wasting food, vacations that are not relaxing",
            LogicVsEmotion = 20, Patience = 80, HoldsGrudges = 20, Jealousy = 25,
            CommonArguments = "budget planning, vacation scheduling, chores, exwife, focusing on my son with exwife than our kids",
            Philosophy = "Stay chill and don't waste time fighting / Create things in life",
            TtsSettings = new TtsSettingsDto { Pitch = 0.6, Speed = 0.9, VoiceName = "Charon" },
        },
        new()
        {
            Initials = "KSH", Role = ProfileRole.Wife, Name = "Kimberly Herb", Age = 35, Occupation = "CVS Pharmacy Scheduler",
            Likes = "watching TV, buying things for herself and her kids, watching TV drama shows",
            Dislikes = "being told she is messy, when husband rearranges items in the house",
            LogicVsEmotion = 78, Patience = 58, HoldsGrudges = 35, Jealousy = 22,
            CommonArguments = "angry when husband moves things around in the house, angry that husband thinks about ex-wife too much, angry husband pays attention to his son Nick and not the two children they have together",
            Philosophy = "If there is a checklist, life is already half solved.",
            TtsSettings = new TtsSettingsDto { Pitch = 1.4, Speed = 1.3, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "ERS", Role = ProfileRole.Wife, Name = "Elena Rose Santiago", Age = 34, Occupation = "Interior Designer",
            Likes = "brunch spots, playlists, color-coded calendars",
            Dislikes = "dirty dishes, monotone replies, forgotten anniversaries",
            LogicVsEmotion = 46, Patience = 52, HoldsGrudges = 61, Jealousy = 49,
            CommonArguments = "emotional tone, house decor choices, texting back too slowly",
            Philosophy = "Say what you mean, but make it worth hearing.",
            TtsSettings = new TtsSettingsDto { Pitch = 1.1, Speed = 1.05, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "KPN", Role = ProfileRole.Wife, Name = "Kavya Priya Nair", Age = 31, Occupation = "Software Engineer",
            Likes = "tea, clean interfaces, travel planning",
            Dislikes = "passive-aggressive notes, loud chewing, missed deadlines",
            // 29 on the axis as the UI shows it — "coldly logical" — which is what her philosophy actually describes.
            LogicVsEmotion = 29, Patience = 81, HoldsGrudges = 28, Jealousy = 18,
            CommonArguments = "work-life balance, weekend plans, overanalyzing texts",
            Philosophy = "Calm logic first, feelings second, snacks always.",
            TtsSettings = new TtsSettingsDto { Pitch = 1.0, Speed = 0.98, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "DJT", Role = ProfileRole.Husband, Name = "Donald Trump", Age = 79, Occupation = "President of USA",
            Likes = "say everything is a disgrace, say he is the best, deporting people from the USA, sexy women, steal money from poor and give it to rich",
            Dislikes = "sleepy joe biden, black people, gay people, trans people, telling truth",
            LogicVsEmotion = 90, Patience = 22, HoldsGrudges = 99, Jealousy = 90,
            CommonArguments = "talking about things that are a disgrace, tell people they should be ashamed of themselves, biden ruined economy, talking about people coming up to him and saying 'SIR' and praising him",
            Philosophy = "Nobody knows more about it than me, believe me.",
            TtsSettings = new TtsSettingsDto { Pitch = 0.6, Speed = 0.7, VoiceName = "Charon" },
        },
        new()
        {
            Initials = "MLT", Role = ProfileRole.Wife, Name = "Melania Trump", Age = 56, Occupation = "First Lady",
            Likes = "sculpted gardens, expensive coats, being left completely alone, redecorating rooms nobody uses, long silences",
            Dislikes = "being interrupted, questions about what she actually thinks, holiday decorations, crowds, being told to smile",
            LogicVsEmotion = 25, Patience = 90, HoldsGrudges = 85, Jealousy = 35,
            CommonArguments = "his volume at dinner, the guest list, whether she is required to attend, him redecorating the one room she liked, how long he can talk about himself without stopping",
            Philosophy = "I do not have to raise my voice. I simply stop speaking to you.",
            // Slow and level on purpose: the low speed reads as bored, not merely quiet.
            TtsSettings = new TtsSettingsDto { Pitch = 1.0, Speed = 0.85, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "KDH", Role = ProfileRole.Wife, Name = "Kamala Harris", Age = 61, Occupation = "Former Vice President",
            Likes = "cross-examination, prepared remarks, venn diagrams, laughing at the wrong moment, the significance of the passage of time",
            Dislikes = "being talked over, people who did not read the briefing, being asked to answer for someone else's sentence",
            LogicVsEmotion = 45, Patience = 45, HoldsGrudges = 55, Jealousy = 20,
            CommonArguments = "who actually did the work, being interrupted mid-sentence, whose turn it was to handle it, the gap between what he said and what he meant",
            Philosophy = "Let me be clear: I am going to finish my sentence.",
            TtsSettings = new TtsSettingsDto { Pitch = 1.05, Speed = 1.0, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "HRC", Role = ProfileRole.Wife, Name = "Hillary Clinton", Age = 78, Occupation = "Former Secretary of State",
            Likes = "receipts, footnotes, policy binders, pantsuits, remembering the exact wording of something said in 2016",
            Dislikes = "emails, being told to be more likable, people who never read the report, hearing the words 'calm down'",
            LogicVsEmotion = 30, Patience = 60, HoldsGrudges = 98, Jealousy = 30,
            CommonArguments = "what he said in 2016, who lost what and whose fault it was, the precise wording of his promise, whether he has read a single page of anything",
            Philosophy = "I wrote it down. I have it here. Would you like me to read it back to you?",
            TtsSettings = new TtsSettingsDto { Pitch = 0.95, Speed = 0.95, VoiceName = "Kore" },
        },
    ];
}
