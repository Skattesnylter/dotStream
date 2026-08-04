using System.Windows;
using System.Windows.Input;

namespace DotStream.App;

public partial class TextInputWindow : Window
{
    public TextInputWindow(string title, string prompt, string hint, string? current, bool allowReset)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        Title = title;
        PromptLabel.Text = prompt;
        HintLabel.Text = hint;
        Input.Text = current ?? "";
        ResetButton.Visibility = allowReset ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    /// <summary>The text entered, or null when the user asked for the default back.</summary>
    public string? Value { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Value = Input.Text;
        DialogResult = true;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        Value = null;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                OnSave(sender, e);
                break;
            case Key.Escape:
                OnCancel(sender, e);
                break;
        }
    }
}
