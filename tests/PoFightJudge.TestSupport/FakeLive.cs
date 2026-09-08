using System.Threading.Channels;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Live;
using PoFightJudge.Api.Features.Storage;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.TestSupport;

/// <summary>Scriptable Live client: a test pushes server events into it and reads back what the orchestrator sent.</summary>
public sealed class FakeGeminiLiveClient : IGeminiLiveClient
{
    private readonly Channel<LiveServerEvent> _events = Channel.CreateUnbounded<LiveServerEvent>();

    public ChannelReader<LiveServerEvent> Events => _events.Reader;

    public string? ResumptionHandle { get; set; }

    public LiveSessionConfig? Config { get; private set; }

    private readonly List<string> _sentTexts = [];

    public IReadOnlyList<string> SentTexts => _sentTexts;

    private readonly List<byte[]> _sentAudio = [];

    public IReadOnlyList<byte[]> SentAudio => _sentAudio;

    private readonly List<(string Id, string Name, object Result)> _toolResponses = [];

    public IReadOnlyList<(string Id, string Name, object Result)> ToolResponses => _toolResponses;

    public bool IsClosed { get; private set; }

    public bool FailConnect { get; set; }

    public Task ConnectAsync(LiveSessionConfig config, CancellationToken ct)
    {
        if (FailConnect)
        {
            throw new InvalidOperationException("connect failed (fake)");
        }

        Config = config;
        return Task.CompletedTask;
    }

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm16k, CancellationToken ct)
    {
        lock (_sentAudio)
        {
            _sentAudio.Add(pcm16k.ToArray());
        }

        return Task.CompletedTask;
    }

    public Task SendTextAsync(string text, CancellationToken ct)
    {
        lock (_sentTexts)
        {
            _sentTexts.Add(text);
        }

        return Task.CompletedTask;
    }

    public Task SendToolResponseAsync(string id, string name, object result, CancellationToken ct)
    {
        lock (_toolResponses)
        {
            _toolResponses.Add((id, name, result));
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken ct)
    {
        IsClosed = true;
        _events.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Emit(LiveServerEvent evt) => _events.Writer.TryWrite(evt);

    public void EmitClosed(string reason = "closed")
    {
        _events.Writer.TryWrite(new LiveServerEvent.Closed(reason));
        _events.Writer.TryComplete();
    }
}

/// <summary>Hands out scriptable clients and keeps every one it made, so a reconnect is visible to the test.</summary>
public sealed class FakeGeminiLiveClientFactory
{
    private readonly List<FakeGeminiLiveClient> _created = [];

    public IReadOnlyList<FakeGeminiLiveClient> Created => _created;

    /// <summary>Hook to script a client the moment it is created, for a show that has to run itself.</summary>
    public Action<FakeGeminiLiveClient>? OnCreate { get; set; }

    public FakeGeminiLiveClient Latest => _created[^1];

    public IGeminiLiveClient Create()
    {
        var client = new FakeGeminiLiveClient();
        _created.Add(client);
        OnCreate?.Invoke(client);
        return client;
    }
}

/// <summary>Records everything the orchestrator would have sent to the room.</summary>
public sealed class RecordingLiveSink : ILiveClientSink
{
    private readonly List<byte[]> _audio = [];

    public IReadOnlyList<byte[]> Audio => _audio;

    private readonly List<CaptionDto> _captions = [];

    public IReadOnlyList<CaptionDto> Captions => _captions;

    private readonly List<DebateSnapshotDto> _snapshots = [];

    public IReadOnlyList<DebateSnapshotDto> Snapshots => _snapshots;

    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors;

    public int Clears { get; private set; }

    public MatchId? EndedMatch { get; private set; }

    public DebateSnapshotDto? Latest => _snapshots.Count == 0 ? null : _snapshots[^1];

    public Task AudioOutAsync(ReadOnlyMemory<byte> pcm24k, CancellationToken ct)
    {
        lock (_audio)
        {
            _audio.Add(pcm24k.ToArray());
        }

        return Task.CompletedTask;
    }

    public Task AudioClearAsync(CancellationToken ct)
    {
        Clears++;
        return Task.CompletedTask;
    }

    public Task CaptionAsync(CaptionDto caption, CancellationToken ct)
    {
        lock (_captions)
        {
            _captions.Add(caption);
        }

        return Task.CompletedTask;
    }

    public Task SnapshotAsync(DebateSnapshotDto snapshot, CancellationToken ct)
    {
        lock (_snapshots)
        {
            _snapshots.Add(snapshot);
        }

        return Task.CompletedTask;
    }

    public Task EndedAsync(MatchId matchId, CancellationToken ct)
    {
        EndedMatch = matchId;
        return Task.CompletedTask;
    }

    public Task ErrorAsync(string message, CancellationToken ct)
    {
        lock (_errors)
        {
            _errors.Add(message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Remembers what was handed to the analysis pipeline without running one.</summary>
public sealed class RecordingAnalysisIntake : IAnalysisIntake
{
    private readonly List<(string UserId, MatchId MatchId)> _queued = [];

    public IReadOnlyList<(string UserId, MatchId MatchId)> Queued => _queued;

    public ValueTask SubmitAsync(string userId, MatchId matchId, CancellationToken ct)
    {
        lock (_queued)
        {
            _queued.Add((userId, matchId));
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>Blob storage in a dictionary. Keeps the bytes, so a test can read back what was uploaded.</summary>
public sealed class InMemoryAudioBlobStore : IAudioBlobStore
{
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> Blobs => _blobs;

    public async Task<string> UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        lock (_blobs)
        {
            _blobs[blobName] = copy.ToArray();
        }

        return blobName;
    }

    public Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default)
    {
        lock (_blobs)
        {
            return Task.FromResult<Stream?>(_blobs.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes) : null);
        }
    }

    public Task DeleteMatchAudioAsync(MatchId matchId, CancellationToken ct = default)
    {
        lock (_blobs)
        {
            foreach (var name in _blobs.Keys.Where(k => k.StartsWith($"{matchId.Value}/", StringComparison.Ordinal)).ToList())
            {
                _blobs.Remove(name);
            }
        }

        return Task.CompletedTask;
    }
}
