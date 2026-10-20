using Blazored.LocalStorage;
using NSubstitute;
using PoFightJudge.Client.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// The Settings panel's optional choices: a first visit gets the simplest app (no stage effects, a medium spin, the
/// Referee hosting), and whatever is changed is what the next visit opens on.
/// </summary>
public class PreferencesTests
{
    [Fact]
    public async Task A_first_visit_gets_the_defaults_and_a_change_survives_a_reload()
    {
        var storage = Substitute.For<ILocalStorageService>();

        var first = new Preferences(storage);
        await first.InitializeAsync();
        first.Effects.Should().BeFalse();
        first.Heat.Should().Be(TopicField.Heat.Medium);
        first.Host.Should().Be(HostPersonaId.Referee);

        await first.SetEffectsAsync(on: true);
        await first.SetHeatAsync(TopicField.Heat.Spicy);
        await first.SetHostAsync(HostPersonaId.Roastmaster);

        await storage.Received().SetItemAsStringAsync(Preferences.EffectsKey, "on", Arg.Any<CancellationToken>());
        await storage.Received().SetItemAsStringAsync(Preferences.HeatKey, "Spicy", Arg.Any<CancellationToken>());
        await storage.Received().SetItemAsStringAsync(Preferences.HostKey, "Roastmaster", Arg.Any<CancellationToken>());

        // What was written is what the next visit reads back.
        storage.GetItemAsStringAsync(Preferences.EffectsKey, Arg.Any<CancellationToken>()).Returns("on");
        storage.GetItemAsStringAsync(Preferences.HeatKey, Arg.Any<CancellationToken>()).Returns("Spicy");
        storage.GetItemAsStringAsync(Preferences.HostKey, Arg.Any<CancellationToken>()).Returns("Roastmaster");
        var next = new Preferences(storage);
        await next.InitializeAsync();
        next.Effects.Should().BeTrue();
        next.Heat.Should().Be(TopicField.Heat.Spicy);
        next.Host.Should().Be(HostPersonaId.Roastmaster);
    }
}
