using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using DotStream.Core;

namespace DotStream.App.Mcp;

/// <summary>
/// An MCP server exposing the deck to AI agents, over JSON-RPC on HTTP.
///
/// HttpListener rather than a web framework, because the project carries no NuGet
/// packages and this needs one endpoint. Bound to 127.0.0.1 only and never to a
/// wildcard: a physical button that an agent can put a question on is not something
/// to publish to the network. Loopback also means no urlacl and no elevation.
/// </summary>
public sealed class McpServer : IAsyncDisposable
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly IDeckAgent _agent;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public McpServer(IDeckAgent agent) => _agent = agent;

    public bool IsRunning => _listener?.IsListening == true;

    public int Port { get; private set; }

    public string Url => $"http://127.0.0.1:{Port}/";

    public event EventHandler<string>? Log;

    public void Start(int port)
    {
        Stop();

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        _listener = listener;
        Port = port;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptAsync(listener, _cts.Token));

        Log?.Invoke(this, $"MCP server listening on {Url}");
    }

    public void Stop()
    {
        _cts?.Cancel();

        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // Shutting down; nothing useful to report.
        }

        _listener = null;
    }

    private async Task AcceptAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            // Deliberately not awaited: deck_ask blocks until somebody presses a key,
            // and that must not stop the server answering anything else.
            _ = Task.Run(() => HandleAsync(context), ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod == "GET")
            {
                // A Streamable HTTP client opens a GET to listen for server-initiated
                // messages. This server never sends any, and the spec says to decline
                // with 405 - answering with the HTML page instead leaves the client
                // waiting on a stream that will never carry anything.
                string accept = context.Request.Headers["Accept"] ?? "";

                if (accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(context, 405, "text/plain", "This server does not stream.");
                    return;
                }

                await WriteAsync(context, 200, "text/html; charset=utf-8", InstructionPage());
                return;
            }

            // Session termination. Nothing is kept per session, so there is nothing
            // to tear down - but saying so beats leaving the client guessing.
            if (context.Request.HttpMethod == "DELETE")
            {
                await WriteAsync(context, 204, "text/plain", "");
                return;
            }

            if (context.Request.HttpMethod != "POST")
            {
                await WriteAsync(context, 405, "text/plain", "Method not allowed");
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();

            JsonNode? request = JsonNode.Parse(body);
            if (request is null)
            {
                await WriteAsync(context, 400, "application/json", Error(null, -32700, "Parse error"));
                return;
            }

            string method = request["method"]?.GetValue<string>() ?? "";
            JsonNode? id = request["id"];

            string detail = method == "tools/call"
                ? $"{method}  {request["params"]?["name"]?.GetValue<string>()}"
                : method;

            DeckLog.In("mcp:in", detail);

            // Notifications carry no id and expect no body back.
            if (id is null)
            {
                await WriteAsync(context, 202, "application/json", "");
                return;
            }

            string response = await DispatchAsync(method, request["params"], id);

            DeckLog.Out("mcp:out", Summarise(response));
            await WriteAsync(context, 200, "application/json", response);
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, "MCP request failed: " + ex.Message);

            try
            {
                await WriteAsync(context, 500, "application/json", Error(null, -32603, ex.Message));
            }
            catch
            {
                // The client has probably gone.
            }
        }
    }

    private async Task<string> DispatchAsync(string method, JsonNode? parameters, JsonNode? id) => method switch
    {
        "initialize" => Result(id, new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = "dotStream", ["version"] = "0.1.0" }
        }),

        "ping" => Result(id, new JsonObject()),

        "tools/list" => Result(id, new JsonObject { ["tools"] = ToolSchemas() }),

        "tools/call" => await CallToolAsync(parameters, id),

        _ => Error(id, -32601, $"Unknown method '{method}'")
    };

    private async Task<string> CallToolAsync(JsonNode? parameters, JsonNode? id)
    {
        string name = parameters?["name"]?.GetValue<string>() ?? "";
        JsonNode? arguments = parameters?["arguments"];

        try
        {
            return name switch
            {
                "deck_ask" => await AskAsync(arguments, id),
                "deck_notify" => Notify(arguments, id),
                "deck_set_key" => SetKey(arguments, id),
                "deck_propose_page" => await ProposePageAsync(arguments, id),
                "deck_status" => Status(id),
                "deck_integrations" => ToolResult(id, await _agent.DescribeIntegrationsAsync()),
                _ => Error(id, -32602, $"Unknown tool '{name}'")
            };
        }
        catch (Exception ex)
        {
            // Per the MCP spec a tool that fails reports isError in its result rather
            // than raising a protocol-level error, so the model can read what broke.
            return ToolResult(id, "Tool failed: " + ex.Message, isError: true);
        }
    }

    private async Task<string> AskAsync(JsonNode? arguments, JsonNode? id)
    {
        string question = arguments?["question"]?.GetValue<string>() ?? "";
        if (question.Length == 0) return ToolResult(id, "question is required", isError: true);

        var options = new List<string>();

        if (arguments?["options"] is JsonArray array)
        {
            foreach (JsonNode? entry in array)
            {
                string? text = entry?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text)) options.Add(text.Trim());
            }
        }

        if (options.Count == 0) return ToolResult(id, "at least one option is required", isError: true);

        if (options.Count > DeckLayout.LastKey)
            return ToolResult(id, $"at most {DeckLayout.LastKey} options fit on the deck", isError: true);

        double seconds = arguments?["timeout_seconds"]?.GetValue<double>() ?? 300;
        AskResult result = await _agent.AskAsync(question, options, TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 3600)));

        return result.Answered
            ? ToolResult(id, $"{result.Choice}\n\n(option {result.Index + 1} of {options.Count}, chosen on the deck)")
            : ToolResult(id, result.Reason, isError: true);
    }

    private async Task<string> ProposePageAsync(JsonNode? arguments, JsonNode? id)
    {
        string page = arguments?["page"]?.GetValue<string>() ?? "";
        if (page.Length == 0) return ToolResult(id, "page is required", isError: true);

        var keys = new List<ProposedKey>();

        if (arguments?["keys"] is JsonArray array)
        {
            foreach (JsonNode? entry in array)
            {
                string? label = entry?["label"]?.GetValue<string>();
                string? hotkey = entry?["hotkey"]?.GetValue<string>();

                ProposedDiscord? discord = null;

                if (entry?["discord"] is JsonObject d &&
                    d["action"]?.GetValue<string>() is { Length: > 0 } discordAction)
                {
                    discord = new ProposedDiscord(
                        discordAction.Trim(), d["target"]?.GetValue<string>()?.Trim() ?? "");
                }

                ProposedObs? obs = null;

                if (entry?["obs"] is JsonObject o &&
                    o["action"]?.GetValue<string>() is { Length: > 0 } action)
                {
                    obs = new ProposedObs(action.Trim(), o["target"]?.GetValue<string>()?.Trim() ?? "");
                }

                // A key needs something to do, and a name unless it names itself. The
                // self-filling Discord keys take their label from whichever channel
                // they currently point at, so requiring one here rejected exactly the
                // proposals that were correct.
                bool namesItself = discord?.Action is "ChannelSlot" or "CurrentChannel";

                if (string.IsNullOrWhiteSpace(label) && !namesItself) continue;
                if (string.IsNullOrWhiteSpace(hotkey) && obs is null && discord is null) continue;

                int? index = entry?["index"] is { } slot && slot.GetValue<int>() is >= 1 and <= 15
                    ? slot.GetValue<int>()
                    : null;

                keys.Add(new ProposedKey(label.Trim(), hotkey?.Trim() ?? "",
                    entry?["icon"]?.GetValue<string>()?.Trim() ?? "", index, obs, discord));
            }
        }

        if (keys.Count == 0) return ToolResult(id, "at least one key is required", isError: true);

        // Three cells carry Later, Accept and Reject while the proposal is on screen,
        // which leaves twelve of the fifteen. This used to say thirteen, and the
        // thirteenth was accepted here and then dropped silently further down.
        const int maximum = 12;

        if (keys.Count > maximum)
            return ToolResult(id, $"at most {maximum} keys fit alongside accept and reject", isError: true);

        string? target = arguments?["target_page"]?.GetValue<string>()?.Trim();
        if (target?.Length == 0) target = null;

        double seconds = arguments?["timeout_seconds"]?.GetValue<double>() ?? 300;
        AskResult result = await _agent.ProposePageAsync(
            page, keys, TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 3600)), target);

        if (!result.Answered) return ToolResult(id, result.Reason, isError: true);

        // "Later" is not a refusal and must not read like one, or an agent told to come
        // back will instead conclude the idea was unwanted and drop it.
        if (result.Index == 2)
            return ToolResult(id,
                "Not now - the user is busy and asked to be shown this again later. "
                + "The proposal was not rejected: keep it, and offer it again when they "
                + "are next at a natural stopping point, or when they ask what is pending.");

        if (result.Index != 0) return ToolResult(id, "The user rejected the proposal.", isError: true);

        return ToolResult(id, target is null
            ? $"Accepted. The \"{page}\" page now has {keys.Count} keys and has been saved."
            : $"Accepted. {keys.Count} key(s) were added to the existing \"{target}\" page and saved.");
    }

    private string Notify(JsonNode? arguments, JsonNode? id)
    {
        string text = arguments?["text"]?.GetValue<string>() ?? "";
        if (text.Length == 0) return ToolResult(id, "text is required", isError: true);

        int? cell = arguments?["cell"]?.GetValue<int>();
        Color? colour = ColorCodec.Parse(arguments?["colour"]?.GetValue<string>()
                                         ?? arguments?["color"]?.GetValue<string>());

        _agent.Notify(text, cell, colour);
        return ToolResult(id, "Shown on the deck.");
    }

    private string SetKey(JsonNode? arguments, JsonNode? id)
    {
        int index = arguments?["index"]?.GetValue<int>() ?? 0;
        if (!DeckLayout.IsKey(index))
            return ToolResult(id, $"index must be a key, {DeckLayout.FirstKey}-{DeckLayout.LastKey}", isError: true);

        string label = arguments?["label"]?.GetValue<string>() ?? "";
        Color? colour = ColorCodec.Parse(arguments?["colour"]?.GetValue<string>()
                                         ?? arguments?["color"]?.GetValue<string>());

        _agent.SetKey(index, label, colour);
        return ToolResult(id, $"Key {index} updated on the agent page.");
    }

    private string Status(JsonNode? id)
    {
        DeckStatus status = _agent.Status();

        var text = new StringBuilder();
        text.AppendLine($"transport: {status.Transport}");
        text.AppendLine($"page: {status.Page} (depth {status.Depth})");
        text.AppendLine($"following focused app: {(status.FollowingFocus ? "yes" : "no")}");
        if (status.NowPlaying is { } playing) text.AppendLine($"now playing: {playing}");

        return ToolResult(id, text.ToString().TrimEnd());
    }

    private static JsonArray ToolSchemas() =>
    [
        Tool("deck_ask",
            "Ask the user a question and wait for them to answer by pressing a physical key. " +
            "Blocks until a key is pressed or the timeout expires. Use this when you hit a " +
            "decision the user should make - they answer on the deck without switching windows.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["question"] = Property("string", "What you are asking. Shown in the dotStream window."),
                    ["options"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" },
                        ["description"] = "One per key. Keep them to a word or two - a key is 85 pixels wide."
                    },
                    ["timeout_seconds"] = Property("number", "Default 300, clamped to 5-3600.")
                },
                ["required"] = new JsonArray { "question", "options" }
            }),

        Tool("deck_notify",
            "Show a short message on one of the three info cells for a few seconds.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["text"] = Property("string", "A few characters. The cell is 85 pixels square."),
                    ["cell"] = Property("integer", "16, 17 or 18. Defaults to 18."),
                    ["colour"] = Property("string", "Hex like #4DD9E8.")
                },
                ["required"] = new JsonArray { "text" }
            }),

        Tool("deck_set_key",
            "Draw a label on a key of the agent's own page and show that page. " +
            "This changes appearance only - it cannot bind a key to an action.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["index"] = Property("integer", "Key 1-15."),
                    ["label"] = Property("string", "Short text. Empty clears the key."),
                    ["colour"] = Property("string", "Hex like #4DD9E8.")
                },
                ["required"] = new JsonArray { "index", "label" }
            }),

        Tool("deck_propose_page",
            "Offer the user a whole page of hotkeys - for example the shortcuts they use "
            + "most in a particular application. The proposal is shown on the deck and the "
            + "user accepts or rejects it by pressing a key; nothing is saved unless they "
            + "accept. This is the only way an agent can create keys that do something.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["page"] = Property("string", "Page name, usually the application: \"Excel\"."),
                    ["keys"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["description"] =
                            "Up to 12. Three of the fifteen keys carry Later, Accept and "
                            + "Reject while the proposal is on the deck.",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["label"] = Property("string",
                                    "A word or two - the key is 100 pixels wide. Optional for the "
                                    + "self-filling Discord keys, which take the name of whichever "
                                    + "channel they currently point at."),
                                ["hotkey"] = Property("string",
                                    "Like \"Ctrl+S\", \"Alt+=\" or \"Ctrl+Shift+L\". Commas make it a "
                                    + "sequence, pressed in order - use this for ribbon commands that "
                                    + "have no shortcut of their own, such as \"Alt, H, M, C\" for "
                                    + "Merge and Centre in Excel. A step in double quotes is typed "
                                    + "rather than pressed, which is how a command palette is reached: "
                                    + "Ctrl+Shift+P, \\\"Developer: Reload Window\\\", Enter."),
                                ["icon"] = Property("string",
                                    "Optional icon name. One of: " + string.Join(", ",
                                        DotStream.Rendering.IconLibrary.All.Select(i => i.Name))
                                    + ". Left out, one is guessed from the label."),
                                ["index"] = Property("number",
                                    "Optional physical key, 1-15. Left out, the next free one is used."),
                                ["obs"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["description"] =
                                        "Instead of a hotkey, drive OBS Studio directly. Only works "
                                        + "while OBS is running with its websocket server on - check "
                                        + "deck_status first. Unlike a hotkey, these keys light up "
                                        + "when what they control is live.",
                                    ["properties"] = new JsonObject
                                    {
                                        ["action"] = Property("string",
                                            "SwitchScene, ToggleRecord, ToggleStream or ToggleMute."),
                                        ["target"] = Property("string",
                                            "Scene name for SwitchScene, audio source name for "
                                            + "ToggleMute. Not used by the others.")
                                    },
                                    ["required"] = new JsonArray { "action" }
                                },
                                ["discord"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["description"] =
                                        "Instead of a hotkey, drive Discord directly. Only works while "
                                        + "Discord is running. These keys light up when the microphone "
                                        + "is actually muted, including when the user muted themselves "
                                        + "inside Discord.",
                                    ["properties"] = new JsonObject
                                    {
                                        ["action"] = Property("string",
                                            "ToggleMute, ToggleDeafen, LeaveVoice, JoinChannel, "
                                            + "ToggleVideo, ToggleScreenshare, "
                                            + "ChannelSlot or CurrentChannel. The last two fill "
                                            + "themselves from whichever server the user is in, so a "
                                            + "row of ChannelSlot keys follows them between servers "
                                            + "without a page per server."),
                                        ["target"] = Property("string",
                                            "Voice channel id for JoinChannel, from deck_integrations. "
                                            + "For ChannelSlot it is the slot number, \"0\" to \"4\". "
                                            + "Not used by the others.")
                                    },
                                    ["required"] = new JsonArray { "action" }
                                }
                            },
                            ["required"] = new JsonArray { "label" }
                        }
                    },
                    ["target_page"] = Property("string",
                        "Optional. The name of a page that already exists - an application's "
                        + "own page, such as \"Excel\", or a page the user made. The keys are "
                        + "merged into it and nothing already on it is moved. Left out, a new "
                        + "page is created instead. Use deck_status or ask the user for the name."),
                    ["timeout_seconds"] = Property("number", "Default 300, clamped to 5-3600.")
                },
                ["required"] = new JsonArray { "page", "keys" }
            }),

        Tool("deck_status",
            "What the deck is showing right now: transport, current page, whether it is " +
            "following the focused app, and what is playing.",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),

        Tool("deck_integrations",
            "What the connected applications offer, by name: OBS scenes and audio "
            + "sources, Discord servers and voice channels. Call this before proposing "
            + "keys that drive them, because the names and channel ids come from here "
            + "rather than from guesswork. Says so plainly when an application is not "
            + "running, in which case do not propose keys for it.",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() })
    ];

    private static JsonObject Tool(string name, string description, JsonObject schema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = schema
    };

    private static JsonObject Property(string type, string description) => new()
    {
        ["type"] = type,
        ["description"] = description
    };

    private static string ToolResult(JsonNode? id, string text, bool isError = false)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } }
        };

        if (isError) result["isError"] = true;
        return Result(id, result);
    }

    private static string Result(JsonNode? id, JsonNode result) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result
    }.ToJsonString();

    private static string Error(JsonNode? id, int code, string message) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    }.ToJsonString();

    /// <summary>The interesting part of a reply, not the JSON-RPC envelope.</summary>
    private static string Summarise(string response)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(response);

            if (node?["error"]?["message"]?.GetValue<string>() is { } error)
                return "error: " + error;

            if (node?["result"]?["content"] is JsonArray content && content.Count > 0)
                return content[0]?["text"]?.GetValue<string>() ?? "ok";

            if (node?["result"]?["tools"] is JsonArray tools)
                return $"{tools.Count} tools";

            return "ok";
        }
        catch
        {
            return "ok";
        }
    }

    private static async Task WriteAsync(HttpListenerContext context, int status, string contentType, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);

        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;

        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    // Not an interpolated raw string: the curl example ends in three closing braces,
    // which the interpolation syntax would try to claim for itself.
    private string InstructionPage() => InstructionTemplate.Replace("__URL__", Url);

    private const string InstructionTemplate = """
        <!doctype html>
        <meta charset="utf-8">
        <title>dotStream MCP</title>
        <style>
          body { background:#121214; color:#e8e8ec; font:14px/1.6 "Segoe UI",sans-serif;
                 max-width:52rem; margin:3rem auto; padding:0 1.5rem; }
          h1 { font-weight:600; } h1 span { color:#4dd9e8; }
          code, pre { background:#1b1b1f; border:1px solid #33333a; border-radius:6px; }
          code { padding:.1rem .35rem; } pre { padding:1rem; overflow-x:auto; }
          td { padding:.35rem 1.5rem .35rem 0; vertical-align:top; }
          .muted { color:#80808a; }
        </style>
        <h1>dot<span>Stream</span> MCP</h1>
        <p>An MCP server on <code>__URL__</code>, loopback only. A stream controller with
        fifteen physical keys, so a question can be answered by pressing something.</p>
        <table>
          <tr><td><code>deck_ask</code></td><td>Put options on physical keys and block until one is pressed.</td></tr>
          <tr><td><code>deck_propose_page</code></td><td>Offer a page of hotkeys for the user to accept, reject or defer.</td></tr>
          <tr><td><code>deck_notify</code></td><td>Short message on an info cell.</td></tr>
          <tr><td><code>deck_set_key</code></td><td>Label a key on the agent's own page.</td></tr>
          <tr><td><code>deck_status</code></td><td>What the deck is showing, and what is playing.</td></tr>
        </table>

        <p class="muted">Nothing here binds a key on its own. <code>deck_propose_page</code>
        shows what it would create and writes nothing until a person presses accept - which
        is the only route from an agent's work to a key that does something.</p>

        <h2>Writing a hotkey</h2>
        <table>
          <tr><td><code>Ctrl+Shift+L</code></td><td>One combination.</td></tr>
          <tr><td><code>Ctrl+K, Ctrl+O</code></td><td>Commas separate steps, pressed in order, 90&nbsp;ms apart.</td></tr>
          <tr><td><code>Alt, H, M, C</code></td><td>A bare modifier is a step - this walks Excel's ribbon to Merge and Centre.</td></tr>
          <tr><td><code>Ctrl+Shift+P, "Developer: Reload Window", Enter</code></td><td>A quoted step is <em>typed</em>. This is how a command palette is reached.</td></tr>
        </table>
        <p class="muted">Sequences exist because thousands of commands have no shortcut of
        their own and can only be reached by walking a menu. A comma inside quotes belongs to
        the text, and <code>Ctrl+,</code> is still one key.</p>

        <h2>Where the keys land</h2>
        <p>Pass <code>target_page</code> to merge into a page that already exists - an
        application's own page, by the name a person would use for it (<code>"Excel"</code>),
        or its identifier. Occupied keys are never overwritten unless an explicit
        <code>index</code> asks for one. Without it, a new page is created.</p>
        <p class="muted">One key opens a filled-in dialog so the label and icon can be
        adjusted before saving; several show accept, reject and <strong>later</strong> on the
        deck itself. "Later" is not a refusal - keep the proposal and offer it again.</p>

        <pre>curl -s __URL__ -H "content-type: application/json" -d '{
          "jsonrpc":"2.0","id":1,"method":"tools/call",
          "params":{"name":"deck_propose_page","arguments":{
            "page":"VS Code","target_page":"Visual Studio Code",
            "keys":[{"label":"Reload","icon":"refresh",
              "hotkey":"Ctrl+Shift+P, \"Developer: Reload Window\", Enter"}]}}}'</pre>

        <pre>curl -s __URL__ -H "content-type: application/json" -d '{
          "jsonrpc":"2.0","id":2,"method":"tools/call",
          "params":{"name":"deck_ask","arguments":{
            "question":"Keep the refactor?","options":["Keep","Rewrite"]}}}'</pre>

        <p class="muted">Call <code>tools/list</code> for the full schemas, including every
        icon name available to a proposed key.</p>
        """;

    public async ValueTask DisposeAsync()
    {
        Stop();

        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch
            {
                // Expected on shutdown.
            }
        }

        _cts?.Dispose();
    }
}
