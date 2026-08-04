using System.Windows.Media;

namespace DotStream.App.Mcp;

public sealed record AskResult(bool Answered, int Index, string? Choice, string Reason);

/// <summary>
/// One key an agent would like to create. The icon is optional - left out, one is
/// guessed from the label, so a proposal never arrives as bare text.
///
/// Index is the physical key to land on, 1-15. Left out, the next free one is used,
/// which is what you want when proposing a whole page and not what you want when
/// adding a single key to a page someone has already arranged.
/// </summary>
public sealed record ProposedKey(string Label, string Hotkey, string Icon = "", int? Index = null);

public sealed record DeckStatus(
    string Transport,
    string Page,
    int Depth,
    bool FollowingFocus,
    string? NowPlaying);

/// <summary>
/// What an AI agent is allowed to do to the deck.
///
/// Note what is missing: nothing here binds a key to an action. An agent may draw on
/// the deck and ask a question, and that is the whole surface. Actions are declared
/// by the user in their profile, so a compromised or confused agent cannot turn
/// "Pause music" into something that runs a program.
/// </summary>
public interface IDeckAgent
{
    /// <summary>
    /// Puts the options on physical keys and waits for one to be pressed.
    ///
    /// The point of the whole exercise: an agent that hits a decision after twenty
    /// minutes of work can ask on the desk rather than in a terminal nobody is
    /// watching.
    /// </summary>
    Task<AskResult> AskAsync(string question, IReadOnlyList<string> options, TimeSpan timeout);

    /// <summary>Shows a short message on an info cell for a few seconds.</summary>
    void Notify(string text, int? cell, Color? colour);

    /// <summary>Draws a label on a key of the agent's own page, and shows that page.</summary>
    void SetKey(int index, string label, Color? colour);

    /// <summary>
    /// Offers a whole page of hotkeys for the user to accept or reject.
    ///
    /// This is the one path by which an agent's work becomes functional keys, and it
    /// exists precisely so that path runs through a person. The proposal is shown on
    /// the deck, the user presses accept or reject, and only then is anything saved.
    /// An agent cannot bind a key on its own.
    ///
    /// With a targetPage the keys are merged into a page that already exists - an
    /// application's own page, say - instead of a new one being created. Without it,
    /// the only thing an agent could ever do was propose a page from scratch, which
    /// meant "add AutoSum to my Excel page" had no route through this interface at all.
    /// </summary>
    Task<AskResult> ProposePageAsync(
        string pageName, IReadOnlyList<ProposedKey> keys, TimeSpan timeout, string? targetPage = null);

    DeckStatus Status();
}
