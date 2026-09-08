using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoFightJudge.Api.Common;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Diagnostics;

public class SecretMaskerTests
{
    [Fact]
    public void Mask_keeps_only_the_edges_of_a_long_value_and_hides_short_ones_entirely()
    {
        SecretMasker.Mask("AIzaSyD-1234567890abcdefXYZW").Should().Be("AIza********************XYZW");
        SecretMasker.Mask("short").Should().Be("****");
        SecretMasker.Mask("exactly12chr").Should().Be("****");
        SecretMasker.Mask(null).Should().BeEmpty();
        SecretMasker.Mask(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void MaskHost_obscures_the_middle_of_the_host_and_drops_the_path()
    {
        SecretMasker.MaskHost("https://kv-poshared.vault.azure.net/secrets/x").Should().Be($"https://kv-p{new string('*', 19)}.net/");
        SecretMasker.MaskHost("https://localhost:12002/devstoreaccount1").Should().Be("https://loca*host/");
        SecretMasker.MaskHost("not a uri").Should().Be("****");
        SecretMasker.MaskHost(null).Should().Be("****");
    }

    [Fact]
    public void Presence_reports_configured_or_not_and_never_the_value()
    {
        SecretMasker.Presence("anything").Should().Be(DiagDto.Configured);
        SecretMasker.Presence("  ").Should().Be(DiagDto.NotConfigured);
        SecretMasker.Presence(null).Should().Be(DiagDto.NotConfigured);
    }

    [Fact]
    public async Task Configuration_health_check_mirrors_the_startup_state()
    {
        var state = new StartupHealthState();
        var check = new ConfigurationHealthCheck(state);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        state.MarkDegraded(["PoFightJudge:GeminiApiKey"]);
        var unhealthy = await check.CheckHealthAsync(new HealthCheckContext());
        unhealthy.Status.Should().Be(HealthStatus.Unhealthy);
        unhealthy.Description.Should().Contain("PoFightJudge:GeminiApiKey");
    }
}
