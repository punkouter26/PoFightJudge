using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PoMarriedFight.Api.Features.Fight;
using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Api.Features.Records;
using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Shared.Identifiers;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Api.Features.Analysis;

/// <summary>
/// What happens to a fight once it is over: the recording goes up, a transcript is found or paid for, the speakers
/// are resolved, everything countable is counted, the judge rules, and both fighters get a row on their record.
/// Status is written at every step, so the room can poll and see where it has got to.
/// </summary>
public sealed partial class AnalysisPipeline(
    IServiceScopeFactory scopes,
    IGeminiFilesClient files,
    IGeminiTranscribeClient transcriber,
    IGeminiJudgeClient judge,
    TimeProvider clock,
    IOptions<AnalysisOptions> options,
    ILogger<AnalysisPipeline> logger) : BackgroundService, IAnalysisIntake
{
    public const string UserFacingFailure = "The analysis could not be completed. Try again in a moment.";

    /// <summary>Below this the judge is not called: an empty recording invites it to invent a fight that never happened.</summary>
    public const int MinWordsForJudging = 30;

    /// <summary>The same gate in seconds, so thirty muttered words from one side still fails it.</summary>
    public const double MinSpeechSecondsForJudging = 12.0;

    private readonly Channel<(string UserId, MatchId MatchId)> _queue = Channel.CreateUnbounded<(string, MatchId)>();

    public ValueTask SubmitAsync(string userId, MatchId matchId, CancellationToken ct) => _queue.Writer.WriteAsync((userId, matchId), ct);

    /// <summary>Drains the queue a few fights at a time. The cap keeps both memory and spend bounded.</summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Parallel.ForEachAsync(
            _queue.Reader.ReadAllAsync(stoppingToken),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.Value.MaxParallelAnalyses),
                CancellationToken = stoppingToken,
            },
            async (job, ct) => await RunAsync(job.UserId, job.MatchId, ct));

    /// <summary>One job, guarded: only cancellation may escape, or the whole drain loop stops with it.</summary>
    private async ValueTask RunAsync(string userId, MatchId matchId, CancellationToken ct)
    {
        try
        {
            await ProcessAsync(userId, matchId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogFailed(logger, matchId.Value, ex);

            // The detail stays in the log. A fight that was deleted while this ran must not get a resurrected row.
            using var scope = scopes.CreateScope();
            var matches = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
            if (await matches.GetAsync(userId, matchId, CancellationToken.None) is { } match)
            {
                await SaveStatusAsync(matches, matchId, AnalysisStatus.Failed, null, UserFacingFailure, CancellationToken.None);
                await MarkAsync(matches, match, SessionStatus.Failed, CancellationToken.None);
            }
        }
    }

    /// <summary>The whole pipeline for one fight. Public so a test can drive it without the background loop.</summary>
    public async Task ProcessAsync(string userId, MatchId matchId, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var scope = scopes.CreateScope();
        var matches = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
        var results = scope.ServiceProvider.GetRequiredService<IFighterResultRepository>();
        var blobs = scope.ServiceProvider.GetRequiredService<IAudioBlobStore>();

        var match = await matches.GetAsync(userId, matchId, ct)
            ?? throw new InvalidOperationException($"Fight {matchId.Value} was not found for this user.");
        var blobName = match.AudioBlobName ?? throw new InvalidOperationException("That fight has no recording.");

        await SaveStatusAsync(matches, matchId, AnalysisStatus.Diarizing, null, null, ct);
        match = await MarkAsync(matches, match, SessionStatus.Analyzing, ct);

        await using var audio = await blobs.OpenReadAsync(blobName, ct)
            ?? throw new InvalidOperationException("The recording is missing from storage.");
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        // The turns are read while the recording uploads, and the upload is also what buys a browser-side
        // transcript the time it needs to arrive.
        var turnsTask = matches.GetTurnsAsync(matchId, ct);

        // Whatever the recording was stored as goes up as it is: the judge and the transcriber both read Opus,
        // and decoding here would undo the point of storing it compressed.
        var opus = OpusAudio.IsOpus(blobName);
        var mimeType = opus ? OpusAudio.ContentType : "audio/wav";
        var fileName = $"{matchId.Value}-players.{(opus ? OpusAudio.Extension : "wav")}";
        var file = await files.UploadAndWaitAsync(buffer, buffer.Length, mimeType, fileName, clock, ct);

        // The cheapest usable transcript wins. Only a fight that produced neither is worth paying to diarize.
        var (existing, source) = await ReadExistingTranscriptAsync(blobs, matchId, ct);
        var transcript = existing ?? await transcriber.TranscribeAsync(file.Uri, mimeType, ct);
        var turns = await turnsTask;
        var mapped = SpeakerMapper.Map(transcript, turns);
        LogTranscribed(logger, matchId.Value, transcript.Words.Count, source, mapped.Note);

        var first = SpeechMetrics.Compute(Speaker.Player1, mapped, turns);
        var second = SpeechMetrics.Compute(Speaker.Player2, mapped, turns);

        // Without this gate the model invents facts and quotes from twelve seconds of breathing, and the headline
        // then contradicts the recording it came from.
        JudgeOutputDto output;
        if (mapped.Words.Count < MinWordsForJudging || first.TalkSeconds + second.TalkSeconds < MinSpeechSecondsForJudging)
        {
            LogTooShort(logger, matchId.Value, mapped.Words.Count, first.TalkSeconds + second.TalkSeconds);
            output = NothingToJudge();
        }
        else
        {
            await SaveStatusAsync(matches, matchId, AnalysisStatus.Judging, null, null, ct);
            output = await judge.JudgeAsync(
                new JudgeRequest(
                    match.Topic,
                    match.Side1.DisplayName,
                    match.Side2.DisplayName,
                    file.Uri,
                    mimeType,
                    mapped,
                    turns,
                    first,
                    second,
                    match.Verdict),
                ct);
        }

        var overall = new OverallVerdictDto(
            ToSpeaker(output.Overall.WinnerLogic),
            ToSpeaker(output.Overall.WinnerCorrect),
            ToSpeaker(output.Overall.Overall),
            output.Overall.Reasons,
            output.Overall.Summary);

        var report = new AnalysisReportDto(
            matchId,
            match.Topic,
            new PlayerReportDto(match.Side1.DisplayName, first, output.Player1),
            new PlayerReportDto(match.Side2.DisplayName, second, output.Player2),
            overall,
            match.Verdict,
            mapped.Flagged,
            mapped.Note,
            Math.Round(stopwatch.Elapsed.TotalSeconds, 1),
            clock.GetUtcNow(),
            HighlightFinder.Find(output.Player1, output.Player2, mapped));

        await SaveStatusAsync(matches, matchId, AnalysisStatus.Ready, JsonSerializer.Serialize(report, GeminiJudgeClient.JsonOptions), null, ct);
        await SaveRecordsAsync(results, match, report, mapped, ct);
        await MarkAsync(matches, match with { Winner = WinnerTag(match, overall) }, SessionStatus.Ready, ct);
        LogReady(logger, matchId.Value, stopwatch.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// One row per fighter, keyed on the tag the fight was started under, each carrying the snapshot their style
    /// profile is built from. Re-analysing a fight overwrites those rows rather than counting it twice.
    /// </summary>
    private static async Task SaveRecordsAsync(
        IFighterResultRepository results,
        MatchDto match,
        AnalysisReportDto report,
        MappedTranscript transcript,
        CancellationToken ct)
    {
        if (!Initials.ArePair(match.Side1.Id, match.Side2.Id))
        {
            return;
        }

        var at = match.EndedAt ?? match.StartedAt;
        await results.SaveAsync(
            [
                Row(Speaker.Player1, match.Side1.Id, match.Side2.Id, report.Player1),
                Row(Speaker.Player2, match.Side2.Id, match.Side1.Id, report.Player2),
            ],
            ct);

        FighterResultDto Row(Speaker side, string tag, string opponent, PlayerReportDto theirs) => new(
            tag,
            match.UserId,
            match.Id,
            MatchMode.Fight,
            at,
            match.Topic,
            opponent,
            report.Overall.Overall == side,
            Draw: false,
            Score(theirs.Assessment),
            FightStyleSnapshotExtractor.FromFight(theirs.Assessment, theirs.Metrics, FirstWords(transcript, side)));
    }

    /// <summary>
    /// One number for how well they argued, out of a hundred: the five things the judge scores about the argument
    /// itself. Emotion and politeness are deliberately not in it — this is a record of arguing, not of behaving.
    /// </summary>
    private static int Score(PlayerAssessmentDto assessment) =>
        Math.Clamp(
            (assessment.Logic + assessment.Clarity + assessment.Persuasiveness + assessment.EvidenceUse + assessment.RebuttalQuality) * 2,
            0,
            100);

    /// <summary>The opening of the first thing this side said, which is where a habit shows first.</summary>
    private static string FirstWords(MappedTranscript transcript, Speaker side) =>
        string.Join(' ', transcript.For(side).OrderBy(w => w.Start).Take(FightStyleSnapshotExtractor.OpenerWords).Select(w => w.Text));

    /// <summary>
    /// The transcript this fight already produced, cheapest first: the host's own captions, then the browser's. The
    /// source is logged, so a run that quietly fell back to the paid model is visible afterwards.
    /// </summary>
    private async Task<(TranscriptDto? Transcript, string Source)> ReadExistingTranscriptAsync(IAudioBlobStore blobs, MatchId matchId, CancellationToken ct)
    {
        if (options.Value.UseLiveCaptionTranscript
            && await ReadTranscriptAsync(blobs, matchId, IAudioBlobStore.LiveTranscript(matchId), ct) is { } captions
            && captions.Words.Count >= Math.Max(0, options.Value.MinCaptionTranscriptWords))
        {
            return (captions, "live-caption");
        }

        return await ReadClientTranscriptAsync(blobs, matchId, ct) is { } browser
            ? (browser, "on-device")
            : (null, "diarized");
    }

    /// <summary>Reads one stored transcript. Anything missing or malformed is simply not there.</summary>
    private async Task<TranscriptDto?> ReadTranscriptAsync(IAudioBlobStore blobs, MatchId matchId, string blobName, CancellationToken ct)
    {
        try
        {
            await using var stream = await blobs.OpenReadAsync(blobName, ct);
            if (stream is null)
            {
                return null;
            }

            var transcript = await JsonSerializer.DeserializeAsync<TranscriptDto>(stream, GeminiJudgeClient.JsonOptions, ct);
            return transcript is { Words.Count: > 0 } ? transcript : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            LogTranscriptUnusable(logger, matchId.Value, ex);
            return null;
        }
    }

    /// <summary>
    /// A transcript the browser made while the fight ran, if that is switched on and one was posted. Best-effort:
    /// anything missing or unreadable falls straight back to the transcribe model.
    /// </summary>
    private async Task<TranscriptDto?> ReadClientTranscriptAsync(IAudioBlobStore blobs, MatchId matchId, CancellationToken ct)
    {
        if (!options.Value.AcceptClientTranscript)
        {
            return null;
        }

        var name = IAudioBlobStore.ClientTranscript(matchId);
        var backoff = new PollBackoff(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(Math.Max(0, options.Value.ClientTranscriptWaitSeconds)),
            clock);

        do
        {
            await using var stream = await blobs.OpenReadAsync(name, ct);
            if (stream is not null)
            {
                try
                {
                    var transcript = await JsonSerializer.DeserializeAsync<TranscriptDto>(stream, GeminiJudgeClient.JsonOptions, ct);
                    return transcript is { Words.Count: > 0 } ? transcript : null;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    // It arrived and it is unusable; waiting longer will not improve it.
                    LogTranscriptUnusable(logger, matchId.Value, ex);
                    return null;
                }
            }
        }
        while (await backoff.WaitAsync(ct));

        return null;
    }

    private static Speaker ToSpeaker(string value) =>
        PlayerIdExtensions.TryParse(value, out var id) ? id.ToSpeaker() : Speaker.Player1;

    private static string WinnerTag(MatchDto match, OverallVerdictDto overall) =>
        overall.Overall == Speaker.Player1 ? match.Side1.Id : match.Side2.Id;

    /// <summary>A ruling that admits nothing was said, so the headline cannot contradict the recording.</summary>
    private static JudgeOutputDto NothingToJudge() => new(
        NothingToAssess(),
        NothingToAssess(),
        new JudgeOverallDto(
            "player1",
            "player1",
            "player1",
            ["Neither of you said enough to judge.", "The recording was cut short or stayed quiet.", "Have the argument again, for longer."],
            "There was not enough here to rule on. A few sentences each is the minimum."));

    private static PlayerAssessmentDto NothingToAssess() => new(
        "A1", "Not enough speech to assess.", 0, [],
        0, 0, 0, [], 0, 0, 0, 0, [],
        new EmotionProfileDto(0, 0, 0, 0, 0, 0), [],
        "N/A", [], "Unknown", "Unknown", "Unknown",
        0, 0, 0, 0, "N/A", "N/A",
        ["Speak for longer, so there is something to assess.", "Take a position; hedging gives the judge nothing to grade.", "Answer the question you were actually asked."]);

    private Task SaveStatusAsync(IMatchRepository matches, MatchId matchId, AnalysisStatus status, string? json, string? error, CancellationToken ct) =>
        matches.SaveAnalysisAsync(new AnalysisRecordDto(matchId, status, json, error, clock.GetUtcNow()), ct);

    private static async Task<MatchDto> MarkAsync(IMatchRepository matches, MatchDto match, SessionStatus status, CancellationToken ct)
    {
        var updated = match with { Status = status };
        await matches.UpsertAsync(updated, ct);
        return updated;
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Analysis {MatchId}: {Words} words from the {Source} transcript. {Note}")]
    private static partial void LogTranscribed(ILogger logger, string matchId, int words, string source, string note);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Analysis {MatchId}: ready in {Seconds:F1}s")]
    private static partial void LogReady(ILogger logger, string matchId, double seconds);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Error, Message = "Analysis {MatchId} failed")]
    private static partial void LogFailed(ILogger logger, string matchId, Exception ex);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information, Message = "Analysis {MatchId}: the judge was skipped ({Words} words, {Seconds:F1}s of speech) — too little was said")]
    private static partial void LogTooShort(ILogger logger, string matchId, int words, double seconds);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Warning, Message = "Analysis {MatchId}: a stored transcript was unusable, falling back to the transcribe model")]
    private static partial void LogTranscriptUnusable(ILogger logger, string matchId, Exception ex);
}
