namespace PoFightJudge.Shared.Models;

/// <summary>Who the API thinks is calling. <c>GET /auth/me</c> answers this for anonymous and signed-in callers alike.</summary>
public sealed record AuthMeDto(bool Authenticated, string? UserId, string? Name, string? AuthType)
{
    public static readonly AuthMeDto Anonymous = new(false, null, null, null);
}
