using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DotStream.Icons;

namespace DotStream.App;

public sealed class AppSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public AppSelectionItem(InstalledApp app, bool isSelected)
    {
        App = app;
        _isSelected = isSelected;
    }

    public InstalledApp App { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class AppSelectionWindow : Window
{
    private readonly List<AppSelectionItem> _items;

    public AppSelectionWindow(IReadOnlyList<InstalledApp> apps, IReadOnlySet<string>? currentSelection)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        // Nothing chosen yet means first run: pre-tick what the heuristic considers a
        // real app, so this dialog starts as a short review rather than a chore.
        _items = apps
            .Select(app => new AppSelectionItem(
                app,
                currentSelection is null
                    ? AppFilter.IsLikelyUserApp(app)
                    : currentSelection.Contains(app.AppUserModelId)))
            .ToList();

        foreach (AppSelectionItem item in _items)
            item.PropertyChanged += (_, _) => UpdateCount();

        SubtitleLabel.Text =
            $"shell:AppsFolder lists {apps.Count} launchable items, including help files and " +
            "system tools. Tick what you want available as deck keys.";

        ApplyFilter(null);
        UpdateCount();
    }

    /// <summary>Populated when the dialog is saved.</summary>
    public IReadOnlyList<string>? Result { get; private set; }

    private void ApplyFilter(string? query)
    {
        ItemList.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _items
            : _items.Where(i => i.App.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void UpdateCount() =>
        CountLabel.Text = $"{_items.Count(i => i.IsSelected)} of {_items.Count} selected";

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (ClearSearchButton is not null)
            ClearSearchButton.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ApplyFilter(SearchBox.Text);
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    /// <summary>Applies the selection to the visible items only, so it composes with search.</summary>
    private void SetVisible(Func<AppSelectionItem, bool> value)
    {
        foreach (AppSelectionItem item in ItemList.Items.OfType<AppSelectionItem>())
            item.IsSelected = value(item);

        UpdateCount();
    }

    private void OnSelectHeuristic(object sender, RoutedEventArgs e) =>
        SetVisible(i => AppFilter.IsLikelyUserApp(i.App));

    private void OnSelectAll(object sender, RoutedEventArgs e) => SetVisible(_ => true);

    private void OnSelectNone(object sender, RoutedEventArgs e) => SetVisible(_ => false);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = _items.Where(i => i.IsSelected).Select(i => i.App.AppUserModelId).ToList();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
