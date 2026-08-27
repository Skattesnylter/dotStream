using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace DotStream.App;

/// <summary>
/// Builds an OBS key, with the scene and source lists read from OBS itself.
///
/// Typing a scene name by hand is how you get a key that silently does nothing after
/// somebody renames a scene. OBS knows what it has, so this asks it, and the dialog
/// doubles as the connection test: if the lists fill in, the websocket works.
/// </summary>
public partial class ObsWindow : Window
{
    private readonly ObsClient _obs;

    private string _icon = "";
    private string _iconFile = "";
    private int _iconIndex;

    public ObsWindow(ObsBinding? existing, ObsClient obs)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _obs = obs ?? throw new ArgumentNullException(nameof(obs));

        if (existing is not null)
        {
            foreach (ComboBoxItem item in ActionBox.Items)
                item.IsSelected = (string)item.Tag == existing.Action.ToString();

            LabelBox.Text = existing.Label;
            _icon = existing.Icon;
            _iconFile = existing.IconFile;
            _iconIndex = existing.IconIndex;
            Pending = existing.Target;
        }

        LabelBox.TextChanged += (_, _) => ShowPreview();

        Loaded += async (_, _) => await FillFromObsAsync();
    }

    /// <summary>The target to reselect once the lists have loaded.</summary>
    private string Pending { get; set; } = "";

    /// <summary>The finished binding, valid once the dialog returns true.</summary>
    public ObsBinding? Result { get; private set; }

    private ObsAction SelectedAction =>
        ActionBox.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse(tag, out ObsAction action)
            ? action
            : ObsAction.SwitchScene;

    /// <summary>
    /// Asks OBS what it has. Failure here is the normal case when OBS is closed or its
    /// websocket server was never switched on, so it explains rather than throws.
    /// </summary>
    private async Task FillFromObsAsync()
    {
        if (!_obs.IsConnected)
        {
            ObsConnectionInfo? config = ObsClient.ReadConfig();

            StateLabel.Text = config is null
                ? "OBS has not been run on this machine, so there is nothing to connect to yet."
                : config.Enabled
                    ? $"OBS is not running, or is not answering on port {config.Port}. Start it and reopen this."
                    : "OBS is installed but its websocket server is off. Turn it on in Tools, WebSocket Server Settings.";

            SaveButton.IsEnabled = false;
            return;
        }

        try
        {
            await LoadTargetsAsync();
            StateLabel.Text = "Connected to OBS.";
        }
        catch (Exception ex)
        {
            StateLabel.Text = "Could not read from OBS: " + ex.Message;
            SaveButton.IsEnabled = false;
        }
    }

    private async Task LoadTargetsAsync()
    {
        TargetBox.Items.Clear();

        switch (SelectedAction)
        {
            case ObsAction.SwitchScene:
                TargetPanel.Visibility = Visibility.Visible;
                TargetLabel.Text = "Which scene?";

                JsonNode? scenes = await _obs.CallAsync("GetSceneList");

                // OBS returns scenes in reverse of how they are shown in its own list.
                if (scenes?["scenes"] is JsonArray list)
                    foreach (JsonNode? scene in list.Reverse())
                        if (scene?["sceneName"]?.GetValue<string>() is { } name)
                            TargetBox.Items.Add(name);
                break;

            case ObsAction.ToggleMute:
                TargetPanel.Visibility = Visibility.Visible;
                TargetLabel.Text = "Which audio source?";

                JsonNode? inputs = await _obs.CallAsync("GetInputList");

                if (inputs?["inputs"] is JsonArray all)
                    foreach (JsonNode? input in all)
                    {
                        // Only sources that have audio can be muted.
                        string kind = input?["inputKind"]?.GetValue<string>() ?? "";
                        if (!kind.Contains("audio", StringComparison.OrdinalIgnoreCase) &&
                            !kind.Contains("wasapi", StringComparison.OrdinalIgnoreCase)) continue;

                        if (input?["inputName"]?.GetValue<string>() is { } name)
                            TargetBox.Items.Add(name);
                    }
                break;

            default:
                // Recording and streaming have nothing to point at.
                TargetPanel.Visibility = Visibility.Collapsed;
                break;
        }

        if (Pending.Length > 0 && TargetBox.Items.Contains(Pending)) TargetBox.SelectedItem = Pending;
        else if (TargetBox.Items.Count > 0) TargetBox.SelectedIndex = 0;

        ShowPreview();
    }

    private async void OnActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !_obs.IsConnected) return;

        try { await LoadTargetsAsync(); }
        catch (Exception ex) { StateLabel.Text = "Could not read from OBS: " + ex.Message; }
    }

    private void OnTargetChanged(object sender, SelectionChangedEventArgs e) => ShowPreview();

    private void ShowPreview()
    {
        if (Preview is null) return;

        ObsBinding binding = Build();

        (string type, JsonObject? data) = binding.Request();
        string payload = data is null ? "" : "  " + data.ToJsonString();

        Preview.Text = $"{type}{payload}\nkey reads: {binding.DisplayLabel}";
    }

    private ObsBinding Build() => new(
        SelectedAction,
        TargetBox.SelectedItem as string ?? "",
        LabelBox.Text.Trim(),
        _icon)
    {
        IconFile = _iconFile,
        IconIndex = _iconIndex
    };

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ObsBinding binding = Build();

        if (binding.Action is ObsAction.SwitchScene or ObsAction.ToggleMute && binding.Target.Length == 0)
        {
            StateLabel.Text = "Pick which one first.";
            return;
        }

        Result = binding;
        DialogResult = true;
        Close();
    }
}
