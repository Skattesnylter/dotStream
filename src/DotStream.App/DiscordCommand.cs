using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>What a key does to Discord.</summary>
public enum DiscordAction
{
    ToggleMute,
    ToggleDeafen,
    LeaveVoice,
}

/// <summary>
/// A key that drives Discord over its RPC pipe.
///
/// Mute and deafen exist as global keybinds too, and those work without any of this.
/// The difference is that a keybind fires and hopes, while this knows. Discord reports
/// its own state, so the key is lit when you are actually muted, and stays right when
/// you mute yourself by clicking in Discord instead of pressing the key.
///
/// That matters more here than anywhere else in this application. Being wrong about
/// whether your microphone is live is the one mistake this class of software can make
/// that a person cannot recover from afterwards.
/// </summary>
public sealed record DiscordBinding(DiscordAction Action, string Label = "", string Icon = "")
{
    /// <summary>See <see cref="HotkeyBinding.IconFile"/>.</summary>
    public string IconFile { get; init; } = "";

    public int IconIndex { get; init; }

    [JsonIgnore]
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Label) ? Label
        : Action switch
        {
            DiscordAction.ToggleMute => "Mute",
            DiscordAction.ToggleDeafen => "Deafen",
            DiscordAction.LeaveVoice => "Leave",
            _ => "Discord"
        };

    [JsonIgnore]
    public DeckIcon? ResolvedIcon =>
        IconLibrary.ByName(Icon)
        ?? IconLibrary.ByName(Action switch
        {
            DiscordAction.ToggleMute => "microphone",
            DiscordAction.ToggleDeafen => "mute",
            DiscordAction.LeaveVoice => "cross",
            _ => "person"
        });

    [JsonIgnore]
    public System.Windows.Media.Imaging.BitmapSource? FileImage => IconCache.Get(IconFile, IconIndex);

    /// <summary>
    /// The command and payload for a given current state.
    ///
    /// Toggles need to know where they are starting from: the RPC surface sets a value
    /// rather than flipping one, so the caller passes what Discord last reported.
    /// </summary>
    public (string Command, JsonObject? Args) Request(bool muted, bool deafened) => Action switch
    {
        DiscordAction.ToggleMute =>
            ("SET_VOICE_SETTINGS", new JsonObject { ["mute"] = !muted }),

        // Undeafening does not unmute, which matches what the Discord client does.
        DiscordAction.ToggleDeafen =>
            ("SET_VOICE_SETTINGS", new JsonObject { ["deaf"] = !deafened }),

        // A null channel is how leaving is expressed.
        DiscordAction.LeaveVoice =>
            ("SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = null }),

        _ => ("GET_VOICE_SETTINGS", null)
    };

    public string Describe() => Action switch
    {
        DiscordAction.ToggleMute => "Discord mute",
        DiscordAction.ToggleDeafen => "Discord deafen",
        DiscordAction.LeaveVoice => "left the voice channel",
        _ => "Discord"
    };
}
