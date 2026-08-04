using System.Windows.Media;
using DotStream.App.Mcp;
using DotStream.Core;

namespace DotStream.App;

/// <summary>
/// The half of <see cref="IDeckAgent"/> that owns state rather than UI: which ask is
/// outstanding, and which cells are showing an agent message.
///
/// Split out of the window so the rules are readable in one place - particularly the
/// rule that only one question may be pending. Two agents asking at once would leave
/// the user looking at a deck where the keys mean two different things.
/// </summary>
public sealed class AgentState
{
    private readonly Lock _gate = new();

    private TaskCompletionSource<AskResult>? _pending;

    public const string AskPageId = "agent:ask";
    public const string AgentPageId = "agent:page";

    /// <summary>Cells showing an agent message, and when it stops.</summary>
    public Dictionary<int, DateTime> NotificationsUntil { get; } = [];

    public bool IsAsking
    {
        get { lock (_gate) return _pending is not null; }
    }

    /// <summary>Claims the single ask slot. False when one is already outstanding.</summary>
    public bool TryBeginAsk(out Task<AskResult> answer)
    {
        lock (_gate)
        {
            if (_pending is not null)
            {
                answer = Task.FromResult(new AskResult(false, -1, null,
                    "The deck is already showing a question. Wait for it to be answered."));
                return false;
            }

            _pending = new TaskCompletionSource<AskResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            answer = _pending.Task;
            return true;
        }
    }

    public void Complete(AskResult result)
    {
        TaskCompletionSource<AskResult>? pending;

        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }

        pending?.TrySetResult(result);
    }

    public static CellVisual OptionVisual(string text, int position) => new()
    {
        Background = Color.FromRgb(0x10, 0x1A, 0x1E),
        BackgroundGradientTo = Color.FromRgb(0x08, 0x0E, 0x11),
        Label = text,
        LabelColor = Colors.White,
        LabelSize = 12,
        LabelPosition = LabelPosition.Bottom,
        ReservedLabelLines = 2,
        BigText = (position + 1).ToString(),
        BigTextColor = WidgetTheme.StreamCyan,
        BigTextScale = 0.75
    };

    public static CellVisual NotificationVisual(string text, Color colour) => new()
    {
        Background = Color.FromRgb(0x0A, 0x0A, 0x0E),
        Label = text,
        LabelColor = colour,
        LabelSize = 11,
        LabelPosition = LabelPosition.Bottom,
        ReservedLabelLines = 3
    };

    public static CellVisual AgentKeyVisual(string label, Color colour) => new()
    {
        Background = Color.FromRgb(0x0E, 0x12, 0x16),
        Label = label,
        LabelColor = colour,
        LabelSize = 12,
        LabelPosition = LabelPosition.Bottom,
        ReservedLabelLines = 2
    };
}
