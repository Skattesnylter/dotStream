using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DotStream.Core;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>What the browser hands back: a named icon, or one out of a file.</summary>
public sealed record IconChoice(string Name, string File, int Index);

/// <summary>
/// Every icon in one place, searchable.
///
/// Previously the only way to see them was to start creating a hotkey, which is the
/// wrong way round: you look at icons to decide, not after deciding. Doubles as the
/// picker so there is one place that knows how to show them.
/// </summary>
public partial class IconBrowserWindow : Window
{
    private readonly CellRenderer _renderer;
    private readonly bool _picking;
    private readonly Dictionary<string, Border> _tiles = [];

    private string _fileSource = "";
    private IReadOnlyList<(int Index, BitmapSource Image)> _fileIcons = [];
    private string _selected = "";
    private int _selectedIndex = -1;

    public IconBrowserWindow(CellRenderer renderer, bool picking)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _renderer = renderer;
        _picking = picking;

        HeadingLabel.Text = picking ? "Choose an icon" : "Icons";
        SubtitleLabel.Text = picking
            ? "Click one, then use it. Or take one from a file on this machine."
            : "The built-in set. Fluent UI System Icons, MIT licensed, plus letterforms "
              + "where the letter is the better icon. Use \"From a file\" for anything else.";

        ChooseButton.Visibility = picking ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = picking ? "Cancel" : "Close";

        ShowLibrary(null);
    }

    /// <summary>Set when the user picks one and confirms.</summary>
    public IconChoice? Result { get; private set; }

    private void ShowLibrary(string? query)
    {
        _fileSource = "";
        _fileIcons = [];

        IEnumerable<DeckIcon> icons = IconLibrary.All;

        if (!string.IsNullOrWhiteSpace(query))
            icons = icons.Where(i => i.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
                                     || i.Category.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

        List<DeckIcon> visible = icons.ToList();

        IconPanel.Children.Clear();
        _tiles.Clear();

        foreach (DeckIcon icon in visible)
        {
            CellVisual visual = icon.ApplyTo(
                new CellVisual { Background = Color.FromRgb(0x16, 0x17, 0x1B) }, Colors.White);

            string name = icon.Name;
            IconPanel.Children.Add(MakeTile(name, icon.Name, _renderer.Render(visual).Image,
                () => Select(name, -1)));
        }

        CountLabel.Text = visible.Count == IconLibrary.All.Count
            ? $"{visible.Count} icons"
            : $"{visible.Count} of {IconLibrary.All.Count} icons";
    }

    private Border MakeTile(string key, string tooltip, BitmapSource image, Action onClick)
    {
        var caption = new TextBlock
        {
            Text = tooltip,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x8A)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 66,
            Margin = new Thickness(0, 3, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(new Image { Source = image, Width = 48, Height = 48, Stretch = Stretch.Uniform });
        stack.Children.Add(caption);

        var tile = new Border
        {
            Width = 74,
            Padding = new Thickness(4, 6, 4, 6),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32)),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = stack
        };

        tile.MouseLeftButtonDown += (_, _) => onClick();
        _tiles[key] = tile;
        return tile;
    }

    private void Select(string name, int index)
    {
        _selected = name;
        _selectedIndex = index;

        foreach ((string key, Border tile) in _tiles)
        {
            bool chosen = key == name;
            tile.BorderBrush = chosen
                ? (Brush)FindResource("Accent")
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x32));
            tile.BorderThickness = new Thickness(chosen ? 2 : 1);
        }

        ChooseButton.IsEnabled = true;

        CountLabel.Text = index >= 0
            ? $"{System.IO.Path.GetFileName(_fileSource)}, icon {index}"
            : name;
    }

    private void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick a file to take an icon from",
            Filter = "Icon sources (*.dll;*.exe;*.ico)|*.dll;*.exe;*.ico|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
            FileName = "imageres.dll"
        };

        if (dialog.ShowDialog(this) != true) return;

        _fileIcons = Icons.IconFile.ExtractAll(dialog.FileName, 64);

        if (_fileIcons.Count == 0)
        {
            CountLabel.Text = "That file contains no icons.";
            return;
        }

        _fileSource = dialog.FileName;
        IconPanel.Children.Clear();
        _tiles.Clear();

        foreach ((int index, BitmapSource image) in _fileIcons)
        {
            int chosen = index;
            IconPanel.Children.Add(MakeTile(index.ToString(), index.ToString(), image,
                () => Select(chosen.ToString(), chosen)));
        }

        CountLabel.Text = $"{_fileIcons.Count} icons in {System.IO.Path.GetFileName(dialog.FileName)}";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (ClearSearchButton is not null)
            ClearSearchButton.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Searching returns to the built-in set; a file listing has no names to match.
        ShowLibrary(SearchBox.Text);
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        if (!_picking || _selected.Length == 0) return;

        Result = _selectedIndex >= 0
            ? new IconChoice("", _fileSource, _selectedIndex)
            : new IconChoice(_selected, "", 0);

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_picking) DialogResult = false;
        else Close();
    }
}
