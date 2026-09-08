using Blazored.LocalStorage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Client;

/// <summary>
/// What a setup screen opens on. Two things feed it and the order between them is the whole point: a rematch asked
/// for just now beats the card that was played last, and it is spent once so going back does not silently re-arm it.
/// </summary>
public class SetupMemoryTests
{
    private static readonly FightCard Card =
        new("AB", "CD", "the bins", ProfileRole.Husband, ProfileRole.Wife, HostPersonaId.Referee);

    private readonly ILocalStorageService _storage = Substitute.For<ILocalStorageService>();

    [Fact]
    public async Task The_last_card_is_what_a_setup_screen_opens_on()
    {
        _storage.GetItemAsync<FightCard>(SetupMemory.FightKey, Arg.Any<CancellationToken>()).Returns(Card);
        var memory = new SetupMemory(_storage);

        (await memory.FightAsync()).Should().Be(Card);
    }

    [Fact]
    public async Task A_rematch_outranks_the_last_card_and_is_spent_when_it_is_read()
    {
        _storage.GetItemAsync<FightCard>(SetupMemory.FightKey, Arg.Any<CancellationToken>()).Returns(Card);
        var memory = new SetupMemory(_storage);
        var again = Card with { Two = "EF", Topic = "the thermostat" };

        memory.Rematch(again);

        (await memory.FightAsync()).Should().Be(again);
        (await memory.FightAsync()).Should().Be(Card, "a rematch is asked for once, not armed forever");
    }

    /// <summary>
    /// A private window, or a browser told to keep no site data, throws on the accessor rather than returning
    /// nothing. A setup screen that could not open because of a storage preference would be absurd.
    /// </summary>
    [Fact]
    public async Task A_browser_that_keeps_no_site_data_opens_on_a_blank_card_rather_than_failing()
    {
#pragma warning disable CA2012 // NSubstitute configures the call it is handed; the ValueTask is never awaited by design.
        _storage.GetItemAsync<FightCard>(SetupMemory.FightKey, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("storage is disabled"));
#pragma warning restore CA2012

        var memory = new SetupMemory(_storage);

        (await memory.FightAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Remembering_a_card_never_fails_a_start()
    {
#pragma warning disable CA2012 // NSubstitute configures the call it is handed; the ValueTask is never awaited by design.
        _storage.SetItemAsync(SetupMemory.FightKey, Arg.Any<FightCard>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("storage is full"));
#pragma warning restore CA2012

        var memory = new SetupMemory(_storage);

        await memory.Invoking(m => m.RememberAsync(Card)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Something_older_under_the_key_is_ignored_rather_than_thrown()
    {
#pragma warning disable CA2012 // NSubstitute configures the call it is handed; the ValueTask is never awaited by design.
        _storage.GetItemAsync<WatchCard>(SetupMemory.WatchKey, Arg.Any<CancellationToken>())
            .Throws(new System.Text.Json.JsonException("shape has moved on"));
#pragma warning restore CA2012

        var memory = new SetupMemory(_storage);

        (await memory.WatchAsync()).Should().BeNull();
    }
}
