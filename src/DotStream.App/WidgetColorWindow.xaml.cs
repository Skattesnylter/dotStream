using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DotStream.Core;
using DotStream.Rendering;
using DotStream.Rendering.Widgets;

namespace DotStream.App;

/// <summary>
/// Per-widget colour editing, with the real renderer driving the preview.
///
/// The preview is the actual 85x85 cell the device would receive, blown up 3x with
/// nearest-neighbour so no smoothing hides what the pixels really look like. Picking
/// colours against a smooth approximation of a small display is how you end up with
/// something that looks fine here and muddy on the hardware.
/// </summary>
public partial class WidgetColorWindow : Window
{
    private readonly IInfoWidget _widget;
    private readonly CellRenderer _renderer;
    private readonly Button[] _roleButtons;

    private Color _background;
    private Color _accent;
    private Color _value;
    private Color _label;

    private int _role;
    private bool _updating;

    public WidgetColorWindow(IInfoWidget widget, WidgetTheme theme, CellRenderer renderer)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _widget = widget;
        _renderer = renderer;

        _background = theme.Background;
        _accent = theme.Accent;
        _value = theme.Value;
        _label = theme.Label;

        HeadingLabel.Text = widget.Name + " - colours";

        _roleButtons =
        [
            MakeRoleButton("Background", 0),
            MakeRoleButton("Ring", 1),
            MakeRoleButton("Value", 2),
            MakeRoleButton("Caption", 3)
        ];

        foreach (Button button in _roleButtons)
            RolePanel.Children.Add(button);

        SelectRole(1);
    }

    /// <summary>Populated when the dialog is saved.</summary>
    public WidgetTheme? Result { get; private set; }

    private Button MakeRoleButton(string name, int role)
    {
        var button = new Button
        {
            Content = name,
            FontSize = 11,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 6, 6)
        };

        button.Click += (_, _) => SelectRole(role);
        return button;
    }

    private void SelectRole(int role)
    {
        _role = role;

        for (int i = 0; i < _roleButtons.Length; i++)
        {
            bool selected = i == role;
            _roleButtons[i].BorderBrush = selected
                ? (Brush)FindResource("Accent")
                : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3A));
        }

        PushToControls(Current());
        Refresh();
    }

    private Color Current() => _role switch
    {
        0 => _background,
        1 => _accent,
        2 => _value,
        _ => _label
    };

    private void SetCurrent(Color color)
    {
        switch (_role)
        {
            case 0: _background = color; break;
            case 1: _accent = color; break;
            case 2: _value = color; break;
            default: _label = color; break;
        }
    }

    private void PushToControls(Color color)
    {
        _updating = true;

        SliderR.Value = color.R;
        SliderG.Value = color.G;
        SliderB.Value = color.B;
        HexBox.Text = ColorCodec.ToHex(color);

        _updating = false;
    }

    private void OnChannelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || HexBox is null) return;

        var color = Color.FromRgb((byte)SliderR.Value, (byte)SliderG.Value, (byte)SliderB.Value);
        SetCurrent(color);

        _updating = true;
        HexBox.Text = ColorCodec.ToHex(color);
        _updating = false;

        Refresh();
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        CommitHex();
        e.Handled = true;
    }

    private void OnHexCommitted(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        if (_updating) return;
        if (ColorCodec.Parse(HexBox.Text) is not { } color) return;

        SetCurrent(color);
        PushToControls(color);
        Refresh();
    }

    private void Refresh()
    {
        ChannelR.Text = "Red  " + (int)SliderR.Value;
        ChannelG.Text = "Green  " + (int)SliderG.Value;
        ChannelB.Text = "Blue  " + (int)SliderB.Value;

        var brush = new SolidColorBrush(Current());
        brush.Freeze();
        Swatch.Background = brush;

        Preview.Source = _renderer.Render(_widget.Render(Build())).Image;
    }

    private WidgetTheme Build() => new()
    {
        Background = _background,
        Accent = _accent,
        Value = _value,
        Label = _label
    };

    private void OnReset(object sender, RoutedEventArgs e)
    {
        WidgetTheme defaults = _widget.DefaultTheme;

        _background = defaults.Background;
        _accent = defaults.Accent;
        _value = defaults.Value;
        _label = defaults.Label;

        PushToControls(Current());
        Refresh();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = Build();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
