using System.Net;
using System.Net.WebSockets;
using System.Text;
using LifeSim.Core.Snapshot;

namespace LifeSim.Console.Serve;

/// <summary>
/// A minimal <see cref="HttpListener"/> transport over a <see cref="SnapshotService"/> (lifesim.md §1),
/// using only BCL types (no ASP.NET) so the console stays lightweight. Endpoints:
/// <list type="bullet">
/// <item><c>GET /snapshot</c> — the current world snapshot (JSON).</item>
/// <item><c>POST /snapshot</c> — import an edited snapshot as the new world state.</item>
/// <item><c>GET /metrics</c> — the current tick's metrics (one NDJSON line).</item>
/// <item><c>GET /health</c> — liveness check.</item>
/// <item><c>GET /stream</c> — a WebSocket that pushes a snapshot frame whenever the tick advances.</item>
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
        long lastTick = -1;

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                long tick = _service.Tick;
                if (tick != lastTick)
                {
                    lastTick = tick;
                    byte[] frame = Encoding.UTF8.GetBytes(_service.CurrentSnapshotJson());
                    await socket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
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
