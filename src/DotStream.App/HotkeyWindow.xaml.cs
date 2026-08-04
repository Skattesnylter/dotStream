using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DotStream.Core;
using DotStream.Rendering;

namespace DotStream.App;

public sealed record HotkeyBinding(string Combination, string Label, string Icon = "")
{
    /// <summary>
    /// A file on this machine to take the icon from - any .dll, .exe or .ico. Stored
    /// as a path and an index, never as a copy of the image: the artwork in
    /// SHELL32.dll belongs to Microsoft, and reading it from the user's own
    /// installation is a different thing from shipping it.
    /// </summary>
    public string IconFile { get; init; } = "";

    public int IconIndex { get; init; }

    // Everything below is derived, and every one of them has to be kept away from the
    // serialiser. ResolvedIcon reaches into WPF's type system through
    // TextDecorationCollection, and System.Text.Json follows it all the way to
    // System.Type before giving up - which is what took the profile save down.

    /// <summary>
    /// The chosen icon, or one guessed from the label. Guessing means a key proposed
    /// as plain text still arrives looking like a button rather than a line of type.
    /// </summary>
    [JsonIgnore]
    public DeckIcon? ResolvedIcon =>
        IconLibrary.ByName(Icon) ?? IconLibrary.Suggest(Label);

    /// <summary>The extracted file icon, cached. Null when none is set or it fails.</summary>
    [JsonIgnore]
    public BitmapSource? FileImage => IconCache.Get(IconFile, IconIndex);

    // Not called "Hotkey": the property would then shadow the type, and inside this
    // namespace "App" already resolves to the Application subclass rather than the
    // namespace, so there is no clean way to name it back.
    [JsonIgnore]
    public Hotkey? Parsed => Hotkey.Parse(Combination);

    /// <summary>
    /// The combination as steps. One for an ordinary hotkey, several for a ribbon
    /// macro like "Alt, H, M, C" - the same field either way.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<SequenceStep> Steps => KeySequence.Parse(Combination);

    [JsonIgnore]
    public bool IsSequence => Steps.Count > 1;

    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Combination : Label;
}

/// <summary>
/// Extracted file icons, kept for the lifetime of the app.
///
/// Extraction touches the disk and the shell, so doing it on every repaint of a cell
/// would be wasteful - and the deck repaints on a timer.
/// </summary>
internal static class IconCache
{
    private static readonly Dictionary<string, BitmapSource?> Entries = [];
    private static readonly Lock Gate = new();

    public static BitmapSource? Get(string? path, int index)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string key = path + "|" + index;

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out BitmapSource? cached)) return cached;

            BitmapSource? image = Icons.IconFile.Extract(path, index, 128);
            Entries[key] = image;
            return image;
        }
    }
}

/// <summary>
/// Captures a key combination by listening for it, rather than asking the user to
/// type its name. Nobody remembers whether it is "OemPlus" or "Equals".
/// </summary>
public partial class HotkeyWindow : Window
{
    private readonly List<SequenceStep> _steps = [];
    private bool _sequenceMode;

    /// <summary>
    /// Set while a modifier is held and nothing else has been pressed yet. If it is
    /// still set when the modifier comes back up, the user tapped it on its own - which
    /// is a real step in a ribbon sequence and nothing at all anywhere else.
    /// </summary>
    private ModifierKeys _tapped;

    private readonly CellRenderer _renderer;
    private readonly Dictionary<string, Border> _iconTiles = [];
    private string _icon = "";

    public HotkeyWindow(HotkeyBinding? existing, CellRenderer renderer)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _renderer = renderer;
        BuildIconPicker();

        if (existing is not null)
        {
            _steps.AddRange(existing.Steps);
            _sequenceMode = _steps.Count > 1;
            LabelBox.Text = existing.Label;
            SelectIcon(existing.Icon);
        }

