using System.Windows;
using System.Windows.Controls;

namespace DotStream.App;

/// <summary>
/// Builds a Discord key. Short, because there is nothing to look up: the three actions
/// take no target, unlike an OBS scene or an audio source.
/// </summary>
public partial class DiscordWindow : Window
{
    public DiscordWindow(DiscordBinding? existing)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        if (existing is not null)
        {
            foreach (ComboBoxItem item in ActionBox.Items)
                item.IsSelected = (string)item.Tag == existing.Action.ToString();

            LabelBox.Text = existing.Label;
            Icon = existing.Icon;
            IconFile = existing.IconFile;
            IconIndex = existing.IconIndex;
        }

        Loaded += (_, _) => ShowPreview();
    }

    /// <summary>The finished binding, valid once the dialog returns true.</summary>
    public DiscordBinding? Result { get; private set; }

    private new string Icon { get; set; } = "";

    private string IconFile { get; set; } = "";

    private int IconIndex { get; set; }

    private DiscordAction Selected =>
        ActionBox.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse(tag, out DiscordAction action)
            ? action
            : DiscordAction.ToggleMute;

    private void OnChanged(object sender, RoutedEventArgs e) => ShowPreview();

    private void ShowPreview()
    {
        if (Preview is null) return;

        DiscordBinding binding = Build();

        // Shown for the unmuted case, which is the one people picture when setting up.
        (string command, System.Text.Json.Nodes.JsonObject? args) = binding.Request(muted: false, deafened: false);

        Preview.Text = $"{command}{(args is null ? "" : "  " + args.ToJsonString())}\nkey reads: {binding.DisplayLabel}";
    }

    private DiscordBinding Build() =>
        new(Selected, LabelBox.Text.Trim(), Icon) { IconFile = IconFile, IconIndex = IconIndex };

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = Build();
        DialogResult = true;
        Close();
    }
}
