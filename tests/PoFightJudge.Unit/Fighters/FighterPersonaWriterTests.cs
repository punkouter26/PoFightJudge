using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Fighters;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using PoFightJudge.TestSupport;
using PoFightJudge.Unit.Profiles;

namespace PoFightJudge.Unit.Fighters;

/// <summary>
/// Somebody who has argued in 2P becomes a persona the CPU and 1P channels can put in a seat. It is read from what
/// they actually said, rewritten after every fight, and it is theirs alone: the seeded cast is never written over.
/// </summary>
public sealed class FighterPersonaWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryFighterRepository _fighters = new();
    private readonly InMemoryFighterResultRepository _results = new();
    private readonly InMemoryFighterWordsRepository _words = new();
    private readonly InMemoryProfileRepository _profiles = new();

    private FighterPersonaWriter Writer(IGeminiText? gemini = null) => new(
        _fighters,
        _results,
        _words,
        _profiles,
        gemini ?? new FakeGeminiText(new AiLatencyTracker(), TimeSpan.Zero),
        GeminiModelOptions.Defaults,
        NullLogger<FighterPersonaWriter>.Instance);

    /// <summary>A fight KKK and LLL just had, as the pipeline hands it over once the judge has ruled.</summary>
    private async Task<(MatchDto Match, AnalysisReportDto Report)> FoughtAsync()
    {
        await _fighters.EnsureAsync(FighterId.From("KKK"), Now, ProfileRole.Husband);
        await _fighters.EnsureAsync(FighterId.From("LLL"), Now, ProfileRole.Wife);

        var match = new MatchDto(
            MatchId.New(), "user-1", MatchMode.Fight, Now.AddMinutes(-5), Now,
            "love trump", MatchSide.Human("KKK"), MatchSide.Human("LLL"),
            SessionPhase.Done, SessionStatus.Ready, "KKK", "KKK took it.", IsFake: false);

        var words = new List<MappedWord>();
        var t = 0.0;
        foreach (var w in "Trump loves me Trump is the greatest ever everyone needs to be like Trump".Split(' '))
        {
            words.Add(new MappedWord(w, Speaker.Player1, t, t + 0.4));
            t += 0.5;
        }

        foreach (var w in "That is not an argument that is a poster".Split(' '))
        {
            words.Add(new MappedWord(w, Speaker.Player2, t, t + 0.4));
            t += 0.5;
        }

        var transcript = new MappedTranscript(words, Flagged: false, Note: string.Empty);
        var turns = new[]
        {
            new TurnDto(match.Id, 0, Speaker.Player1, TurnKind.Talk, string.Empty) { StartSeconds = 0, EndSeconds = 7 },
            new TurnDto(match.Id, 1, Speaker.Player2, TurnKind.Talk, string.Empty) { StartSeconds = 7, EndSeconds = 12 },
        };

        var report = new AnalysisReportDto(
            match.Id,
            match.Topic,
            new PlayerReportDto("KKK", SpeechMetrics.Compute(Speaker.Player1, transcript, turns), Assessment("adoring", "Everyone needs to be like Trump")),
            new PlayerReportDto("LLL", SpeechMetrics.Compute(Speaker.Player2, transcript, turns), Assessment("dry", "That is a poster")),
            new OverallVerdictDto(Speaker.Player1, Speaker.Player2, Speaker.Player1, ["louder"], "One of them meant it."),
            match.Verdict,
            SpeakerMappingFlagged: false,
            SpeakerMappingNote: string.Empty,
            AnalysisSeconds: 3,
            GeneratedAt: Now,
            Highlights: []);

        await SaidAsync(match, "KKK", Speaker.Player1, transcript, Now);
        await SaidAsync(match, "LLL", Speaker.Player2, transcript, Now);

        return (match, report);
    }

    /// <summary>What the pipeline stores for one side once the fight is read: their own words, in order.</summary>
    private Task SaidAsync(MatchDto match, string tag, Speaker side, MappedTranscript transcript, DateTimeOffset at) =>
        _words.SaveAsync(SpokenDebate.From(tag, match.Id, at, MatchMode.Fight, transcript.For(side).OrderBy(w => w.Start).Select(w => w.Text)));

    private static PlayerAssessmentDto Assessment(string tone, string quote) => new(
        "B2", "clear enough", 1, ["a slip"], 6, 7, 8, [new FallacyDto("Appeal to authority", quote)], 5, 6, 7, 80, [],
        new EmotionProfileDto(10, 40, 30, 5, 0, 15), [], quote, [tone, "loud", "fast"],
        "steady", "even", "varied", 7, 6, 4, 5, quote, "worst", ["one", "two", "three"]);

    [Fact]
    public async Task Both_people_get_a_persona_in_the_cast_under_their_own_initials_and_the_role_they_chose()
    {
        var (match, report) = await FoughtAsync();

        await Writer().WriteAsync(match, report, CancellationToken.None);

        var kkk = await _profiles.GetByIdAsync(ProfileId.From("KKK"));
        kkk.Should().NotBeNull("KKK can now be picked for a CPU or 1P argument");
        kkk!.Role.Should().Be(ProfileRole.Husband, "the role is the one chosen at 2P setup");
        kkk.FromFights.Should().BeTrue("so the app knows this one is read from fights, not authored");
        kkk.Name.Should().Be("KKK", "a real person is not given an invented name");
        kkk.Likes.Should().NotBeNullOrWhiteSpace();
        kkk.Philosophy.Should().NotBeNullOrWhiteSpace();

        var lll = await _profiles.GetByIdAsync(ProfileId.From("LLL"));
        lll!.Role.Should().Be(ProfileRole.Wife);
        lll.FromFights.Should().BeTrue();
    }

    [Fact]
    public async Task A_seeded_cast_member_who_happens_to_share_the_initials_is_never_written_over()
    {
        var (match, report) = await FoughtAsync();
        await _profiles.UpsertAsync(ProfileTests.FullRequest("KKK", ProfileRole.Husband).ToDomain());

        await Writer().WriteAsync(match, report, CancellationToken.None);

        var kkk = await _profiles.GetByIdAsync(ProfileId.From("KKK"));
        kkk!.FromFights.Should().BeFalse("the authored cast never changes");
        kkk.Likes.Should().Be("tea", "not a field of it was touched");
        (await _profiles.GetByIdAsync(ProfileId.From("LLL")))!.FromFights.Should().BeTrue("the other seat is still written");
    }

    [Fact]
    public async Task The_next_fight_rewrites_the_persona_but_keeps_their_face_and_takes_the_role_they_chose_this_time()
    {
        var (match, report) = await FoughtAsync();
        await Writer().WriteAsync(match, report, CancellationToken.None);

        var first = await _profiles.GetByIdAsync(ProfileId.From("KKK"));
        first!.UpdateFacePic("faces/KKK.png");
        await _profiles.UpsertAsync(first);
        await _fighters.EnsureAsync(FighterId.From("KKK"), Now.AddDays(1), ProfileRole.Wife);

        await Writer().WriteAsync(match, report, CancellationToken.None);

        var again = await _profiles.GetByIdAsync(ProfileId.From("KKK"));
        again!.FacePic.Should().Be("faces/KKK.png", "a photo somebody added is not something a fight should lose");
        again.Role.Should().Be(ProfileRole.Wife, "the seat they chose this time is the seat they argue from");
        again.FromFights.Should().BeTrue();
    }

    [Fact]
    public async Task A_model_that_fails_costs_them_the_persona_and_nothing_else()
    {
        var (match, report) = await FoughtAsync();
        var gemini = Substitute.For<IGeminiText>();
        gemini.GenerateAsync(Arg.Any<GeminiTextRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("503 from upstream"));

        var act = () => Writer(gemini).WriteAsync(match, report, CancellationToken.None);

        await act.Should().NotThrowAsync("the analysis is already ready; a persona is a bonus, not a condition");
        (await _profiles.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_model_is_told_what_they_actually_said_and_what_the_judge_made_of_it()
    {
        var (_, report) = await FoughtAsync();
        var fighter = (await _fighters.GetAsync(FighterId.From("KKK")))!;
        var style = StyleProfileBuilder.Build("KKK", []);
        var corpus = SpokenCorpus.From(await _words.ListAsync(FighterId.From("KKK")));

        var prompt = FighterPersonaWriter.BuildPrompt(fighter, style, report.Player1.Assessment, corpus, attempt: 0);

        prompt.User.Should().Contain("Trump loves me", "their own words are the evidence");
        prompt.User.Should().Contain("Everyone needs to be like Trump", "so is the judge's pick of their best line");
        prompt.User.Should().Contain("husband", "the persona is written for the seat they chose");
        prompt.User.Should().Contain("KKK");
        prompt.System.Should().ContainEquivalentOf("real person", "this is not the invent-a-character prompt");
    }

    [Fact]
    public async Task Everything_they_have_ever_said_goes_to_the_model_not_just_the_argument_just_had()
    {
        var (match, report) = await FoughtAsync();
        var kkk = FighterId.From("KKK");

        // Two nights they argued before this one, one in each engine: both are theirs, so both are evidence.
        await _words.SaveAsync(SpokenDebate.From("KKK", MatchId.New(), Now.AddDays(-9), MatchMode.Watch, ["Look, the thing is, the bins were your job"]));
        await _words.SaveAsync(SpokenDebate.From("KKK", MatchId.New(), Now.AddDays(-2), MatchMode.Fight, ["Look, the thing is, you never listen to a word"]));

        var fighter = (await _fighters.GetAsync(kkk))!;
        var corpus = SpokenCorpus.From(await _words.ListAsync(kkk));
        var prompt = FighterPersonaWriter.BuildPrompt(fighter, StyleProfileBuilder.Build("KKK", []), report.Player1.Assessment, corpus, attempt: 0);

        corpus.Debates.Should().Be(3, "every debate they have spoken in, this one included");
        prompt.User.Should().Contain("the bins were your job", "what they said nine days ago is still how they argue");
        prompt.User.Should().Contain("you never listen to a word");
        prompt.User.Should().Contain("Trump loves me", "and tonight is in there too");
        prompt.User.Should().Contain("the one just judged", "the model is told which night was the fight it is reading");

        var bins = prompt.User.IndexOf("the bins were your job", StringComparison.Ordinal);
        var tonight = prompt.User.IndexOf("Trump loves me", StringComparison.Ordinal);
        bins.Should().BeLessThan(tonight, "oldest first, so it reads as somebody changing over time");

        // And it is not just prompt-building: the persona itself is written from all of it.
        await Writer().WriteAsync(match, report, CancellationToken.None);
        (await _profiles.GetByIdAsync(ProfileId.From("KKK")))!.FromFights.Should().BeTrue();
    }

    [Fact]
    public async Task Somebody_whose_words_were_never_transcribed_still_gets_a_persona_from_the_judges_read()
    {
        var (match, report) = await FoughtAsync();
        await _words.DeleteForFighterAsync(FighterId.From("KKK"));

        var fighter = (await _fighters.GetAsync(FighterId.From("KKK")))!;
        var corpus = SpokenCorpus.From(await _words.ListAsync(FighterId.From("KKK")));
        var prompt = FighterPersonaWriter.BuildPrompt(fighter, StyleProfileBuilder.Build("KKK", []), report.Player1.Assessment, corpus, attempt: 0);

        corpus.IsEmpty.Should().BeTrue();
        prompt.User.Should().Contain("nothing of theirs was transcribed", "the model is told the evidence is thin rather than left to invent it");

        await Writer().WriteAsync(match, report, CancellationToken.None);
        (await _profiles.GetByIdAsync(ProfileId.From("KKK"))).Should().NotBeNull("a silent night is not a reason to have no card");
    }
}