        Refresh();
        UpdateIconHint();
        Loaded += (_, _) => CaptureBox.Focus();
    }

    public HotkeyBinding? Result { get; private set; }

    /// <summary>
    /// Set when the user chose "Later" rather than cancelling. An agent needs the
    /// difference: cancelling means no, later means not now.
    /// </summary>
    public bool Postponed { get; private set; }

    /// <summary>Offers "Later", which only means anything when something proposed this.</summary>
    public void AllowPostpone() => LaterButton.Visibility = Visibility.Visible;

    private void OnLater(object sender, RoutedEventArgs e)
    {
        Postponed = true;
        DialogResult = false;
    }

    private void BuildIconPicker()
    {
        foreach (DeckIcon icon in IconLibrary.All)
        {
            CellVisual visual = icon.ApplyTo(
                new CellVisual { Background = Color.FromRgb(0x16, 0x17, 0x1B) }, Colors.White);

            var tile = new Border
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 5, 5),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32)),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                ToolTip = icon.Name,
                Child = new Image { Source = _renderer.Render(visual).Image, Stretch = Stretch.Fill }
            };

            string name = icon.Name;
            tile.MouseLeftButtonDown += (_, _) => SelectIcon(name);

            _iconTiles[name] = tile;
            IconPanel.Children.Add(tile);
        }
    }

    private string _iconFile = "";
    private int _iconIndex;

    /// <summary>
    /// Loads every icon out of a chosen file and lets the user pick one. The same
    /// thing Windows' own "Change Icon" dialog does, and for the same reason: the
    /// artwork stays where it was installed.
    /// </summary>
    private void OnBrowseIcons(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick a file to take an icon from",
            Filter = "Icon sources (*.dll;*.exe;*.ico)|*.dll;*.exe;*.ico|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
            FileName = "SHELL32.dll"
        };

        if (dialog.ShowDialog(this) != true) return;

        IReadOnlyList<(int Index, BitmapSource Image)> icons =
            Icons.IconFile.ExtractAll(dialog.FileName, 64);

        if (icons.Count == 0)
        {
            IconHint.Text = "That file contains no icons.";
            return;
        }

        _iconFile = dialog.FileName;
        IconPanel.Children.Clear();
        _iconTiles.Clear();

        foreach ((int index, BitmapSource image) in icons)
        {
            var tile = new Border
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 5, 5),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32)),
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x17, 0x1B)),
                Padding = new Thickness(4),
                Cursor = Cursors.Hand,
                ToolTip = $"{System.IO.Path.GetFileName(dialog.FileName)}, {index}",
                Child = new Image { Source = image, Stretch = Stretch.Uniform }
            };

            int chosen = index;
            tile.MouseLeftButtonDown += (_, _) => SelectFileIcon(chosen);

            _iconTiles[index.ToString()] = tile;
            IconPanel.Children.Add(tile);
        }

        IconHint.Text = $"{icons.Count} icons in {System.IO.Path.GetFileName(dialog.FileName)} - pick one";
    }

    private void SelectFileIcon(int index)
    {
        _iconIndex = index;
        _icon = "";

        foreach ((string key, Border tile) in _iconTiles)
        {
            bool selected = key == index.ToString();
            tile.BorderBrush = selected
                ? (Brush)FindResource("Accent")
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32));
            tile.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        IconHint.Text = $"{System.IO.Path.GetFileName(_iconFile)}, icon {index}";
    }

    private void SelectIcon(string? name)
    {
        _icon = name ?? "";
        _iconFile = "";

        foreach ((string key, Border tile) in _iconTiles)
        {
            bool selected = key.Equals(_icon, StringComparison.OrdinalIgnoreCase);
            tile.BorderBrush = selected
                ? (Brush)FindResource("Accent")
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32));
            tile.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        UpdateIconHint();
    }

    private void OnLabelChanged(object sender, TextChangedEventArgs e) => UpdateIconHint();

    private void UpdateIconHint()
    {
        if (IconHint is null) return;

        if (_icon.Length > 0)
        {
            IconHint.Text = _icon + "  ·  click again to keep, or pick another";
            return;
        }

        IconHint.Text = IconLibrary.Suggest(LabelBox.Text) is { } suggested
            ? $"none chosen - \"{suggested.Name}\" will be used, guessed from the label"
            : "none chosen - the key will show the combination instead";
    }

    private void OnCaptureKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifier(key))
        {
            // Remember it, in case it turns out to be a tap rather than the first half
            // of a combination. Resolved on the way back up.
            _tapped = Keyboard.Modifiers;
            return;
        }

        if (key == Key.None) return;

        _tapped = ModifierKeys.None;
        Add(new SequenceStep(new Hotkey(Keyboard.Modifiers, key), null));
    }

    private void OnCaptureKeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!IsModifier(key) || _tapped == ModifierKeys.None) return;

        ModifierKeys tapped = _tapped;
        _tapped = ModifierKeys.None;

        // Outside a sequence a bare modifier means nothing, so it stays ignored - the
        // old behaviour, and the right one. Inside a sequence it is how "Alt, H, M, C"
        // begins, and there is no other way to write it.
        if (_sequenceMode) Add(new SequenceStep(new Hotkey(tapped, Key.None), null));
    }

    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private void Add(SequenceStep step)
    {
        if (!_sequenceMode) _steps.Clear();
        _steps.Add(step);

        Refresh();

        if (LabelBox.Text.Trim().Length == 0 || !_sequenceMode)
            LabelBox.Text = KeySequence.Describe(_steps);
    }

    private void OnToggleMode(object sender, RoutedEventArgs e)
    {
        _sequenceMode = !_sequenceMode;
        Refresh();
        CaptureBox.Focus();
    }

    private void OnRemoveStep(object sender, RoutedEventArgs e)
    {
        if (_steps.Count > 0) _steps.RemoveAt(_steps.Count - 1);
        Refresh();
        CaptureBox.Focus();
    }

    private void Refresh()
    {
        ModeButton.Content = _sequenceMode ? "Sequence" : "Single combination";
        StepBackButton.Visibility = _sequenceMode && _steps.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        CaptureLabel.Text = _steps.Count == 0
            ? _sequenceMode ? "Press the steps in order..." : "Press a combination..."
            : KeySequence.Describe(_steps);

        // Four steps at 18pt overflows a 470px window. Shrinking beats wrapping.
        CaptureLabel.FontSize = CaptureLabel.Text.Length > 22 ? 14 : 18;

        SaveButton.IsEnabled = _steps.Count > 0;

        HintLabel.Text = _sequenceMode
            ? $"{_steps.Count} step(s) - each tap is added, {KeySequence.StepDelayMs} ms apart."
            : _steps.Count == 0
                ? "Modifiers on their own are ignored - hold them and press a key."
                : "Press another combination to change it.";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) return;

        Result = new HotkeyBinding(KeySequence.Describe(_steps), LabelBox.Text.Trim(), _icon)
        {
            IconFile = _iconFile,
            IconIndex = _iconIndex
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
