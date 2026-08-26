using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DotStream.Core;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>
/// Configures a key that starts a program. Has a Test button, because the difference
/// between a working path and a nearly-working one is not visible in a text box.
/// </summary>
public partial class RunWindow : Window
{
    private readonly CellRenderer _renderer;
    private readonly Dictionary<string, Border> _iconTiles = [];

    private string _icon = "";
    private bool _useShell = true;
    private bool _hidden;

    public RunWindow(RunBinding? existing, CellRenderer renderer)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _renderer = renderer;
        BuildIconPicker();

        if (existing is not null)
        {
            PathBox.Text = existing.Path;
            ArgumentsBox.Text = existing.Arguments;
            WorkingBox.Text = existing.WorkingDirectory;
            LabelBox.Text = existing.Label;
            _useShell = existing.UseShell;
            _hidden = existing.Hidden;
            SelectIcon(existing.Icon);
        }

        Refresh();
        Loaded += (_, _) => PathBox.Focus();
    }

    public RunBinding? Result { get; private set; }

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

    private void SelectIcon(string? name)
    {
        _icon = name ?? "";

        foreach ((string key, Border tile) in _iconTiles)
        {
            bool selected = key.Equals(_icon, StringComparison.OrdinalIgnoreCase);
            tile.BorderBrush = selected
                ? (Brush)FindResource("Accent")
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32));
            tile.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        Refresh();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick a program, script or file",
            Filter = "Programs and scripts (*.exe;*.bat;*.cmd;*.ps1;*.py)|*.exe;*.bat;*.cmd;*.ps1;*.py|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        PathBox.Text = dialog.FileName;

        // A first guess at the label, only when there is nothing there to overwrite.
        if (LabelBox.Text.Trim().Length == 0)
            LabelBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private void OnToggleShell(object sender, RoutedEventArgs e)
    {
        _useShell = !_useShell;
        Refresh();
    }

    private void OnToggleHidden(object sender, RoutedEventArgs e)
    {
        _hidden = !_hidden;
        Refresh();
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (SaveButton is null) return;

        SaveButton.IsEnabled = PathBox.Text.Trim().Length > 0;
        ShellButton.Content = "Use shell: " + (_useShell ? "yes" : "no");
        HiddenButton.Content = "Hide window: " + (_hidden ? "yes" : "no");

        if (_icon.Length > 0)
        {
            IconHint.Text = _icon;
            return;
        }

        string guess = LabelBox.Text.Length > 0 ? LabelBox.Text : PathBox.Text;

        IconHint.Text = IconLibrary.Suggest(guess) is { } suggested
            ? $"none chosen - \"{suggested.Name}\" will be used, guessed from the label"
            : "none chosen - the key will show the file name instead";
    }

    private void OnTest(object sender, RoutedEventArgs e)
    {
        RunBinding binding = Build();
        IconHint.Text = binding.Start();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (PathBox.Text.Trim().Length == 0) return;

        Result = Build();
        DialogResult = true;
    }

    private RunBinding Build() => new(PathBox.Text.Trim(), LabelBox.Text.Trim(), _icon)
    {
        Arguments = ArgumentsBox.Text.Trim(),
        WorkingDirectory = WorkingBox.Text.Trim(),
        UseShell = _useShell,
        Hidden = _hidden
    };

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
