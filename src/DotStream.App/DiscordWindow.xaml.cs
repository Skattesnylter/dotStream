using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace DotStream.App;

/// <summary>One server or voice channel, named for a person rather than by id.</summary>
public sealed record DiscordPlace(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Builds a Discord key.
///
/// Mute, deafen and leave need nothing looked up. Joining a channel does, and the
/// lists come from Discord rather than being typed: a channel id is not something
/// anyone knows by heart, and a name typed by hand goes stale the moment somebody
/// renames it.
/// </summary>
public partial class DiscordWindow : Window
{
    private readonly DiscordClient _discord;

    public DiscordWindow(DiscordBinding? existing, DiscordClient discord)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _discord = discord ?? throw new ArgumentNullException(nameof(discord));

        if (existing is not null)
        {
            foreach (ComboBoxItem item in ActionBox.Items)
                item.IsSelected = (string)item.Tag == existing.Action.ToString();

            LabelBox.Text = existing.Label;
            Pending = existing.Target;
            Icon = existing.Icon;
            IconFile = existing.IconFile;
            IconIndex = existing.IconIndex;
        }

        Loaded += async (_, _) => await ShowActionAsync();
    }

    /// <summary>The finished binding, valid once the dialog returns true.</summary>
    public DiscordBinding? Result { get; private set; }

    /// <summary>Channel to reselect once the lists have loaded.</summary>
    private string Pending { get; set; } = "";

    private new string Icon { get; set; } = "";

    private string IconFile { get; set; } = "";

    private int IconIndex { get; set; }

    private DiscordAction Selected =>
        ActionBox.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse(tag, out DiscordAction action)
            ? action
            : DiscordAction.ToggleMute;

    private async Task ShowActionAsync()
    {
        bool joining = Selected == DiscordAction.JoinChannel;
        ChannelPanel.Visibility = joining ? Visibility.Visible : Visibility.Collapsed;

        if (joining && GuildBox.Items.Count == 0) await LoadGuildsAsync();

        ShowPreview();
    }

    private async Task LoadGuildsAsync()
    {
        try
        {
            JsonNode? guilds = await _discord.CallAsync("GET_GUILDS");
            if (guilds?["guilds"] is not JsonArray list) return;

            foreach (JsonNode? guild in list)
            {
                string? id = guild?["id"]?.GetValue<string>();
                string? name = guild?["name"]?.GetValue<string>();

                if (id is not null && name is not null) GuildBox.Items.Add(new DiscordPlace(id, name));
            }

            if (GuildBox.Items.Count > 0) GuildBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Preview.Text = "Could not read your servers: " + ex.Message;
        }
    }

    private async void OnGuildChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GuildBox.SelectedItem is not DiscordPlace guild) return;

        ChannelBox.Items.Clear();

        try
        {
            JsonNode? channels = await _discord.CallAsync(
                "GET_CHANNELS", new JsonObject { ["guild_id"] = guild.Id });

            if (channels?["channels"] is not JsonArray list) return;

            foreach (JsonNode? channel in list)
            {
                // Type 2 is a voice channel. Text channels cannot be joined this way.
                if (channel?["type"]?.GetValue<int>() != 2) continue;

                string? id = channel["id"]?.GetValue<string>();
                string? name = channel["name"]?.GetValue<string>();

                if (id is not null && name is not null) ChannelBox.Items.Add(new DiscordPlace(id, name));
            }

            DiscordPlace? wanted = ChannelBox.Items.OfType<DiscordPlace>()
                .FirstOrDefault(c => c.Id == Pending);

            if (wanted is not null) ChannelBox.SelectedItem = wanted;
            else if (ChannelBox.Items.Count > 0) ChannelBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Preview.Text = "Could not read that server's channels: " + ex.Message;
        }
    }

    private async void OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        // Only the action box changes which fields matter; the rest just refresh.
        if (ReferenceEquals(sender, ActionBox)) await ShowActionAsync();
        else ShowPreview();
    }

    private void ShowPreview()
    {
        if (Preview is null) return;

        DiscordBinding binding = Build();
        (string command, JsonObject? args) = binding.Request(muted: false, deafened: false);

        Preview.Text = $"{command}{(args is null ? "" : "  " + args.ToJsonString())}\nkey reads: {binding.DisplayLabel}";
    }

    private DiscordBinding Build()
    {
        string label = LabelBox.Text.Trim();

        // A channel key with no label of its own gets the channel's name, which is
        // almost always what somebody wants and saves them typing it twice.
        if (label.Length == 0 && Selected == DiscordAction.JoinChannel &&
            ChannelBox.SelectedItem is DiscordPlace channel)
        {
            label = channel.Name;
        }

        return new DiscordBinding(Selected, label, Icon)
        {
            Target = ChannelBox.SelectedItem is DiscordPlace selected ? selected.Id : Pending,
            IconFile = IconFile,
            IconIndex = IconIndex
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        DiscordBinding binding = Build();

        if (binding.Action == DiscordAction.JoinChannel && binding.Target.Length == 0)
        {
            Preview.Text = "Pick a channel first.";
            return;
        }

        Result = binding;
        DialogResult = true;
        Close();
    }
}
