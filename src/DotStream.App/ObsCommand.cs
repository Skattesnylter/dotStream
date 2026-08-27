using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>What a key does to OBS.</summary>
public enum ObsAction
{
    SwitchScene,
    ToggleRecord,
    ToggleStream,
    ToggleMute,
}

/// <summary>
/// A key that drives OBS Studio.
///
/// The point of doing this over the websocket rather than by sending a hotkey is that
/// OBS answers back. A scene key knows whether its scene is live, so it can be lit,
/// and a mute key knows whether the source is actually muted rather than assuming its
/// last press worked. A blind keystroke can do neither.
/// </summary>
public sealed record ObsBinding(ObsAction Action, string Target = "", string Label = "", string Icon = "")
{
    /// <summary>See <see cref="HotkeyBinding.IconFile"/>.</summary>
    public string IconFile { get; init; } = "";

    public int IconIndex { get; init; }

    [JsonIgnore]
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Label) ? Label
        : Action switch
        {
            ObsAction.SwitchScene => Target.Length > 0 ? Target : "Scene",
            ObsAction.ToggleRecord => "Record",
            ObsAction.ToggleStream => "Stream",
            ObsAction.ToggleMute => Target.Length > 0 ? Target : "Mute",
            _ => "OBS"
        };

    [JsonIgnore]
    public DeckIcon? ResolvedIcon =>
        IconLibrary.ByName(Icon)
        ?? IconLibrary.ByName(Action switch
        {
            ObsAction.SwitchScene => "image",
            ObsAction.ToggleRecord => "record",
            ObsAction.ToggleStream => "send",
            ObsAction.ToggleMute => "microphone",
            _ => "play"
        });

    [JsonIgnore]
    public System.Windows.Media.Imaging.BitmapSource? FileImage => IconCache.Get(IconFile, IconIndex);

    /// <summary>The request and payload this action sends.</summary>
    public (string Type, JsonObject? Data) Request() => Action switch
    {
        ObsAction.SwitchScene => ("SetCurrentProgramScene", new JsonObject { ["sceneName"] = Target }),
        ObsAction.ToggleRecord => ("ToggleRecord", null),
        ObsAction.ToggleStream => ("ToggleStream", null),
        ObsAction.ToggleMute => ("ToggleInputMute", new JsonObject { ["inputName"] = Target }),
        _ => ("GetVersion", null)
    };

    public string Describe() => Action switch
    {
        ObsAction.SwitchScene => $"OBS scene \"{Target}\"",
        ObsAction.ToggleRecord => "OBS recording",
        ObsAction.ToggleStream => "OBS streaming",
        ObsAction.ToggleMute => $"OBS mute for \"{Target}\"",
        _ => "OBS"
    };
}
