using Carter;
using PoMarriedFight.Api.Features.Auth;
using PoMarriedFight.Api.Features.Voice;
using PoMarriedFight.Api.Features.Watch;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Api.Features.Records;

/// <summary>
/// Serves a stored round's audio back to the owner's browser. The blob container is private and identity-only, so a
/// replay cannot be a direct blob URL; this proxies the bytes instead, scoped to the caller's own matches.
/// </summary>
public sealed class AudioProxyEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(ApiRoutes.Audio.Base + ApiRoutes.Audio.RoundSegment, GetRoundAsync)
            .WithTags("Records")
            .Produces(StatusCodes.Status200OK, contentType: "audio/mpeg")
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> GetRoundAsync(
        MatchId matchId,
        int roundIndex,
        HttpContext http,
        IMatchRepository matches,
        IWatchAudioStore audio,
        CancellationToken ct)
    {
        // Ownership first: a match id is guessable enough that the audio must not be readable without owning it.
        if (await matches.GetAsync(http.User.UserId(), matchId, ct) is null)
        {
            return Results.NotFound();
        }

        var turns = await matches.GetTurnsAsync(matchId, ct);
        var turn = turns.FirstOrDefault(t => t.Index == roundIndex);
        if (turn?.AudioBlobName is null)
        {
            return Results.NotFound();
        }

        var clip = await audio.GetRoundAsync(matchId, roundIndex, turn.AudioFormat, ct);
        if (clip is null || clip.Value.IsEmpty)
        {
            return Results.NotFound();
        }

        // The blob is ours and its type is the one we wrote, but the browser must still not sniff it into anything else.
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Bytes(Convert.FromBase64String(clip.Value.Base64), TtsAudioFormats.MimeType(clip.Value.Format));
    }
}
