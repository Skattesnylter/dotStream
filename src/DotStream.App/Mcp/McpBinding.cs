namespace DotStream.App.Mcp;

/// <summary>
/// What one key calls: a server, a tool on it, and the arguments to send.
///
/// Stored with the key in the profile rather than in a separate registry, because a
/// binding without its key is meaningless and a key without its binding is broken.
/// </summary>
public sealed record McpBinding
{
    public required string Url { get; init; }
    public required string Tool { get; init; }

    /// <summary>JSON object, or empty for a tool that takes nothing.</summary>
    public string Arguments { get; init; } = "";

    /// <summary>What the key says. Defaults to the tool name.</summary>
    public string Label { get; init; } = "";

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Tool : Label;
}
