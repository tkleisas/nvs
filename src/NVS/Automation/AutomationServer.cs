using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NVS.Automation;

/// <summary>
/// Embedded UI-automation server: a loopback-only TCP listener speaking JSON lines
/// (one request object per line, one response object per line). Started only when
/// requested via <c>--automation-port</c> or the <c>NVS_AUTOMATION_PORT</c> env var —
/// never on a normal launch.
///
/// Request:  {"id":1,"cmd":"screenshot","args":{"path":"C:/tmp/a.png","control":"DatabaseTreeView"}}
/// Response: {"id":1,"ok":true,"result":{...}}  |  {"id":1,"ok":false,"error":"..."}
/// </summary>
public sealed class AutomationServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 256,
    };

    private readonly IAutomationHost _host;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public AutomationServer(IAutomationHost host, int port)
    {
        _host = host;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    /// <summary>The port the server is bound to (useful when started with port 0).</summary>
    public int Port => _listener.LocalEndpoint is IPEndPoint ep ? ep.Port : 0;

    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        Serilog.Log.Information("[Automation] Listening on 127.0.0.1:{Port}", Port);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                // One connection at a time keeps ordering trivial for drivers.
                await HandleConnectionAsync(client, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (SocketException ex)
        {
            Serilog.Log.Debug(ex, "[Automation] Listener stopped");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Automation] Accept loop failed");
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        client.NoDelay = true;

        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        await using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break; // client closed
                if (string.IsNullOrWhiteSpace(line)) continue;

                var response = await DispatchAsync(line, ct).ConfigureAwait(false);
                await writer.WriteLineAsync(response.AsMemory(), ct).ConfigureAwait(false);
            }
        }
        catch (IOException) { /* client went away */ }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    /// <summary>Core dispatch, separated from transport for testability.</summary>
    internal async Task<string> DispatchAsync(string requestLine, CancellationToken ct)
    {
        long id = 0;
        try
        {
            using var doc = JsonDocument.Parse(requestLine);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var parsed) ? parsed : 0;
            var cmd = root.TryGetProperty("cmd", out var cmdEl) ? cmdEl.GetString() : null;

            if (string.IsNullOrWhiteSpace(cmd))
            {
                return Error(id, "missing 'cmd'");
            }

            var args = root.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object
                ? argsEl
                : default;

            ct.ThrowIfCancellationRequested();

            var result = await RunCommandAsync(cmd, args, ct).ConfigureAwait(false);
            return Serialize(new AutomationResponse(id, Ok: true, result, null));
        }
        catch (JsonException ex)
        {
            return Error(id, $"invalid JSON: {ex.Message}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Error(id, ex.Message);
        }
    }

    private async Task<object> RunCommandAsync(string cmd, JsonElement args, CancellationToken ct)
    {
        switch (cmd.ToLowerInvariant())
        {
            case "ping":
                return await _host.PingAsync().ConfigureAwait(false);
            case "state":
                return await _host.GetStateAsync().ConfigureAwait(false);
            case "tree":
                var maxDepth = GetInt(args, "maxDepth", 12);
                var maxNodes = GetInt(args, "maxNodes", 4000);
                var treeControl = GetString(args, "control");
                return await _host.GetTreeAsync(maxDepth, maxNodes, treeControl).ConfigureAwait(false);
            case "screenshot":
                var path = GetString(args, "path") ?? throw new InvalidOperationException("screenshot requires args.path");
                var control = GetString(args, "control");
                var windowTitle = GetString(args, "window");
                return windowTitle is not null
                    ? await _host.ScreenshotWindowAsync(path, windowTitle).ConfigureAwait(false)
                    : await _host.ScreenshotAsync(path, control).ConfigureAwait(false);
            case "command":
                var name = GetString(args, "name") ?? throw new InvalidOperationException("command requires args.name");
                return await _host.InvokeCommandAsync(name).ConfigureAwait(false);
            case "menu":
                var menuPath = GetString(args, "path") ?? throw new InvalidOperationException("menu requires args.path");
                return await _host.InvokeMenuAsync(menuPath).ConfigureAwait(false);
            case "set-text":
                var controlId = GetString(args, "control") ?? throw new InvalidOperationException("set-text requires args.control");
                var text = GetString(args, "text") ?? throw new InvalidOperationException("set-text requires args.text");
                return await _host.SetTextAsync(controlId, text).ConfigureAwait(false);
            case "open-solution":
                var slnPath = GetString(args, "path") ?? throw new InvalidOperationException("open-solution requires args.path");
                return await _host.OpenSolutionAsync(slnPath).ConfigureAwait(false);
            case "open-file":
                var filePath = GetString(args, "path") ?? throw new InvalidOperationException("open-file requires args.path");
                return await _host.OpenFileAsync(filePath).ConfigureAwait(false);
            case "activate":
                var id = GetString(args, "id") ?? throw new InvalidOperationException("activate requires args.id");
                return await _host.ActivateAsync(id).ConfigureAwait(false);
            default:
                throw new InvalidOperationException(
                    $"unknown cmd '{cmd}' (expected: ping, state, tree, screenshot, command, menu, open-solution, open-file, activate, set-text)");
        }
    }

    private static string? GetString(JsonElement args, string property) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static int GetInt(JsonElement args, string property, int fallback) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(property, out var el) && el.TryGetInt32(out var v)
            ? v
            : fallback;

    private static string Error(long id, string message) =>
        Serialize(new AutomationResponse(id, Ok: false, null, message));

    private static string Serialize(AutomationResponse response) =>
        JsonSerializer.Serialize(response, JsonOptions);

    private sealed record AutomationResponse(long Id, bool Ok, object? Result, string? Error);

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
