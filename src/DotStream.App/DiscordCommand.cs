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
    JoinChannel,

    /// <summary>
    /// The nth voice channel of whichever server you are currently in.
    ///
    /// Fills itself. A row of these follows you from server to server without anybody
    /// building a page per server, which is the difference between a deck you set up
    /// once and one you keep maintaining.
    /// </summary>
    ToggleVideo,
    ToggleScreenshare,

    ChannelSlot,

    /// <summary>
    /// Whatever channel you are in right now.
    ///
    /// For servers that move you into a channel created on the fly, where there is
    /// nothing to bind to in advance because it did not exist yet.
    /// </summary>
    CurrentChannel,
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
    /// <summary>Channel id for JoinChannel. Unused by the others.</summary>
    public string Target { get; init; } = "";

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
            DiscordAction.JoinChannel => "Channel",
            DiscordAction.ToggleVideo => "Camera",
            DiscordAction.ToggleScreenshare => "Share",
            DiscordAction.ChannelSlot => "",
            DiscordAction.CurrentChannel => "",
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

            // A speaker, because that is what Discord itself puts next to a voice
            // channel. Text channels get a hash there; borrowing the convention means
            // nobody has to learn ours.
            DiscordAction.JoinChannel => "volume-up",
            DiscordAction.ToggleVideo => "camera",
            DiscordAction.ToggleScreenshare => "screenshot",
            DiscordAction.ChannelSlot => "volume-up",
            DiscordAction.CurrentChannel => "volume-down",

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

        // Force, because otherwise Discord refuses while you are already in a call
        // rather than moving you, which is not what pressing a channel key means.
        DiscordAction.JoinChannel =>
            ("SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = Target, ["force"] = true }),

        // The caller resolves which channel a slot currently points at and passes it in
        // as Target, so from here these behave like any other join.
        DiscordAction.ChannelSlot or DiscordAction.CurrentChannel =>
            ("SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = Target, ["force"] = true }),

        // Undocumented, and confirmed to exist by the error they give without the
        // scope: 4006 invalid scope, where a command that does not exist gives 4002.
        DiscordAction.ToggleVideo => ("TOGGLE_VIDEO", null),
        DiscordAction.ToggleScreenshare => ("TOGGLE_SCREENSHARE", null),

        _ => ("GET_VOICE_SETTINGS", null)
    };

    public string Describe() => Action switch
    {
        DiscordAction.ToggleMute => "Discord mute",
        DiscordAction.ToggleDeafen => "Discord deafen",
        DiscordAction.LeaveVoice => "left the voice channel",
        DiscordAction.JoinChannel => $"joined {(Label.Length > 0 ? Label : "the channel")}",
        DiscordAction.ToggleVideo => "Discord camera",
        DiscordAction.ToggleScreenshare => "Discord screen share",
        DiscordAction.ChannelSlot => "joined that channel",
        DiscordAction.CurrentChannel => "already here",
        _ => "Discord"
    };
}

/// <summary>One voice channel and how many people are sitting in it.</summary>
public sealed record DiscordChannelState(string Id, string Name, int People);
