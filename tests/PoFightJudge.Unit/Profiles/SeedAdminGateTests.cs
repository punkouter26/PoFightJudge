using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Auth;
using PoFightJudge.Api.Features.Profiles.Seeding;
using PoFightJudge.Shared.Configuration;
using PoFightJudge.Shared.Models;
using PoFightJudge.Shared.Validators;

namespace PoFightJudge.Unit.Profiles;

public class SeedAdminGateTests
{
    private static readonly IConfiguration Empty = new ConfigurationBuilder().Build();

    private static ClaimsPrincipal Principal(string authType, params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authType));

    [Fact]
    public void Anonymous_never_qualifies_and_guests_only_outside_production()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var guest = Principal(GuestMiddleware.AuthenticationType,
            (ClaimTypes.NameIdentifier, "GUEST-1"), ("auth_type", GuestMiddleware.AuthenticationType), ("preferred_username", SeedAdminGate.DefaultAdminEmails[0]));

        SeedAdminGate.IsSeedAdmin(anonymous, Empty, production: true).Should().BeFalse();
        SeedAdminGate.IsSeedAdmin(anonymous, Empty, production: false).Should().BeFalse("signed in is the floor everywhere");
        SeedAdminGate.IsSeedAdmin(guest, Empty, production: true).Should().BeFalse("a guest cookie is not an identity, whatever claims it carries");
        SeedAdminGate.IsSeedAdmin(guest, Empty, production: false).Should().BeTrue("outside Production the dev guest is the only login there is");
    }

    [Fact]
    public void The_admin_role_qualifies_and_a_plain_fake_user_does_not()
    {
        var admin = Principal(FakeAuthOptions.DefaultScheme, (ClaimTypes.NameIdentifier, "dev"), (ClaimTypes.Role, "Admin"));
        var user = Principal(FakeAuthOptions.DefaultScheme, (ClaimTypes.NameIdentifier, "dev"));

        SeedAdminGate.IsSeedAdmin(admin, Empty, production: true).Should().BeTrue();
        SeedAdminGate.IsSeedAdmin(user, Empty, production: true).Should().BeFalse();
    }

    [Fact]
    public void A_real_login_qualifies_by_email_default_list_or_configured_override()
    {
        var owner = Principal("Federation", (ClaimTypes.NameIdentifier, "oid-1"), ("preferred_username", "PunkOuter26@gmail.com"));
        var boss = Principal("Federation", (ClaimTypes.NameIdentifier, "oid-2"), (ClaimTypes.Email, "boss@example.com"));
        var stranger = Principal("Federation", (ClaimTypes.NameIdentifier, "oid-3"), ("email", "someone@example.com"));
        var overridden = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{ConfigKeys.Seed.AdminEmails}:0"] = "Boss@Example.com" })
            .Build();

        SeedAdminGate.IsSeedAdmin(owner, Empty, production: true).Should().BeTrue("the default list is the repository owner, case-insensitively");
        SeedAdminGate.IsSeedAdmin(stranger, Empty, production: true).Should().BeFalse();
        SeedAdminGate.IsSeedAdmin(boss, overridden, production: true).Should().BeTrue();
        SeedAdminGate.IsSeedAdmin(owner, overridden, production: true).Should().BeFalse("a configured list replaces the default rather than extending it");
    }

    [Fact]
    public void Seed_personas_are_eight_distinct_valid_profiles_with_a_face_asset_for_the_pictured_ones()
    {
        var validator = new CreateProfileRequestValidator();

        SeedProfiles.All.Should().HaveCount(8);
        SeedProfiles.All.Select(p => p.Initials).Should().OnlyHaveUniqueItems().And.Contain(["MAH", "KSH", "DJT", "HRC"]);
        SeedProfiles.All.Count(p => p.Role == ProfileRole.Husband).Should().Be(2);
        foreach (var persona in SeedProfiles.All)
        {
            validator.Validate(persona).IsValid.Should().BeTrue($"{persona.Initials} must pass the same rules as a hand-made profile");
        }

        SeedProfiles.FaceAsset("MAH").Should().Be("images/profiles/mah.png");
    }
}
