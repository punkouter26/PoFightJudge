using System.Security.Claims;
using System.Text.Json;
using Carter;
using Microsoft.Extensions.Options;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Analysis;

/// <summary>
/// Reading a fight back: the report while it is being made and once it is done, the clips behind its quotes, and a
/// retry for one that failed. Everything here is scoped to the caller's own fights.
/// </summary>
public sealed class AnalysisEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var fights = app.MapGroup(ApiRoutes.Fights.Base).RequireAuthorization().WithTags("Analysis");

        fights.MapGet(ApiRoutes.Fights.AnalysisSegment, GetAsync)
            .Produces<AnalysisResponse>()
            .Produces<AnalysisResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);

        fights.MapGet(ApiRoutes.Fights.ClipSegment, ClipAsync)
            .Produces(StatusCodes.Status200OK, contentType: "audio/wav")
            .Produces(StatusCodes.Status404NotFound);

        fights.MapPost(ApiRoutes.Fights.TranscriptSegment, PostTranscriptAsync)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        fights.MapPost(ApiRoutes.Fights.AnalysisRetrySegment, RetryAsync)
            .Produces<AnalysisResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>202 while it is still being read, 200 once it is done or has failed, 404 for somebody else's fight.</summary>
    private static async Task<IResult> GetAsync(MatchId id, ClaimsPrincipal user, IMatchRepository matches, CancellationToken ct)
    {
        var match = await matches.GetAsync(user.UserId(), id, ct);
        if (match is null)
        {
            return Results.NotFound();
        }

        var record = await matches.GetAnalysisAsync(id, ct);
        if (record is null)
        {
            // A fight still running has an analysis coming; one that ended without a recording never will.
            return match.Status is SessionStatus.Live
                ? Results.Accepted(ApiRoutes.Fights.Analysis(id), new AnalysisResponse(AnalysisStatus.Queued, null, null))
                : Results.Ok(new AnalysisResponse(AnalysisStatus.Failed, "Nothing was recorded for this fight.", null));
        }

        return record.Status switch
        {
            AnalysisStatus.Ready => Results.Ok(new AnalysisResponse(
                record.Status,
                null,
                JsonSerializer.Deserialize<AnalysisReportDto>(record.ReportJson!, GeminiJudgeClient.JsonOptions))),
            AnalysisStatus.Failed => Results.Ok(new AnalysisResponse(record.Status, record.Error, null)),
            _ => Results.Accepted(ApiRoutes.Fights.Analysis(id), new AnalysisResponse(record.Status, null, null)),
        };
    }

    /// <summary>
    /// One quoted moment, cut out of the recording on demand. Served through the API rather than as a blob URL, so
    /// it inherits the same ownership check as everything else about the fight.
    /// </summary>
    private static async Task<IResult> ClipAsync(
        MatchId id,
        int index,
        ClaimsPrincipal user,
        IMatchRepository matches,
        IAudioBlobStore blobs,
        CancellationToken ct)
    {
        var match = await matches.GetAsync(user.UserId(), id, ct);
        if (match?.AudioBlobName is null
            || await matches.GetAnalysisAsync(id, ct) is not { Status: AnalysisStatus.Ready, ReportJson: { } json })
        {
            return Results.NotFound();
        }

        var highlights = JsonSerializer.Deserialize<AnalysisReportDto>(json, GeminiJudgeClient.JsonOptions)?.Highlights ?? [];
        if (index < 0 || index >= highlights.Count)
        {
            return Results.NotFound();
        }

        await using var audio = await blobs.OpenReadAsync(match.AudioBlobName, ct);
        if (audio is null)
        {
            return Results.NotFound();
        }

        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, ct);

        var highlight = highlights[index];
        return WavSlicer.SliceRecording(buffer.ToArray(), match.AudioBlobName, DebateOrchestrator.PlayerSampleRate, highlight.StartSeconds, highlight.EndSeconds) is { } clip
            ? Results.File(clip, "audio/wav", $"{id.Value}-clip{index}.wav")
            : Results.NotFound();
    }

    /// <summary>
    /// A transcript the browser made while the fight ran. Taken only while that is switched on, only for the
    /// caller's own fight, and bounded before it is stored: the words are untrusted data on their way to a prompt.
    /// </summary>
    private static async Task<IResult> PostTranscriptAsync(
        MatchId id,
        TranscriptDto? transcript,
        ClaimsPrincipal user,
        IMatchRepository matches,
        IAudioBlobStore blobs,
        IOptions<AnalysisOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.AcceptClientTranscript || await matches.GetAsync(user.UserId(), id, ct) is null)
        {
            return Results.NotFound();
        }

        if (ClientTranscript.Sanitize(transcript) is not { } clean)
        {
            return Results.Problem("That transcript is empty or too large.", statusCode: StatusCodes.Status400BadRequest);
        }

        using var payload = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(clean, GeminiJudgeClient.JsonOptions));
        await blobs.UploadAsync(IAudioBlobStore.ClientTranscript(id), payload, "application/json", ct);
        return Results.Accepted(ApiRoutes.Fights.Analysis(id));
    }

    /// <summary>Only a finished attempt can be retried; one still running answers 409 so the button cannot be spammed.</summary>
    private static async Task<IResult> RetryAsync(
        MatchId id,
        ClaimsPrincipal user,
        IMatchRepository matches,
        IAnalysisIntake analysis,
        TimeProvider clock,
        CancellationToken ct)
    {
        var userId = user.UserId();
        var match = await matches.GetAsync(userId, id, ct);
        if (match is null)
        {
            return Results.NotFound();
        }

        if (match.AudioBlobName is null)
        {
            return Results.Problem("Nothing was recorded for this fight.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await matches.GetAnalysisAsync(id, ct) is { Status: AnalysisStatus.Queued or AnalysisStatus.Diarizing or AnalysisStatus.Judging })
        {
            return Results.Problem("It is already being read.", statusCode: StatusCodes.Status409Conflict);
        }

        await matches.SaveAnalysisAsync(new AnalysisRecordDto(id, AnalysisStatus.Queued, null, null, clock.GetUtcNow()), ct);
        await analysis.SubmitAsync(userId, id, ct);
        return Results.Accepted(ApiRoutes.Fights.Analysis(id), new AnalysisResponse(AnalysisStatus.Queued, null, null));
    }
}
