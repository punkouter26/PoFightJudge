using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Diagnostics;

public class SecretMaskerTests
{

    [Fact]
    public void Presence_reports_configured_or_not_and_never_the_value()
    {
        SecretMasker.Presence("anything").Should().Be(DiagDto.Configured);
        SecretMasker.Presence("  ").Should().Be(DiagDto.NotConfigured);
        SecretMasker.Presence(null).Should().Be(DiagDto.NotConfigured);
    }
}
