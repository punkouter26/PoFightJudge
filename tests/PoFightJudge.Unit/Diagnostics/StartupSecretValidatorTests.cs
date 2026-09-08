using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Shared.Configuration;

namespace PoFightJudge.Unit.Diagnostics;

public class StartupSecretValidatorTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    [Fact]
    public async Task Missing_configuration_degrades_production_and_only_warns_elsewhere()
    {
        // Production with nothing configured: degraded, listing every required key.
        {
            var state = new StartupHealthState();
            var sut = new StartupSecretValidator(Config(), Env(Environments.Production), state, NullLogger<StartupSecretValidator>.Instance);

            await sut.StartAsync(CancellationToken.None);

            state.IsDegraded.Should().BeTrue();
            state.MissingKeys.Should().Contain([ConfigKeys.Ai.GeminiApiKey, ConfigKeys.AzureAd.ClientId, ConfigKeys.AzureAd.TenantId, ConfigKeys.Storage.TableEndpoint, ConfigKeys.Storage.BlobEndpoint]);
        }

        // Development with nothing configured: a warning, never degraded (the fakes take over).
        {
            var state = new StartupHealthState();
            var sut = new StartupSecretValidator(Config(), Env(Environments.Development), state, NullLogger<StartupSecretValidator>.Instance);

            await sut.StartAsync(CancellationToken.None);

            state.IsDegraded.Should().BeFalse();
            state.MissingKeys.Should().BeEmpty();
        }

        // Production fully configured: clean.
        {
            var all = StartupSecretValidator.RequiredKeys.Concat(StartupSecretValidator.ProductionOnlyKeys).Select(k => (k, "x")).ToArray();
            var state = new StartupHealthState();
            var sut = new StartupSecretValidator(Config(all), Env(Environments.Production), state, NullLogger<StartupSecretValidator>.Instance);

            await sut.StartAsync(CancellationToken.None);

            state.IsDegraded.Should().BeFalse();
        }
    }

    [Fact]
    public void Entra_and_storage_endpoints_are_required_only_in_production()
    {
        var dev = StartupSecretValidator.FindMissing(Config(), isProduction: false);
        dev.Should().Contain(ConfigKeys.Ai.GeminiApiKey);
        dev.Should().NotContain(ConfigKeys.AzureAd.ClientId).And.NotContain(ConfigKeys.Storage.TableEndpoint);

        var prod = StartupSecretValidator.FindMissing(Config((ConfigKeys.Ai.GeminiApiKey, "k")), isProduction: true);
        prod.Should().BeEquivalentTo(StartupSecretValidator.ProductionOnlyKeys);
    }

    [Fact]
    public void Degraded_state_is_idempotent_and_read_safe()
    {
        var state = new StartupHealthState();
        state.IsDegraded.Should().BeFalse();

        state.MarkDegraded(["a", "b"]);
        state.MarkDegraded(["c"]);

        state.IsDegraded.Should().BeTrue();
        state.MissingKeys.Should().ContainSingle("the latest validation result wins").Which.Should().Be("c");
    }
}
