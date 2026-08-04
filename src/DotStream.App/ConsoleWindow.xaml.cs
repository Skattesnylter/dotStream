using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DotStream.App;

/// <summary>
/// Live view of <see cref="DeckLog"/>.
///
/// A separate window rather than a panel: a log is something you leave open on a
/// second monitor while working, not something that should compete with the deck for
/// space in the editor.
/// </summary>
public partial class ConsoleWindow : Window
{
    private readonly CollectionViewSource _view = new() { Source = DeckLog.Entries };
    private LogDirection? _filter;

    public ConsoleWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _view.Filter += OnFilter;
        EntryList.ItemsSource = _view.View;

        ((INotifyCollectionChanged)DeckLog.Entries).CollectionChanged += OnEntriesChanged;
        Closed += (_, _) => ((INotifyCollectionChanged)DeckLog.Entries).CollectionChanged -= OnEntriesChanged;

        ApplyFilter(null);
        ScrollToEnd();
    }

    private void OnFilter(object sender, FilterEventArgs e)
    {
        e.Accepted = _filter is null || (e.Item is LogEntry entry && entry.Direction == _filter);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateCount();
        if (AutoScroll.IsChecked == true) ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (EntryList.Items.Count == 0) return;
        EntryList.ScrollIntoView(EntryList.Items[^1]);
    }

    private void ApplyFilter(LogDirection? direction)
    {
        _filter = direction;
        _view.View.Refresh();

        foreach ((Button button, LogDirection? which) in new[]
                 {
                     (FilterAll, (LogDirection?)null),
                     (FilterIn, LogDirection.In),
                     (FilterOut, LogDirection.Out),
                     (FilterNote, LogDirection.Note)
                 })
        {
            button.BorderBrush = which == direction
                ? (Brush)FindResource("Accent")
                : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x3A));
        }

        UpdateCount();
        ScrollToEnd();
    }

    private void UpdateCount()
    {
        int shown = _view.View.Cast<object>().Count();
        CountLabel.Text = shown == DeckLog.Entries.Count
            ? $"{DeckLog.Entries.Count} entries"
            : $"{shown} of {DeckLog.Entries.Count} entries";
    }

    private void OnFilterAll(object sender, RoutedEventArgs e) => ApplyFilter(null);
    private void OnFilterIn(object sender, RoutedEventArgs e) => ApplyFilter(LogDirection.In);
    private void OnFilterOut(object sender, RoutedEventArgs e) => ApplyFilter(LogDirection.Out);
    private void OnFilterNote(object sender, RoutedEventArgs e) => ApplyFilter(LogDirection.Note);

    private void OnClear(object sender, RoutedEventArgs e)
    {
        DeckLog.Entries.Clear();
        UpdateCount();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder();

        foreach (LogEntry entry in _view.View.Cast<LogEntry>())
            text.AppendLine($"{entry.Time}  {entry.Arrow}  {entry.Source,-10}  {entry.Text}");

        try
        {
            Clipboard.SetText(text.ToString());
            CountLabel.Text = "Copied to the clipboard.";
        }
        catch
        {
            CountLabel.Text = "The clipboard is busy - try again.";
        }
    }
}
