namespace DotStream.Core;

public sealed record DeckActionContext(int ProtocolIndex);

/// <summary>
/// What a key does when pressed. Kept minimal on purpose - we are not building
/// an Elgato-compatible plugin SDK, just a small set of first-class actions.
/// </summary>
public interface IDeckAction
{
    /// <summary>Stable identifier used in profile JSON.</summary>
    string Kind { get; }

    Task InvokeAsync(DeckActionContext context, CancellationToken ct = default);
}

/// <summary>Convenience action for wiring things up from code.</summary>
public sealed class DelegateAction : IDeckAction
{
    private readonly Func<DeckActionContext, CancellationToken, Task> _run;

    public DelegateAction(string kind, Func<DeckActionContext, CancellationToken, Task> run)
    {
        Kind = kind;
        _run = run;
    }

    public DelegateAction(string kind, Action<DeckActionContext> run)
        : this(kind, (ctx, _) => { run(ctx); return Task.CompletedTask; })
    {
    }

    public string Kind { get; }

    public Task InvokeAsync(DeckActionContext context, CancellationToken ct = default) => _run(context, ct);
}
