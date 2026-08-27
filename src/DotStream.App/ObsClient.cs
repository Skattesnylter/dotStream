using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotStream.App;

/// <summary>
/// OBS Studio over its built-in obs-websocket.
///
/// Measured against obs-websocket 5.7.4 on OBS 32.2.2. The server is off by default,
/// so the first thing anyone has to do is Tools, WebSocket Server Settings, Enable.
/// Port and password are read from the OBS config file rather than typed in twice.
///
/// The reason this beats sending a hotkey is that it reports state. A scene key lit
/// when its scene is live, or a mute key lit when actually muted, is something no
/// key-sending path can do. Same argument as Discord RPC events, and the opposite of
/// firing a keystroke and hoping.
/// </summary>
public sealed class ObsClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _pending = [];
    private readonly CancellationTokenSource _stopping = new();

    private ClientWebSocket? _socket;
    private Task? _reader;
    private int _nextId;

    /// <summary>Where OBS keeps the port and the generated password.</summary>
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "obs-studio", "plugin_config", "obs-websocket", "config.json");

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    /// <summary>Raised when OBS reports a change, with the event type and its data.</summary>
    public event EventHandler<ObsEventArgs>? Event;

    /// <summary>Raised when the connection drops, so callers can grey out their keys.</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Reads the OBS settings. Null when OBS has never run, and Enabled is false when
    /// the websocket server has not been switched on - which is not a fault worth
    /// reporting loudly, because it is the default state.
    /// </summary>
    public static ObsConnectionInfo? ReadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;

            JsonNode? node = JsonNode.Parse(File.ReadAllText(ConfigPath));
            if (node is null) return null;

            return new ObsConnectionInfo(
                node["server_port"]?.GetValue<int>() ?? 4455,
                node["server_password"]?.GetValue<string>() ?? "",
                node["server_enabled"]?.GetValue<bool>() ?? false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    public async Task ConnectAsync(int port, string password, CancellationToken ct = default)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), ct);

        JsonNode hello = await ReceiveOneAsync(socket, ct)
                         ?? throw new InvalidOperationException("OBS did not send Hello.");

        var identify = new JsonObject
        {
            ["op"] = 1,
            ["d"] = new JsonObject { ["rpcVersion"] = hello["d"]!["rpcVersion"]!.GetValue<int>() }
        };

        if (hello["d"]!["authentication"] is { } auth)
        {
            string salt = auth["salt"]!.GetValue<string>();
            string challenge = auth["challenge"]!.GetValue<string>();

            // base64(sha256(base64(sha256(password + salt)) + challenge)), per the spec.
            identify["d"]!["authentication"] = Hash(Hash(password + salt) + challenge);
        }

        await SendRawAsync(socket, identify, ct);

        JsonNode identified = await ReceiveOneAsync(socket, ct)
                              ?? throw new InvalidOperationException("OBS closed the connection during identify.");

        if (identified["op"]?.GetValue<int>() != 2)
            throw new InvalidOperationException("OBS refused the connection. Check the password in its WebSocket settings.");

        _socket = socket;
        _reader = Task.Run(ReadLoopAsync, CancellationToken.None);
    }

    /// <summary>Sends a request and waits for the response that carries its id.</summary>
    public async Task<JsonNode?> CallAsync(string type, JsonObject? data = null, CancellationToken ct = default)
    {
        ClientWebSocket socket = _socket ?? throw new InvalidOperationException("Not connected to OBS.");

        string id = Interlocked.Increment(ref _nextId).ToString();
        var waiter = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pending) _pending[id] = waiter;

        var request = new JsonObject
        {
            ["op"] = 6,
            ["d"] = new JsonObject
            {
                ["requestType"] = type,
                ["requestId"] = id,
                ["requestData"] = data ?? []
            }
        };

        await SendRawAsync(socket, request, ct);

        // Bounded. A request that never comes back would otherwise hang the key that
        // sent it for as long as the application runs.
        try
        {
            return await waiter.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        finally
        {
            lock (_pending) _pending.Remove(id);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_socket is { State: WebSocketState.Open } socket && !_stopping.IsCancellationRequested)
            {
                JsonNode? message = await ReceiveOneAsync(socket, _stopping.Token);
                if (message is null) break;

                switch (message["op"]?.GetValue<int>())
                {
                    case 5 when message["d"] is { } raised:
                        Event?.Invoke(this, new ObsEventArgs(
                            raised["eventType"]?.GetValue<string>() ?? "",
                            raised["eventData"]));
                        break;

                    case 7 when message["d"] is { } response:
                        string id = response["requestId"]?.GetValue<string>() ?? "";
                        TaskCompletionSource<JsonNode?>? waiter;

                        lock (_pending) _pending.Remove(id, out waiter);

                        waiter?.TrySetResult(response["responseData"]);
                        break;
                }
            }
        }
        catch (Exception)
        {
            // Closing OBS is a normal end to this loop, not a fault.
        }
        finally
        {
            // Nothing is coming for anyone still waiting.
            lock (_pending)
            {
                foreach (TaskCompletionSource<JsonNode?> waiter in _pending.Values)
                    waiter.TrySetCanceled();

                _pending.Clear();
            }

            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task SendRawAsync(ClientWebSocket socket, JsonNode message, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString());

        await _send.WaitAsync(ct);

        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally { _send.Release(); }
    }

    private static async Task<JsonNode?> ReceiveOneAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new ArraySegment<byte>(new byte[8192]);
        using var stream = new MemoryStream();

        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;

            stream.Write(buffer.Array!, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return stream.Length == 0 ? null : JsonNode.Parse(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string Hash(string input) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        if (_socket is { } socket)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            }
            catch (Exception) { /* it is going away regardless */ }

            socket.Dispose();
            _socket = null;
        }

        if (_reader is not null)
        {
            try { await _reader.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (Exception) { }
        }

        _stopping.Dispose();
        _send.Dispose();
    }
}

/// <summary>Port, password and whether the server is switched on at all.</summary>
public sealed record ObsConnectionInfo(int Port, string Password, bool Enabled);

public sealed class ObsEventArgs(string type, JsonNode? data) : EventArgs
{
    public string Type { get; } = type;

    public JsonNode? Data { get; } = data;
}
