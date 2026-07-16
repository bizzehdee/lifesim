using System.Net.Http;
using System.Net.WebSockets;
using System.Globalization;
using System.Text;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Engine;

/// <summary>
/// Reads snapshots from a running <c>sim serve</c> endpoint — the browser demo's
/// primary way to show large worlds it cannot itself simulate. In this mode the client does not
/// advance canonical time; it only fetches frames the server produces, and can post
/// an edited snapshot back (the §16 write-back path).
/// </summary>
public sealed class SnapshotStreamClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _streamUri;
    private ClientWebSocket? _socket;
    private long _detailOrganismId = -1;

    public SnapshotStreamClient(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var builder = new UriBuilder(new Uri(_http.BaseAddress, "stream"))
        {
            Scheme = _http.BaseAddress.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
        };
        _streamUri = builder.Uri;
    }

    /// <summary>Fetches and validates the server's current snapshot.</summary>
    public async Task<WorldSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        string json = await _http.GetStringAsync("snapshot", cancellationToken).ConfigureAwait(false);
        return SnapshotSerializer.Load(json);
    }

    /// <summary>Posts an edited snapshot back to the server as the new world state.</summary>
    public async Task PostAsync(WorldSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using var content = new StringContent(SnapshotSerializer.Save(snapshot), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync("snapshot", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Chooses the organism whose full brain should be included in subsequent frames.</summary>
    public void SetDetailOrganismId(long? organismId) =>
        Interlocked.Exchange(ref _detailOrganismId, organismId ?? -1);

    /// <summary>Receives compact frames over WebSocket until cancelled, reconnecting after transient failures.</summary>
    public async Task StreamAsync(Action<WorldFrame> onFrame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await StreamConnectionAsync(onFrame, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException) when (!cancellationToken.IsCancellationRequested)
            {
                // Server unavailable or connection lost — reconnect below.
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
                // Handshake failed — reconnect below.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task StreamConnectionAsync(Action<WorldFrame> onFrame, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        _socket = socket;
        await socket.ConnectAsync(_streamUri, cancellationToken).ConfigureAwait(false);
        Task selectionTask = SendSelectionChangesAsync(socket, cancellationToken);
        byte[] buffer = new byte[16 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    onFrame(WorldFrameSerializer.Load(Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length))));
                }
            }
        }
        finally
        {
            _socket = null;
            try
            {
                await selectionTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
        }
    }

    private async Task SendSelectionChangesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        long lastSent = long.MinValue;
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            long detailId = Interlocked.Read(ref _detailOrganismId);
            if (detailId != lastSent)
            {
                lastSent = detailId;
                string value = detailId >= 0 ? detailId.ToString(CultureInfo.InvariantCulture) : "none";
                await socket.SendAsync(Encoding.UTF8.GetBytes(value), WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _socket?.Abort();
        _http.Dispose();
    }
}
