using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Playli.PcScanner;

public sealed class LoopbackBridge : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly byte[] report;
    private readonly TimeSpan lifetime;
    private readonly CancellationTokenSource shutdown = new();
    private readonly object lifecycle = new();
    private Task<bool>? serving;
    private bool disposed;
    private int served;
    private static readonly HashSet<string> Origins = ["https://playli.co", "https://www.playli.co"];
    public int Port { get; }
    public string Token { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public Uri ReturnUri => new("https://playli.co/pages/can-you-run-it#pc-check=" + Port + "." + Token);

    public LoopbackBridge(HardwareReport hardware, TimeSpan? lifetime = null)
    {
        report = Encoding.UTF8.GetBytes(hardware.ToJson());
        if (report.Length > 65536) throw new InvalidOperationException("Report exceeds transfer limit.");
        var requested = lifetime ?? TimeSpan.FromSeconds(120);
        this.lifetime = requested > TimeSpan.FromSeconds(120) ? TimeSpan.FromSeconds(120) : requested;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(8);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public Task<bool> ServeAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycle)
        {
            if (disposed) return Task.FromResult(false);
            return serving ??= ServeOnceAsync(cancellationToken);
        }
    }

    private async Task<bool> ServeOnceAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        timeout.CancelAfter(lifetime);
        try
        {
            for (var attempts = 0; attempts < 64 && !timeout.IsCancellationRequested; attempts++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    if (await HandleAsync(client.GetStream(), requestTimeout.Token)) return true;
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or SocketException)
                {
                    // An authorized response may already have been partly sent.
                    // End this attempt instead of reusing its consumed token or
                    // leaving the UI waiting for the full sharing lifetime.
                    if (Volatile.Read(ref served) != 0) return false;
                }
            }
            return false;
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { return false; }
        finally { listener.Stop(); }
    }

    private async Task<bool> HandleAsync(NetworkStream stream, CancellationToken cancel)
    {
        var buffer = new byte[1024];
        using var headers = new MemoryStream();
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancel);
            if (count == 0) return false;
            if (headers.Length + count > 8192) { await Reply(stream, 431, null, null, cancel); return false; }
            headers.Write(buffer, 0, count);
            if (Encoding.ASCII.GetString(headers.GetBuffer(), 0, (int)headers.Length).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
        }
        var raw = Encoding.ASCII.GetString(headers.ToArray());
        var end = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (end + 4 != raw.Length) { await Reply(stream, 400, null, null, cancel); return false; }
        var lines = raw[..end].Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3 || first[2] != "HTTP/1.1" || lines.Length > 32) { await Reply(stream, 400, null, null, cancel); return false; }
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon < 1 || line[0] == ' ' || line[0] == '\t' || !fields.TryAdd(line[..colon], line[(colon + 1)..].Trim()))
            { await Reply(stream, 400, null, null, cancel); return false; }
        }
        if (first[1] != "/v1/specs") { await Reply(stream, 404, null, null, cancel); return false; }
        fields.TryGetValue("Origin", out var origin);
        fields.TryGetValue("Host", out var host);
        if (origin is null || !Origins.Contains(origin) || host != "127.0.0.1:" + Port)
        { await Reply(stream, 403, null, null, cancel); return false; }
        if (fields.ContainsKey("Transfer-Encoding") || (fields.TryGetValue("Content-Length", out var length) && length != "0"))
        { await Reply(stream, 400, origin, null, cancel); return false; }
        if (first[0] == "OPTIONS")
        {
            fields.TryGetValue("Access-Control-Request-Method", out var method);
            fields.TryGetValue("Access-Control-Request-Headers", out var requestedHeaders);
            if (method != "GET" || !string.Equals(requestedHeaders, "authorization", StringComparison.OrdinalIgnoreCase))
            { await Reply(stream, 403, origin, null, cancel); return false; }
            await Reply(stream, 204, origin, null, cancel);
            return false;
        }
        if (first[0] != "GET") { await Reply(stream, 405, origin, null, cancel); return false; }
        fields.TryGetValue("Authorization", out var auth);
        var expected = Encoding.ASCII.GetBytes("Bearer " + Token);
        if (auth is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(auth), expected))
        { await Reply(stream, 403, origin, null, cancel); return false; }
        if (Interlocked.CompareExchange(ref served, 1, 0) != 0) { await Reply(stream, 410, origin, null, cancel); return false; }
        await Reply(stream, 200, origin, report, cancel);
        return true;
    }

    private static async Task Reply(NetworkStream stream, int code, string? origin, byte[]? payload, CancellationToken cancel)
    {
        var body = payload ?? [];
        var reason = code switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", 410 => "Gone", _ => "Request Header Fields Too Large" };
        var headers = "HTTP/1.1 " + code + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n";
        if (origin is not null) headers += "Access-Control-Allow-Origin: " + origin + "\r\nVary: Origin\r\nAccess-Control-Allow-Methods: GET, OPTIONS\r\nAccess-Control-Allow-Headers: Authorization\r\nAccess-Control-Allow-Private-Network: true\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"), cancel);
        if (body.Length > 0) await stream.WriteAsync(body, cancel);
        await stream.FlushAsync(cancel);
    }

    public async ValueTask DisposeAsync()
    {
        Task<bool>? pending;
        bool ownsDisposal;
        lock (lifecycle)
        {
            ownsDisposal = !disposed;
            disposed = true;
            pending = serving;
            if (ownsDisposal) { shutdown.Cancel(); listener.Stop(); }
        }
        if (pending is not null) await pending;
        if (ownsDisposal) shutdown.Dispose();
    }
}
