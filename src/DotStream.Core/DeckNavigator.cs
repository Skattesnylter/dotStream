namespace DotStream.Core;

/// <summary>
/// The page stack.
///
/// Drill-down (pressing an app key to reveal its controls) and, later, automatic
/// context switching are the same operation: push a page. Keeping one stack means
/// "back" always means the same thing.
///
/// Later, when a foreground-window hook starts pushing pages on its own, the rule
/// is that it may only do so while the stack is at root - otherwise it would yank
/// away a sub-page the user deliberately navigated into.
/// </summary>
public sealed class DeckNavigator
{
    private readonly List<DeckPage> _stack = [];

    public DeckPage? Current => _stack.Count > 0 ? _stack[^1] : null;

    public bool IsAtRoot => _stack.Count <= 1;

    public int Depth => _stack.Count;

    public event EventHandler? Changed;

    public void SetRoot(DeckPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        _stack.Clear();
        _stack.Add(page);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Push(DeckPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // Pressing the same app key twice should not stack two identical pages.
        if (Current?.Id == page.Id) return;

        _stack.Add(page);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pop()
    {
        if (IsAtRoot) return;

        _stack.RemoveAt(_stack.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PopToRoot()
    {
        if (IsAtRoot) return;

        _stack.RemoveRange(1, _stack.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Goes straight from wherever the stack is to one page above root, announcing the
    /// change once.
    ///
    /// Popping and then pushing announces twice, and the state in between is the home
    /// page - which gets drawn. Switching between two applications therefore showed a
    /// frame of home, whatever happened to be on its keys, before the page that was
    /// actually wanted. The intermediate state is real, so the fix is not to enter it.
    /// </summary>
    public void SwitchTo(DeckPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (Current?.Id == page.Id) return;
        if (_stack.Count == 0) { SetRoot(page); return; }

        _stack.RemoveRange(1, _stack.Count - 1);
        _stack.Add(page);

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
