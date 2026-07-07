using System.Net.Http;
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

    public SnapshotStreamClient(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
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

    /// <summary>Polls the server at <paramref name="intervalMs"/> and pushes each frame to <paramref name="onFrame"/> until cancelled.</summary>
    public async Task StreamAsync(Action<WorldSnapshot> onFrame, int intervalMs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                onFrame(await FetchAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (HttpRequestException)
            {
                // Server not reachable this poll — try again next interval.
            }

            try
            {
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
