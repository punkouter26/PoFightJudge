using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Fight;

/// <summary>
/// What the registry is for: one live fight per user, a clock that reaches every running fight, and a tenancy rule
/// that lives in exactly one place.
/// </summary>
public sealed class SessionRegistryTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 6, 19, 0, 0, TimeSpan.Zero));
    private readonly FakeGeminiLiveClientFactory _clients = new();
    private readonly RecordingLiveSink _sink = new();
    private readonly InMemoryAudioBlobStore _blobs = new();
    private readonly RecordingAnalysisIntake _analysis = new();
    private readonly ServiceProvider _services;
    private readonly SessionRegistry _registry;

    public SessionRegistryTests()
    {
        var watchResults = new InMemoryWatchResultRepository();
        var fighterResults = new InMemoryFighterResultRepository();
        var matches = new InMemoryMatchRepository(watchResults, fighterResults, new InMemoryFighterWordsRepository());

        var services = new ServiceCollection();
        services.AddSingleton<IMatchRepository>(matches);
        services.AddSingleton<IAudioBlobStore>(_blobs);
        _services = services.BuildServiceProvider();

        _registry = new SessionRegistry(
            _services.GetRequiredService<IServiceScopeFactory>(),
            () => _clients.Create(),
            _ => _sink,
            _analysis,
            new DebateOptions { ClientGraceSeconds = 30, MaxSessionSeconds = 900 },
            new AiMode(UseFakes: true, HasGeminiKey: false, Reason: "test"),
            _clock,
            NullLoggerFactory.Instance);
    }

    private static ShowSetup Setup(string one = "AB", string two = "CD") =>
        new(HostPersonaId.Referee, one, two, "the thermostat");

    public async ValueTask DisposeAsync()
    {
        await _registry.StopAsync(CancellationToken.None);
        await _registry.DisposeAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task A_started_fight_can_be_found_by_the_person_running_it_and_by_nobody_else()
    {
        var fight = await _registry.CreateAsync("user-1", Setup(), CancellationToken.None);
        var id = fight.Session.MatchId;

        _registry.Get(id).Should().BeSameAs(fight);
        _registry.Get(id, "user-1").Should().BeSameAs(fight);
        _registry.Get(id, "user-2").Should().BeNull("a fight belongs to whoever started it");
        _registry.Get(id, null).Should().BeNull("an anonymous caller owns nothing");
        _registry.Get(MatchId.New(), "user-1").Should().BeNull();
    }

    [Fact]
    public async Task Starting_a_second_fight_ends_the_first_rather_than_leaving_a_stale_tab_recording()
    {
        var first = await _registry.CreateAsync("user-1", Setup(), CancellationToken.None);

        var second = await _registry.CreateAsync("user-1", Setup("EF", "GH"), CancellationToken.None);

        first.Ended.Should().BeTrue();
        _registry.Get(first.Session.MatchId).Should().BeNull();
        _registry.Get(second.Session.MatchId).Should().BeSameAs(second);
    }

    [Fact]
    public async Task Two_people_can_each_have_their_own_fight_at_the_same_time()
    {
        var mine = await _registry.CreateAsync("user-1", Setup(), CancellationToken.None);
        var theirs = await _registry.CreateAsync("user-2", Setup("EF", "GH"), CancellationToken.None);

        mine.Ended.Should().BeFalse();
        _registry.Get(mine.Session.MatchId).Should().BeSameAs(mine);
        _registry.Get(theirs.Session.MatchId).Should().BeSameAs(theirs);
    }

    [Fact]
    public async Task Ending_a_fight_persists_it_and_takes_it_off_the_list()
    {
        var fight = await _registry.CreateAsync("user-1", Setup(), CancellationToken.None);
        var id = fight.Session.MatchId;

        await _registry.EndAsync(id, "test", CancellationToken.None);

        fight.Ended.Should().BeTrue();
        _registry.Get(id).Should().BeNull();
        _sink.EndedMatch.Should().Be(id);

        // Ending something that is already gone is not an error; two tabs can press stop at once.
        await _registry.EndAsync(id, "again", CancellationToken.None);
    }

    [Fact]
    public async Task The_clock_reaches_every_running_fight()
    {
        var fight = await _registry.CreateAsync("user-1", Setup(), CancellationToken.None);
        fight.ClientConnected();

        _clock.Advance(TimeSpan.FromSeconds(30));
        await _registry.TickAllAsync(CancellationToken.None);

        _sink.Snapshots.Should().HaveCountGreaterThan(1, "a running fight is told what time it is");
        fight.Ended.Should().BeFalse();
    }

    [Fact]
    public async Task A_fight_nobody_is_watching_is_ended_by_the_clock_and_then_reaped()
    {
        var fight = await _registry.CreateAsync("user-1", Setup(), CancellationToken.None);
        var id = fight.Session.MatchId;

        _clock.Advance(TimeSpan.FromSeconds(31));
        await _registry.TickAllAsync(CancellationToken.None);
        fight.Ended.Should().BeTrue("nobody ever joined, so there is nobody to record for");

        await _registry.TickAllAsync(CancellationToken.None);
        _registry.Get(id).Should().BeNull("a finished fight is cleared on the next pass, once it has stored itself");
    }

    [Fact]
    public async Task A_fight_that_cannot_connect_is_not_left_on_the_list()
    {
        _clients.OnCreate = client => client.FailConnect = true;

        var start = async () => await _registry.CreateAsync("user-1", Setup(), CancellationToken.None);

        await start.Should().ThrowAsync<InvalidOperationException>();
        _registry.Get(MatchId.New(), "user-1").Should().BeNull();
        await _registry.TickAllAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Shutting_down_ends_everything_that_is_still_running()
    {
        var mine = await _registry.CreateAsync("user-1", Setup(), CancellationToken.None);
        var theirs = await _registry.CreateAsync("user-2", Setup("EF", "GH"), CancellationToken.None);

        await _registry.StopAsync(CancellationToken.None);

        mine.Ended.Should().BeTrue();
        theirs.Ended.Should().BeTrue("a deploy must not lose a fight that was halfway through");
    }
}
