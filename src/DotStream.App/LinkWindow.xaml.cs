using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DotStream.Core;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>Configures a key that opens an address, with a Test button to prove it.</summary>
public partial class LinkWindow : Window
{
    private readonly CellRenderer _renderer;
    private readonly Dictionary<string, Border> _iconTiles = [];

    private string _icon = "";

    public LinkWindow(LinkBinding? existing, CellRenderer renderer)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _renderer = renderer;
        BuildIconPicker();

        if (existing is not null)
        {
            TargetBox.Text = existing.Target;
            LabelBox.Text = existing.Label;
            SelectIcon(existing.Icon);
        }

        Refresh();
        Loaded += (_, _) => TargetBox.Focus();
    }

    public LinkBinding? Result { get; private set; }

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
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pick a folder to open" };

        if (dialog.ShowDialog(this) != true) return;

        TargetBox.Text = dialog.FolderName;

        if (LabelBox.Text.Trim().Length == 0)
            LabelBox.Text = System.IO.Path.GetFileName(dialog.FolderName.TrimEnd('\\'));
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (SaveButton is null) return;

        SaveButton.IsEnabled = TargetBox.Text.Trim().Length > 0;

        if (_icon.Length > 0)
        {
            IconHint.Text = _icon;
            return;
        }

        string guess = LabelBox.Text.Length > 0 ? LabelBox.Text : TargetBox.Text;

        IconHint.Text = IconLibrary.Suggest(guess) is { } suggested
            ? $"none chosen - \"{suggested.Name}\" will be used, guessed from the label"
            : "none chosen - a link icon will be used";
    }

    private void OnTest(object sender, RoutedEventArgs e) => IconHint.Text = Build().Open();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (TargetBox.Text.Trim().Length == 0) return;

        Result = Build();
        DialogResult = true;
    }

    private LinkBinding Build() => new(TargetBox.Text.Trim(), LabelBox.Text.Trim(), _icon);

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
