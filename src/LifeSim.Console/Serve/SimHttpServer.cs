using System.Net;
using System.Net.WebSockets;
using System.Text;
using LifeSim.Core.Snapshot;

namespace LifeSim.Console.Serve;

/// <summary>
/// A minimal <see cref="HttpListener"/> transport over a <see cref="SnapshotService"/>,
/// using only BCL types (no ASP.NET) so the console stays lightweight. Endpoints:
/// <list type="bullet">
/// <item><c>GET /snapshot</c> — the current world snapshot (JSON).</item>
/// <item><c>POST /snapshot</c> — import an edited snapshot as the new world state.</item>
/// <item><c>GET /metrics</c> — the current tick's metrics (one NDJSON line).</item>
/// <item><c>GET /health</c> — liveness check.</item>
/// <item><c>GET /stream</c> — a WebSocket that pushes compact frames; clients send a selected organism id for brain detail.</item>
/// </list>
/// </summary>
public sealed class SimHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly SnapshotService _service;
    private readonly TextWriter _log;

    public SimHttpServer(SnapshotService service, int port, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(log);
        _service = service;
        _log = log;
        Prefix = $"http://localhost:{port}/";
        _listener.Prefixes.Add(Prefix);
    }

    /// <summary>The base URL the server listens on, e.g. <c>http://localhost:8080/</c>.</summary>
    public string Prefix { get; }

    /// <summary>Accepts requests until <paramref name="cancellationToken"/> fires; then stops the listener.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        using CancellationTokenRegistration registration = cancellationToken.Register(SafeStop);

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = HandleAsync(context, cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        HttpListenerRequest request = context.Request;
        string path = request.Url?.AbsolutePath ?? "/";

        try
        {
            if (request.IsWebSocketRequest && path == "/stream")
            {
                await StreamAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            switch (request.HttpMethod, path)
            {
                case ("GET", "/snapshot"):
                    await WriteTextAsync(context, 200, "application/json", _service.CurrentSnapshotJson()).ConfigureAwait(false);
                    break;
                case ("GET", "/metrics"):
                    await WriteTextAsync(context, 200, "application/x-ndjson", _service.CurrentMetricsLine()).ConfigureAwait(false);
                    break;
                case ("GET", "/health"):
                    await WriteTextAsync(context, 200, "text/plain", "ok").ConfigureAwait(false);
                    break;
                case ("POST", "/snapshot"):
                    await ImportAsync(context).ConfigureAwait(false);
                    break;
                default:
                    await WriteTextAsync(context, 404, "text/plain", "not found").ConfigureAwait(false);
                    break;
            }
        }
        catch (SnapshotValidationException ex)
        {
            await WriteTextAsync(context, 400, "text/plain", $"invalid snapshot: {ex.Message}").ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _log.WriteLine($"serve: request I/O error: {ex.Message}");
        }
        catch (HttpListenerException ex)
        {
            _log.WriteLine($"serve: request error: {ex.Message}");
        }
    }

    private async Task ImportAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
        string body = await reader.ReadToEndAsync().ConfigureAwait(false);
        _service.Import(body); // throws SnapshotValidationException on bad input → 400 in HandleAsync
        await WriteTextAsync(context, 200, "text/plain", $"imported (tick={_service.Tick})").ConfigureAwait(false);
    }

    private async Task StreamAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        using WebSocket socket = wsContext.WebSocket;
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var selection = new StreamSelection();
        Task receiveTask = ReceiveSelectionAsync(socket, selection, connectionCts.Token);
        long lastTick = -1;
        long lastDetailId = long.MinValue;

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                long tick = _service.Tick;
                long detailId = selection.Value;
                if (tick != lastTick || detailId != lastDetailId)
                {
                    lastTick = tick;
                    lastDetailId = detailId;
                    byte[] frame = Encoding.UTF8.GetBytes(_service.CurrentFrameJson(detailId >= 0 ? detailId : null));
                    await socket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — fall through to close.
        }
        catch (WebSocketException)
        {
            // Client vanished — nothing more to do.
        }
        finally
        {
            connectionCts.Cancel();
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the send loop or server shuts down first.
            }
            catch (WebSocketException)
            {
                // Client vanished while receiving its selection.
            }
        }
    }

    private static async Task ReceiveSelectionAsync(
        WebSocket socket,
        StreamSelection selection,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage)
            {
                continue;
            }

            string value = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (value == "none")
            {
                selection.Value = -1;
            }
            else if (long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out long id) && id >= 0)
            {
                selection.Value = id;
            }
        }
    }

    private sealed class StreamSelection
    {
        private long _value = -1;

        public long Value
        {
            get => Interlocked.Read(ref _value);
            set => Interlocked.Exchange(ref _value, value);
        }
    }

    private static async Task WriteTextAsync(HttpListenerContext context, int status, string contentType, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private void SafeStop()
    {
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed — nothing to stop.
        }
    }

    public void Dispose() => ((IDisposable)_listener).Dispose();
}
