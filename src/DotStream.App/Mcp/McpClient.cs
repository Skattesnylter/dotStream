using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace DotStream.App.Mcp;

public sealed record McpToolInfo(string Name, string Description)
{
    public override string ToString() => Name;
}

public sealed record McpCallResult(bool IsError, string Text);

/// <summary>
/// Calls tools on someone else's MCP server, so a key can do whatever that server
/// does.
///
/// Deliberately generic: dotStream knows nothing about any particular server. Point
/// it at a URL, it reads the tool list from the server itself, and the user picks.
/// That keeps this project free of couplings to whatever happens to be running on
/// the machine.
/// </summary>
public sealed class McpClient : IDisposable
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ConcurrentDictionary<string, string?> _sessions = new();

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(string url, CancellationToken ct = default)
    {
        JsonNode? response = await SendAsync(url, "tools/list", null, ct);
        var tools = new List<McpToolInfo>();

        if (response?["result"]?["tools"] is not JsonArray array) return tools;

        foreach (JsonNode? entry in array)
        {
            string? name = entry?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name)) continue;

            tools.Add(new McpToolInfo(name, entry?["description"]?.GetValue<string>() ?? ""));
        }

        return tools;
    }

    public async Task<McpCallResult> CallAsync(string url, string tool, string? argumentsJson,
        CancellationToken ct = default)
    {
        JsonNode arguments;

        try
        {
            arguments = string.IsNullOrWhiteSpace(argumentsJson)
                ? new JsonObject()
                : JsonNode.Parse(argumentsJson) ?? new JsonObject();
        }
        catch (Exception ex)
        {
            return new McpCallResult(true, "Arguments are not valid JSON: " + ex.Message);
        }

        JsonNode? response = await SendAsync(url, "tools/call",
            new JsonObject { ["name"] = tool, ["arguments"] = arguments }, ct);

        if (response?["error"] is { } error)
            return new McpCallResult(true, error["message"]?.GetValue<string>() ?? "The server returned an error.");

        JsonNode? result = response?["result"];
        bool isError = result?["isError"]?.GetValue<bool>() ?? false;

        var text = new StringBuilder();

        if (result?["content"] is JsonArray content)
        {
            foreach (JsonNode? part in content)
            {
                if (part?["text"]?.GetValue<string>() is { } line) text.AppendLine(line);
            }
        }

        return new McpCallResult(isError,
            text.Length > 0 ? text.ToString().Trim() : "The tool returned no text.");
    }

    private async Task<JsonNode?> SendAsync(string url, string method, JsonNode? parameters, CancellationToken ct)
    {
        await EnsureInitialisedAsync(url, ct);
        return await PostAsync(url, method, parameters, ct);
    }

    /// <summary>
    /// Most servers expect initialize before anything else, and some hand back a
    /// session id that later requests have to carry. Ours needs neither, but a client
    /// that only works against its own server is not much of a client.
    /// </summary>
    private async Task EnsureInitialisedAsync(string url, CancellationToken ct)
    {
        if (_sessions.ContainsKey(url)) return;

        JsonNode? response = await PostAsync(url, "initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "dotStream", ["version"] = "0.1.0" }
        }, ct, captureSession: true);

        _ = response;

        await PostAsync(url, "notifications/initialized", null, ct, notification: true);
    }

    private async Task<JsonNode?> PostAsync(string url, string method, JsonNode? parameters,
        CancellationToken ct, bool captureSession = false, bool notification = false)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        if (!notification) payload["id"] = Random.Shared.Next(1, int.MaxValue);
        if (parameters is not null) payload["params"] = parameters;

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (_sessions.TryGetValue(url, out string? session) && session is not null)
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);

        if (captureSession)
        {
            _sessions[url] = response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;
        }

        if (notification) return null;

        string body = await response.Content.ReadAsStringAsync(ct);
        return ParseBody(body);
    }

    /// <summary>
    /// Servers may answer a POST with either plain JSON or an SSE stream carrying the
    /// same object. Reading the first data line covers both without pulling in a
    /// streaming client for what is a single request and a single reply.
    /// </summary>
    private static JsonNode? ParseBody(string body)
    {
        string trimmed = body.TrimStart();

        if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
            return JsonNode.Parse(trimmed);

        foreach (string line in body.Split('\n'))
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string json = line[5..].Trim();
            if (json.Length > 0) return JsonNode.Parse(json);
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}
