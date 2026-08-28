using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotStream.App;

/// <summary>
/// Discord over its local RPC pipe.
///
/// Measured end to end on 27.08.2026, and three things everyone assumes about this
/// turned out to be wrong. There is no whitelist: an application registered ten
/// minutes earlier was granted the voice scopes. Camera and screen share are not
/// partner-only, they are ordinary scopes in the portal. And no client secret is
/// needed, because Discord supports PKCE for public clients, which is exactly what a
/// desktop application is.
///
/// The reason it looks closed is that every error message points away from its own
/// fix. AUTHORIZE without a redirect URI says one is missing from the request, when
/// it means the application has none registered. Sending one explicitly is refused
/// outright. See TODO.md section 9 for the full sequence.
///
/// Worth it because Discord reports state. A mute key lit when actually muted is
/// something no keystroke can manage, and the same argument as the OBS keys.
/// </summary>
public sealed class DiscordClient : IAsyncDisposable
{
    /// <summary>
    /// dotStream's registered application. Public by design: a public client has no
    /// secret to protect, and everyone sharing one application is the intended shape
    /// rather than something to hide.
    /// </summary>
    public const string ClientId = "1542622058679373855";

    /// <summary>Never navigated to. It only has to exist on the application.</summary>
    private const string RedirectUri = "https://github.com/Skattesnylter/dotStream";

    /// <summary>
    /// Camera and screen share are here because they are ordinary scopes, which was
    /// worth measuring rather than assuming: TOGGLE_VIDEO and TOGGLE_SCREENSHARE answer
    /// 4006 "invalid scope" while a made-up command answers 4002 "invalid command", so
    /// the commands exist and only the permission was missing.
    /// </summary>
    private static readonly string[] Scopes =
    [
        "rpc",
        "rpc.voice.read", "rpc.voice.write",
        "rpc.video.read", "rpc.video.write",
        "rpc.screenshare.read", "rpc.screenshare.write",
    ];

    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _pending = [];
    private readonly CancellationTokenSource _stopping = new();

    private NamedPipeClientStream? _pipe;
    private Task? _reader;
    private int _nextNonce;

    // READY arrives as a dispatch with no nonce, so it cannot be awaited like a reply.
    private TaskCompletionSource<bool>? _ready;

    public bool IsConnected => _pipe?.IsConnected == true && IsAuthenticated;

    /// <summary>True once a token has been accepted and the voice commands will work.</summary>
    public bool IsAuthenticated { get; private set; }

    /// <summary>Raised on every dispatched event, with its name and payload.</summary>
    public event EventHandler<DiscordEventArgs>? Event;

    public event EventHandler? Closed;

    /// <summary>
    /// Connects, and authorises if there is no usable token yet.
    ///
    /// The first run asks Discord for permission, which the user approves in the
    /// Discord window. Afterwards the stored token is used and nothing is asked.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Discord opens up to ten pipe instances because several programs talk to it at
        // once, and a client is expected to work down the list. Trying only
        // discord-ipc-0 fails with a timeout the moment anything else got there first,
        // which reads as "Discord is not running" and is not.
        NamedPipeClientStream? pipe = null;

        for (int instance = 0; instance < 10 && pipe is null; instance++)
        {
            var candidate = new NamedPipeClientStream(
                ".", $"discord-ipc-{instance}", PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                // Short: an instance is either free now or somebody else has it.
                await candidate.ConnectAsync(500, ct);
                pipe = candidate;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                candidate.Dispose();
            }
        }

        if (pipe is null)
            throw new TimeoutException("No free Discord pipe. Is Discord running?");

        _pipe = pipe;
        _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader = Task.Run(ReadLoopAsync, CancellationToken.None);

        // The handshake is opcode 0 rather than a command, so it has no nonce and its
        // reply is the READY dispatch.
        await SendFrameAsync(0, new JsonObject { ["v"] = 1, ["client_id"] = ClientId }, ct);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

