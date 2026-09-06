using System.Reflection;
using PoMarriedFight.Shared;
using PoMarriedFight.Shared.Identifiers;

namespace PoMarriedFight.Unit.Shared;

/// <summary>
/// ApiRoutes is the single source of truth for every path. These tests keep it honest: every constant is a rooted
/// path, absolute URLs never collide, and the helpers escape what they interpolate.
/// </summary>
public class ApiRoutesTests
{
    private static IEnumerable<(string Name, string Value)> AllConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => ($"{type.Name}.{f.Name}", (string)f.GetRawConstantValue()!))
            .Concat(type.GetNestedTypes(BindingFlags.Public).SelectMany(AllConstants));

    [Fact]
    public void Every_route_constant_is_a_rooted_path()
    {
        var constants = AllConstants(typeof(ApiRoutes)).ToList();

        constants.Should().NotBeEmpty();
        constants.Should().OnlyContain(c => c.Value.StartsWith('/'), "routes are absolute or MapGroup-relative segments, both rooted");
        constants.Should().OnlyContain(c => !c.Value.EndsWith('/') || c.Value == "/", "no trailing slashes");
    }

    [Fact]
    public void Absolute_urls_are_unique()
    {
        var absolute = AllConstants(typeof(ApiRoutes))
            .Where(c => c.Value.StartsWith("/api", StringComparison.Ordinal) || c.Value.StartsWith("/auth", StringComparison.Ordinal) || c.Value.StartsWith("/hubs", StringComparison.Ordinal) || c.Value.StartsWith("/health", StringComparison.Ordinal))
            .Where(c => !c.Name.EndsWith("Segment", StringComparison.Ordinal))
            .ToList();

        absolute.Select(c => c.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Helpers_build_the_paths_the_server_maps()
    {
        var profile = ProfileId.From("KDH");
        var fighter = FighterId.From("MLT");
        var match = MatchId.From("0123456789abcdef0123456789abcdef");

        ApiRoutes.Profiles.ById(profile).Should().Be("/api/profiles/KDH");
        ApiRoutes.Profiles.Face(profile).Should().Be("/api/profiles/KDH/face");
        ApiRoutes.Profiles.Record(profile).Should().Be("/api/profiles/KDH/record");
        ApiRoutes.Profiles.Generate("wife").Should().Be("/api/profiles/generate?role=wife");
        ApiRoutes.Fighters.ByTag(fighter).Should().Be("/api/fighters/MLT");
        ApiRoutes.Fights.ById(match).Should().Be($"/api/fights/{match.Value}");
        ApiRoutes.Fights.Analysis(match).Should().Be($"/api/fights/{match.Value}/analysis");
        ApiRoutes.Fights.Clip(match, 2).Should().Be($"/api/fights/{match.Value}/clips/2");
        ApiRoutes.Matches.ById(match).Should().Be($"/api/matches/{match.Value}");
        ApiRoutes.Audio.Round(match, 3).Should().Be($"/api/audio/{match.Value}/3");
        ApiRoutes.Watch.GenerateRound(profile, ProfileId.From("MLT")).Should().Be("/api/watch/generate-round/KDH/MLT");
        ApiRoutes.Hubs.Live.Should().Be("/hubs/live");
        ApiRoutes.Health.PageUrl.Should().Be("/health", "the Blazor page owns bare /health; probes live under /healthz");
    }
}
