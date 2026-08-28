namespace DotStream.App;

/// <summary>
/// The layouts dotStream ships with for the applications it integrates with.
///
/// One source for both routes back to them: the menu, for somebody who has moved keys
/// around and wants to start over, and MCP, so an assistant that made a mess can offer
/// to undo it. Two hand-written copies of the same arrangement would drift apart, and
/// the one nobody looked at would be the one people got.
///
/// The arrangement follows the hardware rather than a list. Column order on this deck
/// runs right to left, so 13, 10, 7, 4, 1 is the top row read left to right, and 14,
/// 11, 8, 5, 2 is the middle. Back sits at 15.
/// </summary>
public static class IntegrationDefaults
{
    /// <summary>One key in a default layout: where it goes and what it does.</summary>
    public sealed record Key(int Index, object Binding);

    /// <summary>
    /// Discord: controls along the top, a self-filling channel row in the middle, and
    /// the channel you are in beside Back.
    ///
    /// The middle row is the point of the page. Five slots follow you between servers,
    /// so this layout is worth shipping rather than leaving everyone to discover that
    /// slots exist.
    /// </summary>
    public static IReadOnlyList<Key> Discord { get; } =
    [
        new(13, new DiscordBinding(DiscordAction.ToggleMute)),
        new(10, new DiscordBinding(DiscordAction.ToggleDeafen)),
        new(7, new DiscordBinding(DiscordAction.LeaveVoice)),
        new(4, new DiscordBinding(DiscordAction.ToggleVideo)),
        new(1, new DiscordBinding(DiscordAction.ToggleScreenshare)),

        new(14, new DiscordBinding(DiscordAction.ChannelSlot) { Target = "0" }),
        new(11, new DiscordBinding(DiscordAction.ChannelSlot) { Target = "1" }),
        new(8, new DiscordBinding(DiscordAction.ChannelSlot) { Target = "2" }),
        new(5, new DiscordBinding(DiscordAction.ChannelSlot) { Target = "3" }),
        new(2, new DiscordBinding(DiscordAction.ChannelSlot) { Target = "4" }),

        new(12, new DiscordBinding(DiscordAction.CurrentChannel)),
    ];

    /// <summary>
    /// OBS: recording and streaming on the top row, mute for the two audio sources
    /// every install has.
    ///
    /// No scene keys, because scenes are named by whoever made them and a default
    /// cannot know them. An assistant can add those from deck_integrations.
    /// </summary>
    public static IReadOnlyList<Key> Obs { get; } =
    [
        new(13, new ObsBinding(ObsAction.ToggleRecord)),
        new(10, new ObsBinding(ObsAction.ToggleStream)),
        new(7, new ObsBinding(ObsAction.ToggleMute, "Mic/Aux", "Mic")),
        new(4, new ObsBinding(ObsAction.ToggleMute, "Desktop Audio", "Desktop")),
    ];

    /// <summary>The layout for a name, or null if there is no default for it.</summary>
    public static IReadOnlyList<Key>? For(string name) => name.Trim().ToLowerInvariant() switch
    {
        "discord" => Discord,
        "obs" or "obs studio" => Obs,
        _ => null
    };

    /// <summary>The names that have a default, for error messages and menus.</summary>
    public static IReadOnlyList<string> Names { get; } = ["Discord", "OBS"];
}