        await AuthenticateAsync(ct);
    }

    /// <summary>
    /// Uses the stored token, refreshes it, or asks the user, in that order.
    ///
    /// Only the last of those shows anything. A returning user sees nothing at all,
    /// which is the entire point of storing it.
    /// </summary>
    private async Task AuthenticateAsync(CancellationToken ct)
    {
        DiscordToken? token = DiscordTokens.Load();

        if (token is { IsUsable: true } && await TryAuthenticateAsync(token.AccessToken, ct)) return;

        if (token is not null && await RefreshAsync(token.RefreshToken, ct) is { } refreshed)
        {
            DiscordTokens.Save(refreshed);
            if (await TryAuthenticateAsync(refreshed.AccessToken, ct)) return;
        }

        // Nothing stored, or nothing that still works. Asking puts a prompt in Discord.
        DiscordToken issued = await AuthorizeAsync(ct);
        DiscordTokens.Save(issued);

        if (!await TryAuthenticateAsync(issued.AccessToken, ct))
            throw new InvalidOperationException("Discord accepted the authorisation and then refused the token.");
    }

    private async Task<bool> TryAuthenticateAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            await CallAsync("AUTHENTICATE", new JsonObject { ["access_token"] = accessToken }, ct);
            IsAuthenticated = true;
            return true;
        }
        catch (Exception ex)
        {
            DeckLog.Note("discord", "token rejected: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// The full PKCE authorisation, which is what makes this work without a secret.
    ///
    /// Note what is and is not sent. The code challenge goes in; the redirect URI does
    /// not, even though one must exist on the application. Sending it is refused with
    /// "Redirect URI cannot be used in the RPC OAuth2 Authorization flow", and leaving
    /// it off an application that has none registered fails claiming it is missing
    /// from the request. Two errors that each point away from the fix.
    /// </summary>
    private async Task<DiscordToken> AuthorizeAsync(CancellationToken ct)
    {
        string verifier = CreateVerifier();

        JsonNode? granted = await CallAsync("AUTHORIZE", new JsonObject
        {
            ["client_id"] = ClientId,
            ["scopes"] = new JsonArray([.. Scopes.Select(s => (JsonNode)s!)]),
            ["code_challenge"] = Challenge(verifier),
            ["code_challenge_method"] = "S256"
        }, ct);

        string code = granted?["code"]?.GetValue<string>()
                      ?? throw new InvalidOperationException("Discord did not return an authorisation code.");

        return await ExchangeAsync(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = RedirectUri
        }, ct);
    }

    private async Task<DiscordToken?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (refreshToken.Length == 0) return null;

        try
        {
            return await ExchangeAsync(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }, ct);
        }
        catch (Exception ex)
        {
            DeckLog.Note("discord", "refresh failed: " + ex.Message);
            return null;
        }
    }

    private static async Task<DiscordToken> ExchangeAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        HttpResponseMessage response = await http.PostAsync(
            "https://discord.com/api/oauth2/token", new FormUrlEncodedContent(form), ct);

        string text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed ({(int)response.StatusCode}): {text}");

        JsonNode node = JsonNode.Parse(text)
                        ?? throw new InvalidOperationException("The token response was not JSON.");

        return new DiscordToken(
            node["access_token"]!.GetValue<string>(),
            node["refresh_token"]?.GetValue<string>() ?? "",
            DateTime.UtcNow.AddSeconds(node["expires_in"]?.GetValue<int>() ?? 604800));
    }

    /// <summary>64 characters from the unreserved set, per RFC 7636.</summary>
    private static string CreateVerifier()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

        return string.Create(64, alphabet, static (span, chars) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Sends a command and waits for the reply carrying its nonce.</summary>
    public async Task<JsonNode?> CallAsync(string command, JsonObject? args = null, CancellationToken ct = default)
    {
        string nonce = Interlocked.Increment(ref _nextNonce).ToString();
        var waiter = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pending) _pending[nonce] = waiter;

        try
        {
            await SendFrameAsync(1, new JsonObject
            {
                ["cmd"] = command,
                ["args"] = args ?? [],
                ["nonce"] = nonce
            }, ct);

            return await waiter.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
        finally
        {
            lock (_pending) _pending.Remove(nonce);
        }
    }

    /// <summary>Asks to be told when something changes.</summary>
    public Task SubscribeAsync(string eventName, CancellationToken ct = default)
    {
        string nonce = Interlocked.Increment(ref _nextNonce).ToString();

        return SendFrameAsync(1, new JsonObject
        {
            ["cmd"] = "SUBSCRIBE",
            ["evt"] = eventName,
            ["args"] = new JsonObject(),
            ["nonce"] = nonce
        }, ct);
    }

    private async Task SendFrameAsync(int opcode, JsonNode payload, CancellationToken ct)
    {
        NamedPipeClientStream pipe = _pipe ?? throw new InvalidOperationException("Not connected to Discord.");

        byte[] body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + body.Length];

        BitConverter.GetBytes(opcode).CopyTo(frame, 0);
        BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
        body.CopyTo(frame, 8);

        await _send.WaitAsync(ct);

        try { await pipe.WriteAsync(frame, ct); }
        finally { _send.Release(); }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_pipe is { IsConnected: true } pipe && !_stopping.IsCancellationRequested)
            {
                var header = new byte[8];
                if (!await ReadExactlyAsync(pipe, header, _stopping.Token)) break;

                int length = BitConverter.ToInt32(header, 4);
                if (length is <= 0 or > 1_000_000) break;

                var body = new byte[length];
                if (!await ReadExactlyAsync(pipe, body, _stopping.Token)) break;

                if (JsonNode.Parse(Encoding.UTF8.GetString(body)) is not { } message) continue;

                string? nonce = message["nonce"]?.GetValue<string>();

                if (nonce is not null)
                {
                    TaskCompletionSource<JsonNode?>? waiter;
                    lock (_pending) _pending.Remove(nonce, out waiter);

                    if (waiter is not null)
                    {
                        // An error reply carries evt: ERROR and a message worth showing.
                        if (message["evt"]?.GetValue<string>() == "ERROR")
                        {
                            string reason = message["data"]?["message"]?.GetValue<string>() ?? "refused";
                            waiter.TrySetException(new InvalidOperationException("Discord: " + reason));
                        }
                        else waiter.TrySetResult(message["data"]);

                        continue;
                    }
                }

                if (message["evt"]?.GetValue<string>() is not { Length: > 0 } name) continue;

                if (name == "READY") _ready?.TrySetResult(true);
                else Event?.Invoke(this, new DiscordEventArgs(name, message["data"]));
            }
        }
        catch (Exception)
        {
            // Closing Discord ends this loop, which is not a fault.
        }
        finally
        {
            IsAuthenticated = false;

            lock (_pending)
            {
                foreach (TaskCompletionSource<JsonNode?> waiter in _pending.Values) waiter.TrySetCanceled();
                _pending.Clear();
            }

            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;

        while (read < buffer.Length)
        {
            int got = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (got == 0) return false;

            read += got;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        _pipe?.Dispose();
        _pipe = null;

        if (_reader is not null)
        {
            try { await _reader.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (Exception) { }
        }

        _stopping.Dispose();
        _send.Dispose();
    }
}

public sealed class DiscordEventArgs(string name, JsonNode? data) : EventArgs
{
    public string Name { get; } = name;

    public JsonNode? Data { get; } = data;
}
