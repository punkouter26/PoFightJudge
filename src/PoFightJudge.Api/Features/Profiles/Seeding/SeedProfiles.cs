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
            IsIntrovert = false, IsStubborn = true, IsSpontaneous = true, IsSarcastic = true, IsWorkaholic = false, IsPackRat = false,
            LoveLanguage = LoveLanguage.QualityTime, AttachmentStyle = AttachmentStyle.Secure, StressResponse = StressResponse.Fight,
            LogicVsEmotion = 20, Punctuality = 87, InLawAffinity = 90, ScreenTime = 80, Jealousy = 25,
            IsMessy = 10, SpendsMoneyFreely = 45, HoldsGrudges = 20, Patience = 80,
            CommonArguments = "budget planning, vacation scheduling, chores, exwife, focusing on my son with exwife than our kids",
            Philosophy = "Stay chill and don't waste time fighting / Create things in life",
            TtsSettings = new TtsSettingsDto { Pitch = 0.6, Speed = 0.9, VoiceName = "Charon" },
        },
        new()
        {
            Initials = "KSH", Role = ProfileRole.Wife, Name = "Kimberly Herb", Age = 35, Occupation = "CVS Pharmacy Scheduler",
            Likes = "watching TV, buying things for herself and her kids, watching TV drama shows",
            Dislikes = "being told she is messy, when husband rearranges items in the house",
            IsIntrovert = true, IsStubborn = true, IsSpontaneous = true, IsSarcastic = false, IsWorkaholic = false, IsPackRat = true,
            LoveLanguage = LoveLanguage.ActsOfService, AttachmentStyle = AttachmentStyle.Anxious, StressResponse = StressResponse.Fight,
            LogicVsEmotion = 78, Punctuality = 60, InLawAffinity = 55, ScreenTime = 38, Jealousy = 22,
            IsMessy = 25, SpendsMoneyFreely = 40, HoldsGrudges = 35, Patience = 58,
            CommonArguments = "angry when husband moves things around in the house, angry that husband thinks about ex-wife too much, angry husband pays attention to his son Nick and not the two children they have together",
            Philosophy = "If there is a checklist, life is already half solved.",
            TtsSettings = new TtsSettingsDto { Pitch = 1.4, Speed = 1.3, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "ERS", Role = ProfileRole.Wife, Name = "Elena Rose Santiago", Age = 34, Occupation = "Interior Designer",
            Likes = "brunch spots, playlists, color-coded calendars",
            Dislikes = "dirty dishes, monotone replies, forgotten anniversaries",
            IsIntrovert = false, IsStubborn = true, IsSpontaneous = true, IsSarcastic = true, IsWorkaholic = false, IsPackRat = true,
            LoveLanguage = LoveLanguage.WordsOfAffirmation, AttachmentStyle = AttachmentStyle.Anxious, StressResponse = StressResponse.Fawn,
            LogicVsEmotion = 46, Punctuality = 72, InLawAffinity = 84, ScreenTime = 57, Jealousy = 49,
            IsMessy = 44, SpendsMoneyFreely = 68, HoldsGrudges = 61, Patience = 52,
            CommonArguments = "emotional tone, house decor choices, texting back too slowly",
            Philosophy = "Say what you mean, but make it worth hearing.",
            TtsSettings = new TtsSettingsDto { Pitch = 1.1, Speed = 1.05, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "KPN", Role = ProfileRole.Wife, Name = "Kavya Priya Nair", Age = 31, Occupation = "Software Engineer",
            Likes = "tea, clean interfaces, travel planning",
            Dislikes = "passive-aggressive notes, loud chewing, missed deadlines",
            IsIntrovert = true, IsStubborn = false, IsSpontaneous = false, IsSarcastic = false, IsWorkaholic = true, IsPackRat = false,
            LoveLanguage = LoveLanguage.QualityTime, AttachmentStyle = AttachmentStyle.Avoidant, StressResponse = StressResponse.Flight,
            // 29 on the axis as the UI shows it — "coldly logical" — which is what her philosophy actually describes.
            LogicVsEmotion = 29, Punctuality = 89, InLawAffinity = 63, ScreenTime = 64, Jealousy = 18,
            IsMessy = 19, SpendsMoneyFreely = 36, HoldsGrudges = 28, Patience = 81,
            CommonArguments = "work-life balance, weekend plans, overanalyzing texts",
            Philosophy = "Calm logic first, feelings second, snacks always.",
            TtsSettings = new TtsSettingsDto { Pitch = 1.0, Speed = 0.98, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "DJT", Role = ProfileRole.Husband, Name = "Donald Trump", Age = 79, Occupation = "President of USA",
            Likes = "say everything is a disgrace, say he is the best, deporting people from the USA, sexy women, steal money from poor and give it to rich",
            Dislikes = "sleepy joe biden, black people, gay people, trans people, telling truth",
            IsIntrovert = false, IsStubborn = true, IsSpontaneous = true, IsSarcastic = false, IsWorkaholic = false, IsPackRat = false,
            LoveLanguage = LoveLanguage.ReceivingGifts, AttachmentStyle = AttachmentStyle.Anxious, StressResponse = StressResponse.Fight,
            LogicVsEmotion = 90, Punctuality = 50, InLawAffinity = 50, ScreenTime = 90, Jealousy = 90,
            IsMessy = 70, SpendsMoneyFreely = 99, HoldsGrudges = 99, Patience = 22,
            CommonArguments = "talking about things that are a disgrace, tell people they should be ashamed of themselves, biden ruined economy, talking about people coming up to him and saying 'SIR' and praising him",
            Philosophy = "Nobody knows more about it than me, believe me.",
            TtsSettings = new TtsSettingsDto { Pitch = 0.6, Speed = 0.7, VoiceName = "Charon" },
        },
        new()
        {
            Initials = "MLT", Role = ProfileRole.Wife, Name = "Melania Trump", Age = 56, Occupation = "First Lady",
            Likes = "sculpted gardens, expensive coats, being left completely alone, redecorating rooms nobody uses, long silences",
            Dislikes = "being interrupted, questions about what she actually thinks, holiday decorations, crowds, being told to smile",
            IsIntrovert = true, IsStubborn = true, IsSpontaneous = false, IsSarcastic = true, IsWorkaholic = false, IsPackRat = false,
            LoveLanguage = LoveLanguage.ReceivingGifts, AttachmentStyle = AttachmentStyle.Avoidant, StressResponse = StressResponse.Freeze,
            LogicVsEmotion = 25, Punctuality = 85, InLawAffinity = 15, ScreenTime = 40, Jealousy = 35,
            IsMessy = 5, SpendsMoneyFreely = 95, HoldsGrudges = 85, Patience = 90,
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
            IsIntrovert = false, IsStubborn = true, IsSpontaneous = false, IsSarcastic = true, IsWorkaholic = true, IsPackRat = false,
            LoveLanguage = LoveLanguage.WordsOfAffirmation, AttachmentStyle = AttachmentStyle.Secure, StressResponse = StressResponse.Fight,
            LogicVsEmotion = 45, Punctuality = 80, InLawAffinity = 60, ScreenTime = 55, Jealousy = 20,
            IsMessy = 20, SpendsMoneyFreely = 50, HoldsGrudges = 55, Patience = 45,
            CommonArguments = "who actually did the work, being interrupted mid-sentence, whose turn it was to handle it, the gap between what he said and what he meant",
            Philosophy = "Let me be clear: I am going to finish my sentence.",
            TtsSettings = new TtsSettingsDto { Pitch = 1.05, Speed = 1.0, VoiceName = "Kore" },
        },
        new()
        {
            Initials = "HRC", Role = ProfileRole.Wife, Name = "Hillary Clinton", Age = 78, Occupation = "Former Secretary of State",
            Likes = "receipts, footnotes, policy binders, pantsuits, remembering the exact wording of something said in 2016",
            Dislikes = "emails, being told to be more likable, people who never read the report, hearing the words 'calm down'",
            IsIntrovert = false, IsStubborn = true, IsSpontaneous = false, IsSarcastic = true, IsWorkaholic = true, IsPackRat = true,
            LoveLanguage = LoveLanguage.ActsOfService, AttachmentStyle = AttachmentStyle.Avoidant, StressResponse = StressResponse.Fight,
            LogicVsEmotion = 30, Punctuality = 92, InLawAffinity = 45, ScreenTime = 45, Jealousy = 30,
            IsMessy = 15, SpendsMoneyFreely = 40, HoldsGrudges = 98, Patience = 60,
            CommonArguments = "what he said in 2016, who lost what and whose fault it was, the precise wording of his promise, whether he has read a single page of anything",
            Philosophy = "I wrote it down. I have it here. Would you like me to read it back to you?",
            TtsSettings = new TtsSettingsDto { Pitch = 0.95, Speed = 0.95, VoiceName = "Kore" },
        },
    ];
}
