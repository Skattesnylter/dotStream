using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DotStream.Core;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>
/// Writes the text a key will type, and chooses what the key looks like.
/// </summary>
public partial class TextMacroWindow : Window
{
    private readonly CellRenderer _renderer;
    private readonly Dictionary<string, Border> _iconTiles = [];

    private string _icon = "";
    private bool _pressEnter;

    public TextMacroWindow(TextMacroBinding? existing, CellRenderer renderer)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _renderer = renderer;
        BuildIconPicker();

        if (existing is not null)
        {
            TextBox.Text = existing.Text;
            LabelBox.Text = existing.Label;
            _pressEnter = existing.PressEnter;
            SelectIcon(existing.Icon);
        }

        Refresh();
        Loaded += (_, _) => TextBox.Focus();
    }

    public TextMacroBinding? Result { get; private set; }

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

    private void OnToggleEnter(object sender, RoutedEventArgs e)
    {
        _pressEnter = !_pressEnter;
        Refresh();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnLabelChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (SaveButton is null) return;

        string text = TextBox.Text;

        SaveButton.IsEnabled = text.Length > 0;
        EnterButton.Content = "Press Enter afterwards: " + (_pressEnter ? "yes" : "no");

        int lines = text.Length == 0 ? 0 : text.Split('\n').Length;
        LengthLabel.Text = text.Length == 0
            ? ""
            : $"{text.Length} characters, {lines} line{(lines == 1 ? "" : "s")}";

        if (_icon.Length > 0)
        {
            IconHint.Text = _icon;
            return;
        }

        string forGuess = LabelBox.Text.Length > 0 ? LabelBox.Text : text;

        IconHint.Text = IconLibrary.Suggest(forGuess) is { } suggested
            ? $"none chosen - \"{suggested.Name}\" will be used, guessed from the label"
            : "none chosen - the key will show the first few words instead";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (TextBox.Text.Length == 0) return;

        Result = new TextMacroBinding(TextBox.Text, LabelBox.Text.Trim(), _icon)
        {
            PressEnter = _pressEnter
        };

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
