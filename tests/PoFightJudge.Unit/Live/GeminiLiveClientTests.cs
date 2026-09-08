using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Live;

namespace PoFightJudge.Unit.Live;

public class GeminiLiveClientTests
{
    private static readonly LiveOptions Options = LiveOptions.Defaults with { ApiKey = "test-key" };

    private static GeminiLiveClient Client(FakeLiveSocket socket, LiveOptions? options = null) =>
        new((_, _, _) => Task.FromResult<ILiveSocket>(socket), options ?? Options, GeminiModelOptions.Defaults, NullLogger<GeminiLiveClient>.Instance);

    /// <summary>Connects with the acknowledgement already queued, which is what the real endpoint does immediately.</summary>
    private static async Task<GeminiLiveClient> ConnectedAsync(FakeLiveSocket socket)
    {
        var client = Client(socket);
        socket.Receive("""{"setupComplete":{}}""");
        await client.ConnectAsync(new LiveSessionConfig("host the debate"), CancellationToken.None);
        return client;
    }

    [Fact]
    public async Task Connecting_sends_the_setup_frame_and_waits_to_be_acknowledged()
    {
        await using var socket = new FakeLiveSocket();
        await using var client = await ConnectedAsync(socket);

        var setup = JsonDocument.Parse(socket.Sent.Should().ContainSingle().Subject).RootElement;
        setup.GetProperty("setup").GetProperty("model").GetString().Should().Be($"models/{GeminiModelOptions.Defaults.Live}");
    }

    [Fact]
    public async Task The_key_travels_both_ways_the_endpoint_accepts_it()
    {
        await using var socket = new FakeLiveSocket();
        Uri? asked = null;
        var client = new GeminiLiveClient(
            (endpoint, _, _) => { asked = endpoint; return Task.FromResult<ILiveSocket>(socket); },
            Options,
            GeminiModelOptions.Defaults,
            NullLogger<GeminiLiveClient>.Instance);
        await using var _ = client;

        socket.Receive("""{"setupComplete":{}}""");
        await client.ConnectAsync(new LiveSessionConfig("host"), CancellationToken.None);

        asked.Should().NotBeNull();
        asked!.Query.Should().Contain("key=test-key", "the WebSocket endpoint takes the key as a query parameter");
        asked.AbsoluteUri.Should().StartWith(LiveOptions.Defaults.Endpoint.AbsoluteUri);
    }

    [Fact]
    public async Task An_endpoint_that_never_acknowledges_setup_fails_rather_than_hanging()
    {
        await using var socket = new FakeLiveSocket();
        await using var client = Client(socket, Options with { SetupTimeout = TimeSpan.FromMilliseconds(80) });

        var connect = async () => await client.ConnectAsync(new LiveSessionConfig("host"), CancellationToken.None);

        await connect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*acknowledge setup*");
    }

    [Fact]
    public async Task A_socket_that_closes_during_setup_fails_the_connect_with_the_reason()
    {
        await using var socket = new FakeLiveSocket();
        await using var client = Client(socket);
        socket.EndOfStream();

        var connect = async () => await client.ConnectAsync(new LiveSessionConfig("host"), CancellationToken.None);

        await connect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*closed before setup*");
    }

    [Fact]
    public async Task Connecting_without_a_key_says_so_instead_of_opening_a_socket()
    {
        await using var socket = new FakeLiveSocket();
        await using var client = Client(socket, Options with { ApiKey = "  " });

        var connect = async () => await client.ConnectAsync(new LiveSessionConfig("host"), CancellationToken.None);

        await connect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*key*");
        socket.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Connecting_twice_on_one_client_is_a_mistake_worth_naming()
    {
        await using var socket = new FakeLiveSocket();
        await using var client = await ConnectedAsync(socket);

        var again = async () => await client.ConnectAsync(new LiveSessionConfig("host"), CancellationToken.None);

        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Already connected*");
    }

    [Fact]
    public async Task Events_reach_the_reader_in_order_and_the_handle_is_kept_for_reconnecting()
    {
        await using var socket = new FakeLiveSocket();
        await using var client = await ConnectedAsync(socket);

        socket.Receive("""{"sessionResumptionUpdate":{"newHandle":"h-42","resumable":true}}""");
        socket.Receive("""{"serverContent":{"outputTranscription":{"text":"one at a time"}}}""");
        socket.EndOfStream();

        var seen = new List<LiveServerEvent>();
        await foreach (var evt in client.Events.ReadAllAsync(CancellationToken.None))
        {
            seen.Add(evt);
        }

        seen.Should().SatisfyRespectively(
            first => first.Should().BeOfType<LiveServerEvent.SetupComplete>(),
            second => second.Should().BeOfType<LiveServerEvent.ResumptionHandle>(),
            third => third.Should().BeOfType<LiveServerEvent.OutputTranscript>(),
            last => last.Should().BeOfType<LiveServerEvent.Closed>());
        client.ResumptionHandle.Should().Be("h-42", "reconnecting picks the session up rather than starting a new one");
    }

    [Fact]
    public async Task What_the_orchestrator_sends_goes_out_as_live_api_frames()
    {
        await using var socket = new FakeLiveSocket();
        await using var client = await ConnectedAsync(socket);

        await client.SendAudioAsync(new byte[] { 9, 9 }, CancellationToken.None);
        await client.SendTextAsync("wrap it up", CancellationToken.None);
        await client.SendToolResponseAsync("fc-1", "set_players", new { ok = true }, CancellationToken.None);

        socket.Sent.Should().HaveCount(4, "the setup frame plus the three that followed");
        socket.Sent[1].Should().Contain("realtimeInput");
        socket.Sent[2].Should().Contain("clientContent");
        socket.Sent[3].Should().Contain("toolResponse");
    }

    [Fact]
    public async Task Closing_is_best_effort_and_never_throws_at_the_caller()
    {
        await using var socket = new FakeLiveSocket { ThrowOnClose = true };
        await using var client = await ConnectedAsync(socket);

        var close = async () => await client.CloseAsync(CancellationToken.None);

        await close.Should().NotThrowAsync("a debate that is already over must not fail on the way out");
    }

    [Fact]
    public async Task Closing_a_client_that_never_connected_does_nothing()
    {
        await using var socket = new FakeLiveSocket();
        var client = Client(socket);

        await client.CloseAsync(CancellationToken.None);

        socket.Sent.Should().BeEmpty();
    }

    /// <summary>An in-memory <see cref="ILiveSocket"/>: frames sent are recorded, frames received are queued by the test.</summary>
    private sealed class FakeLiveSocket : ILiveSocket
    {
        private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();

        public List<string> Sent { get; } = [];

        public bool ThrowOnClose { get; init; }

        public void Receive(string json) => _inbound.Writer.TryWrite(json);

        /// <summary>No more frames will arrive: the socket has gone.</summary>
        public void EndOfStream() => _inbound.Writer.TryComplete();

        public Task SendAsync(string json, CancellationToken ct)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken ct)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public Task CloseAsync(CancellationToken ct)
        {
            if (ThrowOnClose)
            {
                throw new InvalidOperationException("the socket was already gone");
            }

            _inbound.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _inbound.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
