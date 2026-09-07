using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using PoMarriedFight.Api.Features.Ai;

namespace PoMarriedFight.Api.Features.Live;

/// <summary>Minimal text WebSocket surface, so the Live client can be driven by an in-memory fake in tests.</summary>
public interface ILiveSocket : IAsyncDisposable
{
    Task SendAsync(string json, CancellationToken ct);

    /// <summary>The next complete text message, or null once the socket has closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken ct);

    Task CloseAsync(CancellationToken ct);
}

/// <summary>Opens one socket to the Live endpoint. A delegate, because there is only ever one thing to do with it.</summary>
public delegate Task<ILiveSocket> LiveSocketConnector(Uri endpoint, string apiKey, CancellationToken ct);

public static class LiveSockets
{
    /// <summary>
    /// The real connector. The key goes in the query string, which is what the Live API documents for this endpoint,
    /// and in the header the REST surface uses; whichever the endpoint honours, the connection is authorised, and
    /// the one it ignores costs nothing.
    /// </summary>
    public static async Task<ILiveSocket> ConnectAsync(Uri endpoint, string apiKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        ClientWebSocket? socket = null;
        try
        {
            socket = new ClientWebSocket();
            socket.Options.SetRequestHeader(GeminiHttp.ApiKeyHeader, apiKey);
            await socket.ConnectAsync(endpoint, ct);

            var live = new ClientWebSocketLiveSocket(socket);
            socket = null;
            return live;
        }
        finally
        {
            // Only reached with a socket still in hand when the connect threw; the wrapper owns it otherwise.
            socket?.Dispose();
        }
    }

    /// <summary>Where to connect for one session: the endpoint carrying the key as a query parameter.</summary>
    public static Uri AddressFor(Uri endpoint, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return new UriBuilder(endpoint) { Query = $"key={Uri.EscapeDataString(apiKey)}" }.Uri;
    }
}

public sealed class ClientWebSocketLiveSocket(ClientWebSocket socket) : ILiveSocket
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly byte[] _receiveBuffer = new byte[64 * 1024];

    public async Task SendAsync(string json, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Single reader, one reusable buffer: the common single-fragment message is decoded without a copy.</summary>
    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var result = await socket.ReceiveAsync(_receiveBuffer, ct);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            return null;
        }

        if (result.EndOfMessage)
        {
            return Encoding.UTF8.GetString(_receiveBuffer, 0, result.Count);
        }

        var whole = new ArrayBufferWriter<byte>(result.Count * 2);
        whole.Write(_receiveBuffer.AsSpan(0, result.Count));
        while (!result.EndOfMessage)
        {
            result = await socket.ReceiveAsync(_receiveBuffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            whole.Write(_receiveBuffer.AsSpan(0, result.Count));
        }

        return Encoding.UTF8.GetString(whole.WrittenSpan);
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
            }
            catch (WebSocketException)
            {
                // Already gone: there is nothing left to close.
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        socket.Dispose();
        _sendLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
