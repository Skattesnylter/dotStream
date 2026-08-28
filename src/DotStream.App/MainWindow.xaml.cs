using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DotStream.App.Mcp;
using DotStream.Core;
using DotStream.Hid;
using DotStream.Icons;
using DotStream.Media;
using DotStream.Rendering;
using DotStream.Rendering.Widgets;
using DotStream.Simulator;

namespace DotStream.App;

/// <summary>
/// One row in the actions palette. Carries either a catalogue action or a hotkey the
/// user has already defined for this page - both are things you drag onto a key.
/// </summary>
public sealed record ActionListItem(
    string Group,
    string Name,
    string Description,
    BitmapSource Thumbnail,
    ActionDefinition? Definition = null,
    HotkeyBinding? Hotkey = null);

public partial class MainWindow : Window, IDeckAgent
{
    private const string AppDragFormat = "dotstream/app";
    private const string ActionDragFormat = "dotstream/action";
    private const string RootPageId = "root";

    private readonly DeckSimulatorControl _deckView = new();
    private readonly DeckNavigator _navigator = new();
    private readonly MediaHub _media = new();
    private readonly Dictionary<string, DeckPage> _pages = [];
    private readonly Dictionary<int, DateTime> _widgetDue = [];
    private readonly DispatcherTimer _timer;
    private readonly AppSelectionStore _selection = AppSelectionStore.Load();
    private readonly LabelStore _labels = LabelStore.Load();
    private readonly MatchStore _matches = MatchStore.Load();

    /// <summary>
    /// Rebuilt whenever the cell size changes, which is why it is not readonly: the
    /// renderer draws at a fixed size and there is nothing to adjust after the fact.
    /// </summary>
    private CellRenderer _renderer;

    private ActionCatalog? _catalog;
    private IDeckTransport? _transport;
    private MirroringTransport? _mirror;

    /// <summary>
    /// The rotation currently on the hardware, which is not the same as the saved one
    /// while the calibration window is open. Kept separate so a preview cannot be
    /// written to settings.json by something unrelated saving at the wrong moment.
    /// </summary>
    private int _liveRotation;

    /// <summary>The attached device's own product string, kept for diagnostics.</summary>
    private string? _deckProduct;
    private DeckController? _controller;
    private IReadOnlyList<InstalledApp> _apps = [];
    private int _steamGameCount;

    private Point _dragOrigin;

    /// <summary>Guards against writing the profile back while it is being restored.</summary>
    private bool _suspendSave;

    private TrayIcon? _tray;
    private bool _exiting;
    private bool _explainedTray;

    private TextLab _textLab = TextLab.Load();
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _ready;

    private ForegroundWatcher? _watcher;

    /// <summary>The page automatic switching opened, so it knows what it may close.</summary>
    private string? _autoPushedPageId;

    /// <summary>
    /// While this is in the future, automatic switching stays out of the way. Set by
    /// navigating by hand and refreshed by every key press.
    /// </summary>
    private DateTime _pinnedUntil = DateTime.MinValue;

    private readonly DispatcherTimer _focusSettled =
        new() { Interval = TimeSpan.FromMilliseconds(400) };

    private ForegroundApp? _pendingForeground;

    private readonly AgentState _agent = new();
    private readonly McpClient _mcpClient = new();
    private readonly ObsClient _obs = new();
    private readonly DiscordClient _discord = new();

    // What Discord says about the microphone, so keys are lit from fact rather than
    // from what the key last did.
    private bool _discordMuted;
    private bool _discordDeafened;

    // What OBS says is happening, so keys can be lit without asking on every repaint.
    private string? _obsScene;
    private bool _obsRecording;
    private bool _obsStreaming;
    private readonly Dictionary<string, bool> _obsMuted = new(StringComparer.Ordinal);

    // What each scene currently looks like. OBS renders these itself, so a scene key
    // can show the scene rather than a generic icon.
    private readonly Dictionary<string, BitmapSource> _obsThumbnails = new(StringComparer.Ordinal);
    private DateTime _obsThumbsDue = DateTime.MinValue;
    private McpServer? _mcp;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _renderer = new CellRenderer(_settings.CellPixels);
        _liveRotation = _settings.CellRotation;

        DeckHost.Content = _deckView;

        _deckView.CellRightClicked += OnCellRightClicked;
        _deckView.CellDropped += OnCellDropped;
        _deckView.CanDragCell = index => _navigator.Current?.Get(index) is not null;

        _navigator.Changed += OnPageChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private DeckPage RootPage => _pages[RootPageId];

    // ---------------------------------------------------------------- lifecycle

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _tray = new TrayIcon();
        _tray.OpenRequested += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        _tray.ExitRequested += (_, _) => Dispatcher.Invoke(ExitApplication);

        // Real hardware wins when it is plugged in, but the window keeps drawing
        // either way: with a deck attached the two run in parallel, so what you see on
        // screen is what is on the desk. Nothing above this line knows the difference.
        var screen = new SimulatorTransport(_deckView);

        // Always the mirror, whether or not a deck is attached right now. It watches for
        // one either way, which is what makes plugging in later work at all - before
        // this, a deck that arrived after startup was never looked for again, and the
        // only cure was restarting the application.
        //
        // The rotation goes on every transport the mirror opens, including the
        // replacement built after a reconnect - which would otherwise come back at its
        // default and quietly undo a calibration.
        var mirror = new MirroringTransport(HidTransport.TryOpen(), screen, HidTransport.TryOpen,
            device =>
            {
                if (device is not HidTransport hid) return;

                hid.Rotation = _liveRotation;

                // The callback sees every device the mirror opens, including the one
                // built after a reconnect, so this is the one place the product string
                // is always current without the mirror having to know what HID is.
                _deckProduct = hid.ProductName;
            });

        _mirror = mirror;

        mirror.Disconnected += (_, _) => Dispatcher.Invoke(() =>
            StatusLabel.Text = "The deck was unplugged. The window still works; plug it back in and it picks up again.");

        // A deck that turns up shows its own boot logo, and only this side knows what
        // belongs there instead. Clearing first matters: RepaintKeys covers 1-15, while
        // the info cells wait for their widget interval, so without it the vendor logo
        // sits in column five for as long as a second.
        mirror.Reconnected += (_, _) => Dispatcher.InvokeAsync(async () =>
        {
            await mirror.ClearAllAsync();

            _controller?.InvalidateAll();
            _widgetDue.Clear();

            RepaintKeys(highPriority: true);

            ShowTransport();
            StatusLabel.Text = "The deck is connected.";
        });

        _transport = mirror;

        _controller = new DeckController(_transport);
        _controller.KeyPressed += OnKeyPressed;
        _controller.KeyReleased += OnKeyReleased;
        _controller.UploadFailed += (_, ex) =>
            Dispatcher.Invoke(() => StatusLabel.Text = "Upload failed: " + ex.Message);

        await _transport.ConnectAsync();

        // The deck powers on showing the vendor's own logo on a white ground, and its
        // cells are persistent framebuffers - an image only overwrites the pixels it
        // covers, so whatever is not painted stays. Reconnecting already cleared for
        // this reason; a cold start needs it just as much, and skipping it was why
        // white kept showing through between cells on the first run after plugging in.
        await _transport.ClearAllAsync();

        ShowTransport();

        BrightnessSlider.Value = _settings.Brightness;
        await _transport.SetBrightnessAsync(_settings.Brightness);

        UpdateFollowButton();

        // Read from the registry rather than remembered in settings.json, so the tick
        // still tells the truth after somebody turns it off in Task Manager.
        StartWithWindowsMenuItem.IsChecked = StartWithWindows.IsEnabled;

        BuildPinMenu();

        UpdateMcpMenuItem();
        if (_settings.McpEnabled) StartMcp(announce: false);

        _focusSettled.Tick += OnFocusSettled;

        _watcher = new ForegroundWatcher();
        _watcher.Changed += OnForegroundChanged;

        LabelSizeSlider.Value = _textLab.LabelSize;
        _ready = true;
        ApplyTextLab();

        await _media.InitialiseAsync();
        _catalog = new ActionCatalog(_media);

        _pages[RootPageId] = new DeckPage { Id = RootPageId, Title = "Home" };
        _navigator.SetRoot(RootPage);

        _timer.Start();

        await LoadAppsAsync();

        // Last, not first: started at login the deck should come up painted, and hiding
        // before the apps are loaded would leave it blank for as long as that takes.
        // Both optional and usually not running, so these are last and quiet.
        await ConnectObsAsync();
        await ConnectDiscordAsync();
        if (App.StartHidden) HideToTray();
    }

    /// <summary>
    /// Closing the window hides it. dotStream is a background service that happens to
    /// have an editor - the deck must keep responding to key presses whether or not
    /// the window is on screen. Exit is deliberate: File &gt; Exit, or the tray menu.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;

        if (_explainedTray) return;

        _tray?.ShowBalloon("dotStream is still running",
            "The deck stays live in the background. Double-click the tray icon to reopen, or use Exit to quit.");
        _explainedTray = true;
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;

        // Leave the deck dark. The cells hold their last image with nothing running to
        // explain it, so a closed application otherwise looks like a live one - and the
        // keys under those pictures no longer do anything.
        //
        // This has to happen before Close(), not in Closed. That handler is async void,
        // so WPF carries on tearing the application down at its first await, and the
        // mirroring transport awaits the dispatcher before it writes to the device -
        // the process was gone before CLE ever left the machine.
        if (_transport is not null)
        {
            try
            {
                await _transport.ClearAllAsync();

                // Black pixels are not a dark deck. The panel keeps its backlight on,
                // so eighteen cleared cells still glow enough to be the brightest thing
                // in a dark room - which was reason enough to reach behind the machine
                // and pull the cable. LIG 0 is what actually turns it off.
                //
                // Nothing has to put it back: startup and reconnect both set the saved
                // brightness after connecting.
                await _transport.SetBrightnessAsync(0);

                // Then ask it to sleep. LIG 0 turns out to be the lowest backlight
                // step rather than off, so a little still leaks through the panel.
                // Whether HAN does better is the open question this is here to answer.
                await _transport.SleepAsync();
            }
            catch (Exception ex) { DeckLog.Note("shutdown", "could not darken the deck: " + ex.Message); }
        }

        Close();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _focusSettled.Stop();

        if (_mcp is not null) await _mcp.DisposeAsync();
        await _obs.DisposeAsync();
        await _discord.DisposeAsync();
        _mcpClient.Dispose();
        _watcher?.Dispose();
        _tray?.Dispose();

        // The deck was cleared in ExitApplication, which is early enough for the write
        // to reach it. Anything that gets here without going through that - a log-off,
        // a kill - leaves the last picture up, and the cold-start clear handles it.
        if (_controller is not null)
            await _controller.DisposeAsync();
    }

    // ------------------------------------------------------------------- paging

    private void OnPageChanged(object? sender, EventArgs e)
    {
        DeckPage? page = _navigator.Current;
        bool atRoot = _navigator.IsAtRoot;

        BreadcrumbLabel.Text = atRoot ? "Home" : "Home  ›  " + (page?.Title ?? page?.Id ?? "");
        BackButton.Visibility = atRoot ? Visibility.Collapsed : Visibility.Visible;

        // Applications belong on the home page, actions on an app's own page. Showing
        // both at once left each list with its own scrollbar and no room to breathe.
        AppsPanel.Visibility = atRoot ? Visibility.Visible : Visibility.Collapsed;
        ActionsPanel.Visibility = atRoot ? Visibility.Collapsed : Visibility.Visible;

        ActionsSubtitle.Text = atRoot
            ? ""
            : $"drag onto a key on the {page?.Title ?? "current"} page";

        // Media keys on an application's page address that application. On the home
        // page there is no such context, so they fall back to whatever is playing.
        _media.PreferredSource = page?.Id.StartsWith("app:", StringComparison.Ordinal) == true
            ? page.Id[4..]
            : null;

        if (!atRoot) ShowActionsFor(page);

        // Arriving home closes the page automatic switching opened, but it must not
        // clear the pin.
        //
        // It used to. Pressing Back set the pin, the pop landed here, the pin was wiped,
        // and the very next foreground event pushed the same page straight back - so
        // Back looked broken. Press it twice in a row and the second press arrived
        // during the instant home was showing, where cell 15 is not Back at all: on
        // this profile it is Excel, which duly launched.
        //
        // The pin means "the user just did something deliberate, leave the deck alone
        // for a moment". Going home is exactly such an act. It expires on its own.
        if (atRoot) _autoPushedPageId = null;

        RepaintKeys(highPriority: true);
    }

    /// <summary>
    /// Shows the actions that make sense on this page.
    ///
    /// Media transport on a Word page is noise - Word has no media session and never
    /// will. The test is whether the app actually has one right now, with an escape
    /// hatch for a page that already uses them so an existing setup never loses the
    /// palette it was built with.
    /// </summary>
    private void ShowActionsFor(DeckPage? page)
    {
        if (_catalog is null || page is null) return;

        // A proposal is not a page you edit - it is a question with two answers. Its
        // palette offered "Add a Hotkey" and the media transport, neither of which
        // means anything here, and an empty library next to your own page's keys reads
        // as though the keys had been lost.
        if (page.Id.StartsWith("agent:", StringComparison.Ordinal))
        {
            ActionList.ItemsSource = null;
            ActionsSubtitle.Text = "waiting for you to accept or reject on the deck";
            return;
        }

        bool media = PageWantsMedia(page);
        string pageGroup = (page.Title ?? "This page").ToUpperInvariant();

        var items = new List<ActionListItem>();

        // The page's own hotkeys first: on a Word page, Word's things are what you
        // came for. Everything generic sits under a second heading rather than being
        // hidden, so nobody has to wonder where the volume keys went.
        foreach (HotkeyBinding hotkey in LibraryFor(page.Id))
        {
            items.Add(new ActionListItem(
                pageGroup, hotkey.DisplayLabel, hotkey.Combination,
                _renderer.Render(HotkeyVisual(hotkey)).Image, Hotkey: hotkey));
        }

        if (_catalog.ById("input.hotkey") is { } add)
        {
            items.Add(new ActionListItem(
                pageGroup, "Add a Hotkey", "define a new one for this page",
                _renderer.Render(add.Preview).Image, add));
        }

        // Only navigation and, where the app has audio, its transport. Volume, mute
        // and MCP calls are not about this application, and a palette that lists them
        // on a Word page is answering a question nobody asked. They are still one
        // right-click away on any key.
        foreach (ActionDefinition action in _catalog.Actions)
        {
            // OBS follows the same rule as the media transport: offered when there is
            // something to control, absent when there is not. A key that talks to a
            // program which is not running is a key that does nothing, and a palette
            // full of those is how software stops being trusted.
            bool belongs = action.Category == "Navigation"
                           || (media && action.Category == "Media")
                           || (action.Id == "obs.control" && _obs.IsConnected)
                           || (action.Id == "discord.control" && _discord.IsConnected);

            if (!belongs) continue;

            items.Add(new ActionListItem(
                "GENERAL", action.Name, action.Category,
                _renderer.Render(action.Preview).Image, action));
        }

        var view = new CollectionViewSource { Source = items };
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionListItem.Group)));
        ActionList.ItemsSource = view.View;

        ActionsSubtitle.Text = media
            ? $"drag onto a key on the {page.Title ?? "current"} page"
            : $"media controls hidden - {page.Title ?? "this app"} has no audio session. "
              + "Right-click a key to place them anyway.";
    }

    private bool PageWantsMedia(DeckPage? page)
    {
        if (page is null) return true;

        // Already using them - leave the palette alone.
        if (page.Cells.Values.Any(b => b.Tag is string id && id.StartsWith("media.", StringComparison.Ordinal)))
            return true;

        InstalledApp? app = page.Cells.Values
            .Select(b => b.Tag as InstalledApp)
            .FirstOrDefault(a => a is not null);

        // The page belongs to an app; ask whether that app is playing anything.
        string? pageAppId = page.Id.StartsWith("app:", StringComparison.Ordinal) ? page.Id[4..] : null;
        if (pageAppId is null) return true;

        InstalledApp? owner = app ?? _apps.FirstOrDefault(a => a.AppUserModelId == pageAppId);
        return owner is null || _media.HasSessionFor(owner.AppUserModelId, owner.Name);
    }

    private void RepaintKeys(bool highPriority)
    {
        if (_controller is null) return;

        DeckPage? page = _navigator.Current;

        foreach (int index in DeckLayout.Keys())
        {
            DeckButton? button = page?.Get(index);
            CellVisual visual = button?.Visual() ?? EmptyKeyVisual(index);

            // Custom labels are applied here rather than inside each button factory,
            // so one rule covers apps, actions and anything added later.
            if (button is not null && _labels.Get(IdentityOf(button)) is { } custom)
                visual = visual with { Label = custom };

            _controller.Update(index, _renderer.Render(visual), highPriority);
        }
    }

    /// <summary>
    /// Names the transport in the toolbar, with the device's own product string on
    /// hover.
    ///
    /// Split because that string is a factory placeholder on the measured unit -
    /// "HOTSPOTEKUSB HID DEMO" - which reads as a defect on screen while still being
    /// the first thing worth knowing when two variants behave differently.
    /// </summary>
    private void ShowTransport()
    {
        if (_transport is null) return;

        TransportLabel.Text = "  -  " + _transport.Name;

        string? product = _transport.IsConnected ? _deckProduct : null;

        TransportLabel.ToolTip = product is null
            ? null
            : $"The device reports itself as “{product}”.";

        if (product is not null) DeckLog.Out("deck", $"{_transport.Name}  product string: {product}");
    }

    // ----------------------------------------------------------------- startup

    private void OnToggleStartWithWindows(object sender, RoutedEventArgs e)
    {
        bool wanted = StartWithWindowsMenuItem.IsChecked;

        if (!StartWithWindows.Set(wanted))
        {
            // Put the tick back where reality is, rather than leaving a checkbox
            // claiming something that did not happen.
            StartWithWindowsMenuItem.IsChecked = !wanted;
            StatusLabel.Text = "Windows would not let dotStream change that. View > Console has the details.";
            return;
        }

        StatusLabel.Text = wanted
            ? "dotStream will start in the tray when you sign in."
            : "dotStream will no longer start with Windows.";
    }

    // ------------------------------------------------------------- calibration

    /// <summary>
    /// Opens the calibration window and keeps the deck in step with its sliders.
    ///
    /// The window owns nothing. It asks for a geometry and this draws it, which is why
    /// calibrating no longer means closing dotStream first: the transport that is
    /// already open does the work, and you calibrate against your own keys rather than
    /// a test image in a separate tool.
    /// </summary>
    private async void OnCalibrate(object sender, RoutedEventArgs e)
    {
        int size = _settings.CellPixels;
        int rotation = _settings.CellRotation;

        var window = new CalibrationWindow(
            _transport?.Name ?? "No deck attached - the window still shows the result",
            size, rotation, ApplyCellGeometryAsync)
        {
            Owner = this
        };

        bool saved = window.ShowDialog() == true;

        if (saved)
        {
            _settings.CellPixels = window.CellPixels;
            _settings.CellRotation = window.CellRotation;
            _settings.Save();

            StatusLabel.Text = $"Cells calibrated to {window.CellPixels}x{window.CellPixels} at {window.CellRotation}°.";
        }
        else
        {
            StatusLabel.Text = "Calibration cancelled.";
        }

        // Either way the deck is showing whatever the sliders last asked for, which may
        // be the measuring pattern. Put the real keys back.
        await ApplyCellGeometryAsync(_settings.CellPixels, _settings.CellRotation, pattern: false);
    }

    /// <summary>
    /// Redraws the whole deck at a given cell geometry.
    ///
    /// Clearing first is not optional. A cell is a persistent framebuffer, so shrinking
    /// the image leaves a ring of the larger one behind - and during calibration that
    /// ring is exactly what somebody is trying to measure.
    /// </summary>
    private async Task ApplyCellGeometryAsync(int size, int rotation, bool pattern)
    {
        if (_controller is null) return;

        _renderer = new CellRenderer(size);

        _liveRotation = rotation;
        _mirror?.ReconfigureDevice();

        if (_transport is not null) await _transport.ClearAllAsync();

        _controller.InvalidateAll();

        if (pattern)
        {
            foreach (int index in DeckLayout.AllCells())
            {
                BitmapSource image = CalibrationPattern.Render(size, index);
                _controller.Update(index, new RenderedCell(image, $"calibration-{size}-{index}"), highPriority: true);
            }

            return;
        }

        // Widgets redraw on their own schedule, so without this the info cells sit
        // blank for as long as their interval after leaving the pattern behind.
        _widgetDue.Clear();

        RepaintKeys(highPriority: true);
    }

    /// <summary>
    /// What a custom label is remembered against - the app or the action, never the
    /// cell. Clearing a key and adding the same thing back later keeps the name.
    /// </summary>
    private static string? IdentityOf(DeckButton button) => button.Tag switch
    {
        InstalledApp app => app.AppUserModelId,
        string actionId => actionId,
        _ => null
    };

    private DeckPage GetOrCreateAppPage(InstalledApp app)
    {
        string id = "app:" + app.AppUserModelId;

        if (_pages.TryGetValue(id, out DeckPage? existing))
            return existing;

        var page = new DeckPage
        {
            Id = id,
            Title = app.Name,
            // Media buttons show live state, so this page repaints on the timer.
            RefreshInterval = TimeSpan.FromMilliseconds(500)
        };

        // Seed a way out. Everything else is for the user to drag in - but leaving a
        // page with no exit would be a trap, so this one button is not optional.
        if (_catalog?.ById("nav.back") is { } back)
            page.SetAt(2, 0, back.Create(_navigator));

        _pages[id] = page;
        SaveProfile();
        return page;
    }

    /// <summary>Hotkeys defined for a page, whether or not they sit on a key.</summary>
    private readonly Dictionary<string, List<HotkeyBinding>> _pageLibraries = [];

    private void SaveProfile()
    {
        if (_suspendSave) return;
        ProfileStore.Save(_pages.Values, _pageLibraries);
    }

    private List<HotkeyBinding> LibraryFor(string pageId)
    {
        if (!_pageLibraries.TryGetValue(pageId, out List<HotkeyBinding>? library))
        {
            library = [];
            _pageLibraries[pageId] = library;
        }

        return library;
    }

    /// <summary>
    /// Rebuilds the saved layout, or lays out a starter deck on first run.
    ///
    /// Cells whose app is no longer installed, or whose action no longer exists, are
    /// dropped rather than failing the whole restore - an uninstall should cost you
    /// one key, not the profile.
    /// </summary>
    private void RestoreOrSeed()
    {
        ProfileRecord? profile = ProfileStore.Load();

        if (profile is null || profile.Pages.Count == 0)
        {
            SeedDefaultDeck();
            return;
        }

        _suspendSave = true;
        bool migrated = false;

        try
        {
            Dictionary<string, InstalledApp> byId = _apps
                .GroupBy(a => a.AppUserModelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            _pages.Clear();
            _pageLibraries.Clear();
            int dropped = 0;

            foreach (PageRecord record in profile.Pages)
            {
                var page = new DeckPage
                {
                    Id = record.Id,
                    Title = record.Title,
                    RefreshInterval = record.Dynamic ? TimeSpan.FromMilliseconds(500) : null
                };

                // The library is not derived from the cells - a hotkey can be defined
                // and not yet placed, and taking a key off a page must not un-define
                // it. Restoring only the cells left the palette empty after every
                // restart while the definitions sat in the file, unread.
                if (record.Hotkeys.Count > 0) LibraryFor(record.Id).AddRange(record.Hotkeys);

                foreach ((string key, CellRecord cell) in record.Cells)
                {
                    if (!int.TryParse(key, out int index) || !DeckLayout.IsValid(index)) continue;

                    DeckButton? button = cell.Kind switch
                    {
                        "app" when byId.TryGetValue(cell.Value, out InstalledApp? app) => CreateAppButton(app),
                        "action" when _catalog?.ById(cell.Value) is { } action => action.Create(_navigator),
                        "widget" when InfoWidgets.ById(cell.Value) is { } widget =>
                            CreateWidgetButton(widget, cell.Theme?.ToTheme(widget.DefaultTheme)),
                        "mcp" when JsonSerializer.Deserialize<McpBinding>(cell.Value) is { } binding =>
                            BuildMcpButton(binding),
                        "hotkey" when JsonSerializer.Deserialize<HotkeyBinding>(cell.Value) is { } hotkey =>
                            BuildHotkeyButton(hotkey),
                        "text" when JsonSerializer.Deserialize<TextMacroBinding>(cell.Value) is { } macro =>
                            BuildTextButton(macro),
                        "run" when JsonSerializer.Deserialize<RunBinding>(cell.Value) is { } run =>
                            BuildRunButton(run),
                        "link" when JsonSerializer.Deserialize<LinkBinding>(cell.Value) is { } link =>
                            BuildLinkButton(link),
                        "obs" when JsonSerializer.Deserialize<ObsBinding>(cell.Value) is { } obs =>
                            BuildObsButton(obs),
                        "discord" when JsonSerializer.Deserialize<DiscordBinding>(cell.Value) is { } discord =>
                            BuildDiscordButton(discord),
                        _ => null
                    };

                    if (button is null) dropped++;
                    else page.Set(index, button);
                }

                _pages[page.Id] = page;
            }

            if (!_pages.ContainsKey(RootPageId))
                _pages[RootPageId] = new DeckPage { Id = RootPageId, Title = "Home" };

            // Profiles written before the info cells were assignable have no entries
            // for 16-18. Seed them rather than leaving three blank cells behind.
            if (DeckLayout.InfoCells().All(i => RootPage.Get(i) is null))
            {
                SeedInfoCells();
                migrated = true;
            }

            _navigator.SetRoot(RootPage);

            StatusLabel.Text = dropped == 0
                ? $"Restored {_pages.Count} page(s) from {ProfileStore.FilePath}."
                : $"Restored {_pages.Count} page(s); {dropped} key(s) skipped - app or action no longer available.";
        }
        finally
        {
            _suspendSave = false;
        }

        // Writing has to wait until saving is unblocked again, or the migration lives
        // only in memory and runs afresh on every start.
        if (migrated) SaveProfile();
    }

    private void SeedDefaultDeck()
    {
        // Visual reading order - key 1 is physically the top-RIGHT cell, so iterating
        // 1..14 would lay the list out right-to-left.
        RootPage.Cells.Clear();

        foreach ((int position, InstalledApp app) in VisibleApps.Take(14).Select((a, i) => (i, a)))
            RootPage.SetAt(position / 5, position % 5, CreateAppButton(app));

        SeedInfoCells();

        _navigator.SetRoot(RootPage);
        SaveProfile();
    }

    private void SeedInfoCells()
    {
        foreach ((int index, string id) in new[] { (16, "cpu"), (17, "ram"), (18, "clock") })
        {
            if (InfoWidgets.ById(id) is { } widget)
                RootPage.Set(index, CreateWidgetButton(widget));
        }
    }

    private void OnResetLayout(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(
            this,
            "Clear every key and page, and lay out a starter deck again?\n\nThis cannot be undone.",
            "Reset deck layout",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;

        ProfileStore.Delete();

        _pages.Clear();
        _pages[RootPageId] = new DeckPage { Id = RootPageId, Title = "Home" };

        SeedDefaultDeck();
        _controller?.InvalidateAll();
        RepaintKeys(highPriority: true);

        StatusLabel.Text = "Deck layout reset.";
    }

    // -------------------------------------------------------------------- input

    private void OnKeyPressed(object? sender, DeckKeyEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            DeckLog.In("key", $"key {e.ProtocolIndex} pressed on \"{_navigator.Current?.Title ?? "?"}\"");

            // Using a sub-page keeps holding it. Without this the deck would switch
            // away mid-task simply because a notification stole focus.
            if (!_navigator.IsAtRoot) Pin();

            // Nothing here intercepts the press. Selecting something in the palette
            // used to arm the next key press, which meant a click somewhere else
            // entirely - a different page, another app - was quietly consumed as an
            // assignment. Placing is drag and drop, or the right-click menu; both say
            // where they are going before anything happens.
            DeckButton? button = _navigator.Current?.Get(e.ProtocolIndex);

            if (button?.OnPress is null)
            {
                StatusLabel.Text = $"Key {e.ProtocolIndex} is empty. Drag an app or action onto it.";
                return;
            }

            _heldSince[e.ProtocolIndex] = DateTime.UtcNow;

            // A key that repeats fires straight away and then keeps going. One that has
            // a separate hold action cannot fire yet - until the finger lifts, nobody
            // knows which of the two was meant. Everything else stays instant.
            if (button.RepeatWhileHeld)
            {
                await Fire(button);
                StartRepeating(e.ProtocolIndex, button);
                return;
            }

            if (button.OnHold is not null) return;

            await Fire(button);
        });
    }

    /// <summary>How long a key has to be held before it counts as a hold rather than a press.</summary>
    private static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(450);

    /// <summary>Gap between repeats while a key is held down.</summary>
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(140);

    private readonly Dictionary<int, DateTime> _heldSince = [];
    private readonly Dictionary<int, CancellationTokenSource> _repeating = [];

    private void OnKeyReleased(object? sender, DeckKeyEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            StopRepeating(e.ProtocolIndex);

            if (!_heldSince.Remove(e.ProtocolIndex, out DateTime pressedAt)) return;

            DeckButton? button = _navigator.Current?.Get(e.ProtocolIndex);
            if (button?.OnHold is null) return;

            TimeSpan held = DateTime.UtcNow - pressedAt;

            // The press was deferred on the way down, so one of the two has to happen
            // now - which one is the only thing the release tells us.
            await Fire(held >= HoldThreshold ? button.OnHold : button.OnPress);

            if (held >= HoldThreshold)
                DeckLog.In("key", $"key {e.ProtocolIndex} held for {held.TotalMilliseconds:0} ms");
        });
    }

    private void StartRepeating(int protocolIndex, DeckButton button)
    {
        StopRepeating(protocolIndex);

        var cts = new CancellationTokenSource();
        _repeating[protocolIndex] = cts;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                // A pause before the first repeat, so a normal tap does not fire twice.
                await Task.Delay(HoldThreshold, cts.Token);

                while (!cts.IsCancellationRequested)
                {
                    await Fire(button);
                    await Task.Delay(RepeatInterval, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void StopRepeating(int protocolIndex)
    {
        if (!_repeating.Remove(protocolIndex, out CancellationTokenSource? cts)) return;

        cts.Cancel();
        cts.Dispose();
    }

    private async Task Fire(Func<Task>? action)
    {
        if (action is null) return;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Action failed: " + ex.Message;
        }

        RepaintKeys(highPriority: true);
    }

    private Task Fire(DeckButton button) => Fire(button.OnPress);

    /// <summary>
    /// Editor-only affordance - the hardware has no right-click, so nothing in the
    /// runtime behaviour may depend on anything reachable only from here.
    /// </summary>
    /// <summary>
    /// The actions that are not about any particular application - volume, mute, a
    /// call to an MCP tool. They were cluttering the palette on app pages, where the
    /// question is what that app can do; here they are available on any key without
    /// taking up room.
    /// </summary>
    private MenuItem BuildPlaceMenu(int protocolIndex)
    {
        var place = new MenuItem { Header = "Place" };

        if (_catalog is null) return place;

        foreach (ActionDefinition action in _catalog.Actions)
        {
            // Media is here even though the palette may be hiding it. The palette asks
            // whether the app has a session *right now*, which is the wrong question
            // while building a page: a media player sitting on a loaded file publishes
            // nothing until it starts playing, so the keys could never be added before
            // they were needed.
            if (action.Category is "Navigation") continue;

            var item = new MenuItem { Header = action.Name, InputGestureText = action.Category };
            ActionDefinition chosen = action;
            item.Click += (_, _) => AssignAction(protocolIndex, chosen);
            place.Items.Add(item);
        }

        return place;
    }

    /// <summary>
    /// Teaches a page which window it belongs to, by pointing rather than describing.
    ///
    /// Working out which application a window belongs to fails in a new way for every
    /// application - and nobody using this can patch the matching code. So: switch to
    /// the application, come back here, and pick it off the menu. The watcher skips
    /// this process, so "the app I was just in" is exactly the one they mean.
    /// </summary>
    private void AddFollowItems(ContextMenu menu)
    {
        if (_navigator.Current is not { } page || _navigator.IsAtRoot) return;

        if (_matches.Get(page.Id) is { } existing)
        {
            var stop = new MenuItem { Header = $"Stop following \"{existing.Describe()}\"" };
            stop.Click += (_, _) =>
            {
                _matches.Clear(page.Id);
                StatusLabel.Text = $"\"{page.Title ?? page.Id}\" no longer follows a window of its own.";
            };
            menu.Items.Add(stop);
            return;
        }

        if (_watcher?.LastApp is not { } last) return;

        var follow = new MenuItem
        {
            Header = $"Open this page when \"{Shorten(last.Title, last.ProcessName)}\" comes to the front",
            InputGestureText = last.ProcessName
        };

        follow.Click += (_, _) =>
        {
            _matches.Set(page.Id, new MatchRule(last.AppUserModelId, last.ProcessName, last.Title));
            _settings.FollowForegroundApp = true;
            _settings.Save();
            UpdateFollowButton();

            StatusLabel.Text =
                $"\"{page.Title ?? page.Id}\" now opens when {Shorten(last.Title, last.ProcessName)} comes to the front.";
        };

        menu.Items.Add(follow);
    }

    private static string Shorten(string? title, string fallback)
    {
        if (string.IsNullOrWhiteSpace(title)) return fallback;
        return title.Length <= 40 ? title : title[..37] + "...";
    }

    private void OnCellRightClicked(object? sender, int protocolIndex)
    {
        if (DeckLayout.IsInfoCell(protocolIndex))
        {
            ShowInfoCellMenu(protocolIndex);
            return;
        }

        DeckButton? button = _navigator.Current?.Get(protocolIndex);

        // An empty key still offers somewhere to start.
        if (button is null)
        {
            var empty = new ContextMenu { Placement = PlacementMode.MousePoint };
            empty.Items.Add(BuildPlaceMenu(protocolIndex));
            empty.IsOpen = true;
            return;
        }

        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        string? identity = IdentityOf(button);

        menu.Items.Add(BuildPlaceMenu(protocolIndex));
        AddFollowItems(menu);
        menu.Items.Add(new Separator());

        var edit = new MenuItem { Header = "Edit label..." };
        edit.Click += (_, _) => EditLabel(button, protocolIndex);
        menu.Items.Add(edit);

        if (_labels.Has(identity))
        {
            var reset = new MenuItem { Header = "Use default label" };
            reset.Click += (_, _) => SetLabel(button, protocolIndex, null);
            menu.Items.Add(reset);
        }

        if (button.Tag is McpBinding binding)
        {
            var editCall = new MenuItem { Header = "Edit the MCP call..." };
            editCall.Click += (_, _) =>
            {
                if (CreateMcpButton(binding) is { } rebuilt)
                    Assign(protocolIndex, rebuilt, "an MCP call");
            };
            menu.Items.Add(editCall);
        }

        if (button.Tag is HotkeyBinding hotkeyBinding)
        {
            var editHotkey = new MenuItem { Header = "Change the hotkey..." };
            editHotkey.Click += (_, _) =>
            {
                if (CreateHotkeyButton(hotkeyBinding) is { } rebuilt)
                    Assign(protocolIndex, rebuilt, "a hotkey");
            };
            menu.Items.Add(editHotkey);
        }

        if (button.Tag is LinkBinding linkBinding)
        {
            var editLink = new MenuItem { Header = "Change the address..." };
            editLink.Click += (_, _) =>
            {
                if (CreateLinkButton(linkBinding) is { } rebuilt)
                    Assign(protocolIndex, rebuilt, "a link");
            };
            menu.Items.Add(editLink);
        }

        if (button.Tag is RunBinding runBinding)
        {
            var editRun = new MenuItem { Header = "Change what it runs..." };
            editRun.Click += (_, _) =>
            {
                if (CreateRunButton(runBinding) is { } rebuilt)
                    Assign(protocolIndex, rebuilt, "a program");
            };
            menu.Items.Add(editRun);
        }

        if (button.Tag is TextMacroBinding textBinding)
        {
            var editText = new MenuItem { Header = "Change the text..." };
            editText.Click += (_, _) =>
            {
                if (CreateTextButton(textBinding) is { } rebuilt)
                    Assign(protocolIndex, rebuilt, "a text macro");
            };
            menu.Items.Add(editText);
        }

        if (button.Tag is InstalledApp app)
        {
            menu.Items.Add(new Separator());

            var open = new MenuItem { Header = $"Open the {app.Name} page" };
            open.Click += (_, _) =>
            {
                _navigator.Push(GetOrCreateAppPage(app));
                Pin();
                StatusLabel.Text = $"Editing the {app.Name} page. Drag actions onto the keys.";
            };
            menu.Items.Add(open);
        }

        menu.Items.Add(new Separator());

        var clear = new MenuItem { Header = "Clear this key" };
        clear.Click += (_, _) => ClearKey(protocolIndex);
        menu.Items.Add(clear);

        menu.IsOpen = true;
    }

    /// <summary>
    /// Info cells are configured from the cell itself rather than from the palette.
    /// They are visible on every page, so they do not belong in a panel that swaps
    /// with the page - and right-click is already the "configure this cell" gesture.
    /// </summary>
    private void ShowInfoCellMenu(int protocolIndex)
    {
        DeckPage page = HostPageFor(protocolIndex);
        DeckButton? existing = page.Get(protocolIndex);
        var current = existing?.Tag as WidgetPlacement;

        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        var choose = new MenuItem { Header = "Show" };

        foreach (IInfoWidget widget in InfoWidgets.All)
        {
            bool selected = current?.Widget.Id == widget.Id;

            var item = new MenuItem
            {
                Header = widget.Name,
                InputGestureText = selected ? "shown" : ""
            };

            if (selected) item.Foreground = (Brush)FindResource("Accent");

            IInfoWidget chosen = widget;
            item.Click += (_, _) => AssignWidget(protocolIndex, chosen);
            choose.Items.Add(item);
        }

        menu.Items.Add(choose);

        if (current is not null)
        {
            var colours = new MenuItem { Header = "Colours..." };
            colours.Click += (_, _) => EditWidgetColours(protocolIndex, current);
            menu.Items.Add(colours);

            menu.Items.Add(new Separator());

            var clear = new MenuItem { Header = "Clear this cell" };
            clear.Click += (_, _) =>
            {
                page.Cells.Remove(protocolIndex);
                _widgetDue.Remove(protocolIndex);
                _controller?.Invalidate(protocolIndex);
                SaveProfile();
                StatusLabel.Text = $"Cleared info cell {protocolIndex}.";
            };
            menu.Items.Add(clear);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// Info cells belong to the home page unless the page you are on has claimed
    /// them, so configuring one from a sub-page edits that page's override.
    /// </summary>
    private DeckPage HostPageFor(int protocolIndex)
    {
        DeckPage? page = _navigator.Current;
        return page is not null && page.Get(protocolIndex) is not null ? page : RootPage;
    }

    private void AssignWidget(int protocolIndex, IInfoWidget widget)
    {
        DeckPage page = HostPageFor(protocolIndex);

        // Carry the colours over when swapping between widgets is not meant - a
        // different widget gets its own defaults.
        page.Set(protocolIndex, CreateWidgetButton(widget));

        _widgetDue.Remove(protocolIndex);
        _controller?.Invalidate(protocolIndex);
        SaveProfile();

        StatusLabel.Text = $"Info cell {protocolIndex}: {widget.Name}. Right-click it to change the colours.";
    }

    private void EditWidgetColours(int protocolIndex, WidgetPlacement placement)
    {
        var dialog = new WidgetColorWindow(placement.Widget, placement.Theme, _renderer) { Owner = this };

        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        placement.Theme = dialog.Result;
        _widgetDue.Remove(protocolIndex);
        _controller?.Invalidate(protocolIndex);
        SaveProfile();

        StatusLabel.Text = $"{placement.Widget.Name} recoloured.";
    }

    private void EditLabel(DeckButton button, int protocolIndex)
    {
        string? identity = IdentityOf(button);
        if (identity is null)
        {
            StatusLabel.Text = "This key has nothing to name.";
            return;
        }

        string current = _labels.Get(identity) ?? button.Visual().Label ?? "";

        var dialog = new TextInputWindow(
            "Edit label",
            "What should this key be called?",
            "Short names read best - the cell is 85 pixels wide. The name is remembered " +
            "against the app itself, so it survives moving or removing the key.",
            current,
            allowReset: _labels.Has(identity))
        { Owner = this };

        if (dialog.ShowDialog() != true) return;

        SetLabel(button, protocolIndex, dialog.Value);
    }

    private void SetLabel(DeckButton button, int protocolIndex, string? label)
    {
        if (IdentityOf(button) is not { } identity) return;

        _labels.Set(identity, label);
        _controller?.Invalidate(protocolIndex);
        RepaintKeys(highPriority: true);

        StatusLabel.Text = string.IsNullOrWhiteSpace(label)
            ? "Restored the default label."
            : $"Key {protocolIndex} is now \"{label.Trim()}\".";
    }

    private void ClearKey(int protocolIndex)
    {
        DeckPage? page = _navigator.Current;
        if (page is null) return;

        page.Cells.Remove(protocolIndex);
        _controller?.Invalidate(protocolIndex);
        RepaintKeys(highPriority: true);
        SaveProfile();

        StatusLabel.Text = $"Cleared key {protocolIndex}.";
    }

    private DeckButton CreateAppButton(InstalledApp app) => new()
    {
        Tag = app,
        Visual = () => BuildAppVisual(app),
        OnPress = () => OnAppButtonPressed(app)
    };

    private Task OnAppButtonPressed(InstalledApp app)
    {
        // A Steam game answers none of the usual process questions - Steam starts it,
        // sometimes behind a launcher - so ask Steam instead. It keeps a Running flag
        // per app, which is both cheaper and right.
        bool running = app.SteamAppId is { } gameId
            ? SteamLibrary.IsRunning(gameId)
            : _media.HasSessionFor(app.AppUserModelId, app.Name) || RunningApps.IsRunning(app);

        // Launch either way. Asking the shell to start something that is already
        // running is how Windows itself brings an application forward, and it is the
        // only way that works for one sitting in the tray.
        //
        // Reaching for the window handle instead was tried and measured against Steam,
        // which is instructive: nine processes, not one of them reporting a main
        // window, and the real window - class SDL_app, title "Steam" - present but
        // hidden rather than minimised. Forcing a window an application deliberately
        // hid is fighting it. Launching asks it, and its own single-instance handling
        // does the rest.
        try
        {
            AppsFolder.Launch(app);
            DeckLog.Out("launch", $"{app.Name}  ({app.LaunchUri})");
        }
        catch (Exception ex)
        {
            DeckLog.Note("launch", $"{app.Name} failed: {ex.Message}");
            StatusLabel.Text = $"Could not launch {app.Name}: {ex.Message}";
            return Task.CompletedTask;
        }

        if (!running)
        {
            StatusLabel.Text = $"Launched {app.Name}. Press again to open its page.";
            return Task.CompletedTask;
        }

        // Already running, so the press is about getting to its controls.
        _navigator.Push(GetOrCreateAppPage(app));
        Pin();

        StatusLabel.Text = $"{app.Name} to the front, and its page is open.";
        return Task.CompletedTask;
    }

    /// <summary>
    /// Most actions drop straight onto a key. The MCP one needs to know which server
    /// and which tool first, so it asks before anything is assigned.
    /// </summary>
    private void AssignAction(int protocolIndex, ActionDefinition action)
    {
        DeckButton? configured = action.Id switch
        {
            "mcp.call" => CreateMcpButton(existing: null),
            "input.hotkey" => CreateHotkeyButton(existing: null),
            "input.text" => CreateTextButton(existing: null),
            "input.run" => CreateRunButton(existing: null),
            "input.link" => CreateLinkButton(existing: null),
            "obs.control" => CreateObsButton(existing: null),
            "discord.control" => CreateDiscordButton(existing: null),
            _ => null
        };

        if (action.Id is "mcp.call" or "input.hotkey" or "input.text" or "input.run" or "input.link" or "obs.control" or "discord.control")
        {
            if (configured is not null) Assign(protocolIndex, configured, action.Name);
            return;
        }

        Assign(protocolIndex, action.Create(_navigator), action.Name);
    }

    private DeckButton? CreateLinkButton(LinkBinding? existing)
    {
        var dialog = new LinkWindow(existing, _renderer) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return null;

        return BuildLinkButton(dialog.Result);
    }

    private DeckButton BuildLinkButton(LinkBinding link) => new()
    {
        Tag = link,
        Visual = () =>
        {
            var visual = new CellVisual
            {
                Background = Color.FromRgb(0x11, 0x16, 0x1C),
                BackgroundGradientTo = Color.FromRgb(0x0A, 0x0C, 0x0F),
                Label = link.DisplayLabel,
                LabelColor = Colors.White,
                LabelSize = _textLab.LabelSize,
                LabelPosition = LabelPosition.Bottom,
                ReservedLabelLines = 1
            };

            if (link.FileImage is { } image)
                return visual with { Icon = image, IconScale = 0.68 };

            return link.ResolvedIcon is { } icon
                ? icon.ApplyTo(visual, Color.FromRgb(0x6F, 0xA8, 0xDC))
                : visual;
        },
        OnPress = () =>
        {
            string outcome = link.Open();

            DeckLog.Out("link", link.Target);
            StatusLabel.Text = outcome;

            return Task.CompletedTask;
        }
    };

    // ---------------------------------------------------------------------- OBS

    /// <summary>
    /// Connects to OBS if it is running with its websocket server on.
    ///
    /// Quiet about failure by design. The server is off by default, so "cannot connect"
    /// is the normal state for almost everyone and is not worth a dialog. The key's own
    /// window explains what to switch on, at the moment somebody is actually trying to
    /// use it.
    /// </summary>
    private async Task ConnectObsAsync()
    {
        if (_obs.IsConnected) return;
        if (ObsClient.ReadConfig() is not { Enabled: true } config) return;

        try
        {
            await _obs.ConnectAsync(config.Port, config.Password);
            DeckLog.Out("obs", "connected on port " + config.Port);

            _obs.Event += OnObsEvent;
            _obs.Closed += (_, _) => Dispatcher.Invoke(() =>
            {
                DeckLog.Note("obs", "connection closed");

                RepaintKeys(highPriority: false);
                ShowActionsFor(_navigator.Current);
            });

            await RefreshObsStateAsync();

            RepaintKeys(highPriority: false);
            ShowActionsFor(_navigator.Current);
        }
        catch (Exception ex)
        {
            DeckLog.Note("obs", "could not connect: " + ex.Message);
        }
    }

    /// <summary>
    /// Reads what OBS is doing now, so keys are lit correctly before anything changes.
    ///
    /// Events only report transitions. Without an initial read, a scene key stays dark
    /// until somebody switches scenes - which looks exactly like the lighting not
    /// working.
    /// </summary>
    private async Task RefreshObsStateAsync()
    {
        try
        {
            if (await _obs.CallAsync("GetSceneList") is { } scenes)
                _obsScene = scenes["currentProgramSceneName"]?.GetValue<string>();

            if (await _obs.CallAsync("GetRecordStatus") is { } record)
                _obsRecording = record["outputActive"]?.GetValue<bool>() ?? false;

            if (await _obs.CallAsync("GetStreamStatus") is { } stream)
                _obsStreaming = stream["outputActive"]?.GetValue<bool>() ?? false;
        }
        catch (Exception ex)
        {
            DeckLog.Note("obs", "state read failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Refreshes the picture on any scene key currently in view.
    ///
    /// Only what is on screen, and only every few seconds. A screenshot costs about
    /// five milliseconds, which is nothing on its own and adds up if every scene in a
    /// large collection is fetched whether or not anyone can see it.
    ///
    /// The scene that is live changes constantly - it is a video feed - so this is a
    /// glance at it, not a preview monitor. Anything faster would be spending the
    /// upload budget on something a 100 pixel cell cannot show anyway.
    /// </summary>
    private async Task RefreshObsThumbnailsAsync()
    {
        if (!_obs.IsConnected || DateTime.UtcNow < _obsThumbsDue) return;

        _obsThumbsDue = DateTime.UtcNow.AddSeconds(3);

        List<string> scenes = (_navigator.Current?.Cells.Values ?? Enumerable.Empty<DeckButton>())
            .Select(b => b.Tag as ObsBinding)
            .Where(o => o is { Action: ObsAction.SwitchScene } && o.Target.Length > 0)
            .Select(o => o!.Target)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (scenes.Count == 0) return;

        bool changed = false;

        foreach (string scene in scenes)
        {
            try
            {
                JsonNode? shot = await _obs.CallAsync("GetSourceScreenshot", new JsonObject
                {
                    ["sourceName"] = scene,
                    ["imageFormat"] = "jpg",
                    ["imageWidth"] = _settings.CellPixels,
                    ["imageHeight"] = _settings.CellPixels,
                    ["imageCompressionQuality"] = 85
                });

                if (shot?["imageData"]?.GetValue<string>() is not { } encoded) continue;

                int comma = encoded.IndexOf(',');
                if (comma < 0) continue;

                byte[] bytes = Convert.FromBase64String(encoded[(comma + 1)..]);

                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new MemoryStream(bytes);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();

                _obsThumbnails[scene] = image;
                changed = true;
            }
            catch (Exception)
            {
                // A scene can be deleted between listing it and asking for its picture.
            }
        }

        if (changed) RepaintKeys(highPriority: false);
    }

    private void OnObsEvent(object? sender, ObsEventArgs e) => Dispatcher.Invoke(() =>
    {
        switch (e.Type)
        {
            case "CurrentProgramSceneChanged":
                _obsScene = e.Data?["sceneName"]?.GetValue<string>();
                break;

            case "RecordStateChanged":
                _obsRecording = e.Data?["outputActive"]?.GetValue<bool>() ?? false;
                break;

            case "StreamStateChanged":
                _obsStreaming = e.Data?["outputActive"]?.GetValue<bool>() ?? false;
                break;

            case "InputMuteStateChanged":
                string? input = e.Data?["inputName"]?.GetValue<string>();
                if (input is null) return;

                _obsMuted[input] = e.Data?["inputMuted"]?.GetValue<bool>() ?? false;
                break;

            default:
                return;
        }

        RepaintKeys(highPriority: true);
    });

    /// <summary>Whether the thing this key controls is currently on.</summary>
    private bool IsObsActive(ObsBinding obs) => obs.Action switch
    {
        ObsAction.SwitchScene => string.Equals(_obsScene, obs.Target, StringComparison.Ordinal),
        ObsAction.ToggleRecord => _obsRecording,
        ObsAction.ToggleStream => _obsStreaming,
        ObsAction.ToggleMute => _obsMuted.TryGetValue(obs.Target, out bool muted) && muted,
        _ => false
    };

    // ------------------------------------------------------------------ Discord

    /// <summary>
    /// Connects to Discord if it is running.
    ///
    /// Quiet about failure, like OBS. Not running is the ordinary case and not worth a
    /// dialog. The first successful connection may put an authorisation prompt in the
    /// Discord window, which is Discord asking rather than us, and only ever once.
    /// </summary>
    private async Task ConnectDiscordAsync()
    {
        if (_discord.IsConnected) return;

        try
        {
            await _discord.ConnectAsync();

            _discord.Event += OnDiscordEvent;
            _discord.Closed += (_, _) => Dispatcher.Invoke(() =>
            {
                DeckLog.Note("discord", "connection closed");

                RepaintKeys(highPriority: false);
                ShowActionsFor(_navigator.Current);
            });

            await _discord.SubscribeAsync("VOICE_SETTINGS_UPDATE");
            await RefreshDiscordStateAsync();

            DeckLog.Out("discord", "connected");

            RepaintKeys(highPriority: false);
            ShowActionsFor(_navigator.Current);
        }
        catch (Exception ex)
        {
            DeckLog.Note("discord", "could not connect: " + ex.Message);
        }
    }

    /// <summary>
    /// Reads the current voice state, so keys are lit before anything changes.
    ///
    /// Events only report transitions, so without this a mute key stays dark until you
    /// toggle it once, which looks exactly like the lighting being broken.
    /// </summary>
    private async Task RefreshDiscordStateAsync()
    {
        try
        {
            if (await _discord.CallAsync("GET_VOICE_SETTINGS") is { } voice)
            {
                _discordMuted = voice["mute"]?.GetValue<bool>() ?? false;
                _discordDeafened = voice["deaf"]?.GetValue<bool>() ?? false;
            }
        }
        catch (Exception ex)
        {
            DeckLog.Note("discord", "state read failed: " + ex.Message);
        }
    }

    private void OnDiscordEvent(object? sender, DiscordEventArgs e) => Dispatcher.Invoke(() =>
    {
        if (e.Name != "VOICE_SETTINGS_UPDATE") return;

        _discordMuted = e.Data?["mute"]?.GetValue<bool>() ?? _discordMuted;
        _discordDeafened = e.Data?["deaf"]?.GetValue<bool>() ?? _discordDeafened;

        RepaintKeys(highPriority: true);
    });

    private bool IsDiscordActive(DiscordBinding discord) => discord.Action switch
    {
        DiscordAction.ToggleMute => _discordMuted,
        DiscordAction.ToggleDeafen => _discordDeafened,
        _ => false
    };

    private DeckButton? CreateDiscordButton(DiscordBinding? existing)
    {
        var dialog = new DiscordWindow(existing) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return null;

        return BuildDiscordButton(dialog.Result);
    }

    private DeckButton BuildDiscordButton(DiscordBinding discord) => new()
    {
        Tag = discord,
        Visual = () =>
        {
            bool active = _discord.IsConnected && IsDiscordActive(discord);

            // Muted is the state worth seeing across a room, so it is the loud one.
            var visual = new CellVisual
            {
                Background = active ? Color.FromRgb(0x3A, 0x14, 0x18) : Color.FromRgb(0x16, 0x18, 0x24),
                BackgroundGradientTo = active ? Color.FromRgb(0x20, 0x0B, 0x0E) : Color.FromRgb(0x0B, 0x0C, 0x12),
                Label = discord.DisplayLabel,
                LabelColor = _discord.IsConnected ? Colors.White : Color.FromRgb(0x80, 0x80, 0x8A),
                LabelSize = _textLab.LabelSize,
                LabelPosition = LabelPosition.Bottom,
                ReservedLabelLines = 1
            };

            if (discord.FileImage is { } image)
                return visual with { Icon = image, IconScale = 0.68 };

            Color tint = !_discord.IsConnected ? Color.FromRgb(0x50, 0x52, 0x60)
                       : active ? Color.FromRgb(0xFF, 0x6B, 0x7A)
                       : Color.FromRgb(0x8E, 0x9B, 0xF0);

            return discord.ResolvedIcon is { } icon ? icon.ApplyTo(visual, tint) : visual;
        },
        OnPress = async () =>
        {
            if (!_discord.IsConnected)
            {
                await ConnectDiscordAsync();

                if (!_discord.IsConnected)
                {
                    StatusLabel.Text = "Discord is not answering. Start it and press again.";
                    return;
                }
            }

            (string command, JsonObject? args) = discord.Request(_discordMuted, _discordDeafened);

            try
            {
                await _discord.CallAsync(command, args);
                DeckLog.Out("discord", command + (args is null ? "" : "  " + args.ToJsonString()));
                StatusLabel.Text = discord.Describe();
            }
            catch (Exception ex)
            {
                DeckLog.Note("discord", command + " failed: " + ex.Message);
                StatusLabel.Text = "Discord did not accept that: " + ex.Message;
            }
        }
    };

    /// <summary>
    /// Turns whatever an agent proposed into a key.
    ///
    /// Deliberately narrow. A proposal may only contain things the user can see and
    /// judge on the deck before accepting, so this handles exactly those two and
    /// nothing else.
    /// </summary>
    private DeckButton BuildProposedButton(object binding) => binding switch
    {
        ObsBinding obs => BuildObsButton(obs),
        DiscordBinding discord => BuildDiscordButton(discord),
        HotkeyBinding hotkey => BuildHotkeyButton(hotkey),
        _ => throw new ArgumentOutOfRangeException(nameof(binding), binding, "Not a proposable binding.")
    };

    private DeckButton? CreateObsButton(ObsBinding? existing)
    {
        var dialog = new ObsWindow(existing, _obs) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return null;

        return BuildObsButton(dialog.Result);
    }

    private DeckButton BuildObsButton(ObsBinding obs) => new()
    {
        Tag = obs,
        Visual = () =>
        {
            bool active = _obs.IsConnected && IsObsActive(obs);

            // Lit means live. That is the whole reason for going through the websocket
            // rather than sending a hotkey, so it should be unmistakable.
            var visual = new CellVisual
            {
                Background = active ? Color.FromRgb(0x3A, 0x12, 0x12) : Color.FromRgb(0x1A, 0x0E, 0x0E),
                BackgroundGradientTo = active ? Color.FromRgb(0x20, 0x0A, 0x0A) : Color.FromRgb(0x0D, 0x08, 0x08),
                Label = obs.DisplayLabel,
                LabelColor = _obs.IsConnected ? Colors.White : Color.FromRgb(0x80, 0x80, 0x8A),
                LabelSize = _textLab.LabelSize,
                LabelPosition = LabelPosition.Bottom,
                ReservedLabelLines = 1
            };

            // A scene key shows the scene. It is the one case where a picture says
            // more than any icon could, and OBS is already rendering it.
            if (obs.Action == ObsAction.SwitchScene &&
                _obsThumbnails.TryGetValue(obs.Target, out BitmapSource? thumbnail))
            {
                return visual with
                {
                    Icon = thumbnail,
                    IconScale = 1.0,
                    LabelPosition = LabelPosition.Bottom,
                    ReservedLabelLines = 1
                };
            }

            if (obs.FileImage is { } image)
                return visual with { Icon = image, IconScale = 0.68 };

            Color tint = !_obs.IsConnected ? Color.FromRgb(0x60, 0x50, 0x50)
                       : active ? Color.FromRgb(0xFF, 0x8A, 0x8A)
                       : Color.FromRgb(0xE0, 0x6C, 0x6C);

            return obs.ResolvedIcon is { } icon ? icon.ApplyTo(visual, tint) : visual;
        },
        OnPress = async () =>
        {
            if (!_obs.IsConnected)
            {
                // It may simply have been started since dotStream was.
                await ConnectObsAsync();

                if (!_obs.IsConnected)
                {
                    StatusLabel.Text = "OBS is not answering. Start it, and turn on Tools > WebSocket Server Settings.";
                    return;
                }
            }

            (string type, System.Text.Json.Nodes.JsonObject? data) = obs.Request();

            try
            {
                await _obs.CallAsync(type, data);
                DeckLog.Out("obs", type + (data is null ? "" : "  " + data.ToJsonString()));
                StatusLabel.Text = obs.Describe();
            }
            catch (Exception ex)
            {
                DeckLog.Note("obs", type + " failed: " + ex.Message);
                StatusLabel.Text = "OBS did not accept that: " + ex.Message;
            }
        }
    };

    private DeckButton? CreateRunButton(RunBinding? existing)
    {
        var dialog = new RunWindow(existing, _renderer) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return null;

        return BuildRunButton(dialog.Result);
    }

    private CellVisual RunVisual(RunBinding run)
    {
        var visual = new CellVisual
        {
            Background = Color.FromRgb(0x18, 0x11, 0x1C),
            BackgroundGradientTo = Color.FromRgb(0x0C, 0x0A, 0x0E),
            Label = run.DisplayLabel,
            LabelColor = Colors.White,
            LabelSize = _textLab.LabelSize,
            LabelPosition = LabelPosition.Bottom,
            ReservedLabelLines = 1
        };

        if (run.FileImage is { } image)
            return visual with { Icon = image, IconScale = 0.68 };

        return run.ResolvedIcon is { } icon
            ? icon.ApplyTo(visual, Color.FromRgb(0xC9, 0x8B, 0xE2))
            : visual with
            {
                BigText = "Run",
                BigTextColor = Color.FromRgb(0xC9, 0x8B, 0xE2),
                BigTextScale = 0.38
            };
    }

    private DeckButton BuildRunButton(RunBinding run) => new()
    {
        Tag = run,
        Visual = () => RunVisual(run),
        OnPress = () =>
        {
            // No focus handover here. Unlike a hotkey or a text macro, this does not
            // go through whatever window happens to be in front - which is the whole
            // reason the action exists.
            string outcome = run.Start();

            DeckLog.Out("run", $"{run.Path} {run.Arguments}".TrimEnd());
            StatusLabel.Text = outcome;

            return Task.CompletedTask;
        }
    };

    private DeckButton? CreateTextButton(TextMacroBinding? existing)
    {
        var dialog = new TextMacroWindow(existing, _renderer) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return null;

        return BuildTextButton(dialog.Result);
    }

    private CellVisual TextVisual(TextMacroBinding macro)
    {
        var visual = new CellVisual
        {
            Background = Color.FromRgb(0x11, 0x18, 0x14),
            BackgroundGradientTo = Color.FromRgb(0x0A, 0x0D, 0x0B),
            Label = macro.DisplayLabel,
            LabelColor = Colors.White,
            LabelSize = _textLab.LabelSize,
            LabelPosition = LabelPosition.Bottom,
            ReservedLabelLines = 1
        };

        if (macro.FileImage is { } image)
            return visual with { Icon = image, IconScale = 0.68 };

        return macro.ResolvedIcon is { } icon
            ? icon.ApplyTo(visual, Color.FromRgb(0x8B, 0xE2, 0xA8))
            : visual with
            {
                BigText = macro.Preview,
                BigTextColor = Color.FromRgb(0x8B, 0xE2, 0xA8),
                BigTextScale = 0.34
            };
    }

    private DeckButton BuildTextButton(TextMacroBinding macro) => new()
    {
        Tag = macro,
        Visual = () => TextVisual(macro),
        OnPress = async () =>
        {
            // Same reason as a hotkey: in the simulator the click has just taken focus,
            // and without handing it back the text is typed into dotStream itself.
            if (_watcher?.LastForegroundWindow is { } target && target != IntPtr.Zero)
                ForegroundWatcher.SetForegroundWindow(target);

            await TextMacro.SendAsync(macro.Text, macro.PressEnter);

            DeckLog.Out("text", $"{macro.Text.Length} characters  ({macro.DisplayLabel})");
            StatusLabel.Text = $"Typed {macro.DisplayLabel}.";
        }
    };

    private DeckButton? CreateHotkeyButton(HotkeyBinding? existing)
    {
        var dialog = new HotkeyWindow(existing, _renderer) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return null;

        return BuildHotkeyButton(dialog.Result);
    }

    /// <summary>
    /// A file icon wins if one was chosen, then a named or guessed glyph, then the
    /// combination itself. A key showing "Ctrl+Shift+V" is honest but it is not a
    /// button.
    /// </summary>
    private CellVisual HotkeyVisual(HotkeyBinding binding)
    {
        var visual = new CellVisual
        {
            Background = Color.FromRgb(0x16, 0x17, 0x1B),
            BackgroundGradientTo = Color.FromRgb(0x0B, 0x0C, 0x0F),
            Label = binding.DisplayLabel,
            LabelColor = Colors.White,
            LabelSize = _textLab.LabelSize,
            LabelPosition = LabelPosition.Bottom,
            ReservedLabelLines = 1
        };

        if (binding.FileImage is { } image)
            return visual with { Icon = image, IconScale = 0.68 };

        return binding.ResolvedIcon is { } icon
            ? icon.ApplyTo(visual, Colors.White)
            : visual with
            {
                BigText = binding.Combination,
                BigTextColor = Color.FromRgb(0xFF, 0xC9, 0x6B),
                BigTextScale = 0.45
            };
    }

    private DeckButton BuildHotkeyButton(HotkeyBinding binding) => new()
    {
        Tag = binding,
        Visual = () => HotkeyVisual(binding),
        OnPress = async () =>
        {
            IReadOnlyList<SequenceStep> steps = binding.Steps;

            if (steps.Count == 0)
            {
                StatusLabel.Text = $"\"{binding.Combination}\" is not a key combination I understand.";
                return;
            }

            // Hand focus back to whatever the user was working in. On real hardware a
            // key press never takes focus, so this only matters in the simulator - but
            // without it the keystroke lands in dotStream itself.
            if (_watcher?.LastForegroundWindow is { } target && target != IntPtr.Zero)
                ForegroundWatcher.SetForegroundWindow(target);

            await KeySequence.SendAsync(steps);

            DeckLog.Out("hotkey", $"{binding.Combination}  ({binding.DisplayLabel})");
            StatusLabel.Text = steps.Count > 1
                ? $"Sent {binding.Combination} - {steps.Count} steps."
                : $"Sent {binding.Combination}.";
        }
    };

    /// <summary>
    /// Builds a key that calls someone else's MCP tool. Returns null when the user
    /// cancels the configuration window, so the key is left alone.
    /// </summary>
    private DeckButton? CreateMcpButton(McpBinding? existing)
    {
        var dialog = new McpBindingWindow(_mcpClient, existing) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return null;

        return BuildMcpButton(dialog.Result);
    }

    private DeckButton BuildMcpButton(McpBinding binding) => new()
    {
        Tag = binding,
        Visual = () => new CellVisual
        {
            Background = Color.FromRgb(0x0B, 0x14, 0x18),
            BackgroundGradientTo = Color.FromRgb(0x06, 0x0C, 0x10),
            Glyph = Glyphs.Plus,
            GlyphColor = WidgetTheme.StreamCyan,
            IconScale = 0.5,
            Label = binding.DisplayLabel,
            LabelSize = _textLab.LabelSize,
            LabelPosition = LabelPosition.Bottom,
            ReservedLabelLines = 1
        },
        OnPress = async () =>
        {
            StatusLabel.Text = $"Calling {binding.Tool}...";
            DeckLog.Out("mcp:call", $"{binding.Tool} -> {binding.Url}  {binding.Arguments}");

            try
            {
                McpCallResult result = await _mcpClient.CallAsync(binding.Url, binding.Tool, binding.Arguments);
                DeckLog.In("mcp:reply", (result.IsError ? "error: " : "") + result.Text);

                // Tool output is often several lines; the status bar is one. Flatten
                // rather than let it resize the whole window.
                string summary = string.Join("  ·  ",
                    result.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

                if (summary.Length > 170) summary = summary[..170] + "...";

                StatusLabel.Text = (result.IsError ? $"{binding.Tool} reported an error: " : $"{binding.Tool}: ") + summary;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"{binding.Tool} failed: {ex.Message}";
            }
        }
    };

    private void Assign(int protocolIndex, DeckButton button, string what)
    {
        DeckPage? page = _navigator.Current;
        if (page is null) return;

        page.Set(protocolIndex, button);

        _controller?.Invalidate(protocolIndex);
        RepaintKeys(highPriority: true);
        SaveProfile();

        StatusLabel.Text = $"Assigned {what} to key {protocolIndex} on \"{page.Title ?? page.Id}\".";
    }

    private void OnBack(object sender, RoutedEventArgs e) => _navigator.Pop();

    // ------------------------------------------------------------ agent surface

    /// <summary>
    /// Lays the options out on keys and blocks until one is pressed.
    ///
    /// The deck is pinned for the duration so automatic switching cannot pull the
    /// question away while the user is looking at it, and the page is popped whatever
    /// the outcome - answered, timed out or cancelled.
    /// </summary>
    public async Task<AskResult> AskAsync(string question, IReadOnlyList<string> options, TimeSpan timeout)
    {
        if (!_agent.TryBeginAsk(out Task<AskResult> answer))
            return await answer;

        await Dispatcher.InvokeAsync(() =>
        {
            var page = new DeckPage { Id = AgentState.AskPageId, Title = question };

            for (int i = 0; i < options.Count; i++)
            {
                string choice = options[i];
                int index = i;

                page.SetAt(i / 5, i % 5, new DeckButton
                {
                    Tag = "agent.option",
                    Visual = () => AgentState.OptionVisual(choice, index),
                    OnPress = () =>
                    {
                        _agent.Complete(new AskResult(true, index, choice, "answered"));
                        return Task.CompletedTask;
                    }
                });
            }

            _pages[AgentState.AskPageId] = page;
            _navigator.SwitchTo(page);
            Pin();

            StatusLabel.Text = "An agent is asking: " + question;
        });

        Task finished = await Task.WhenAny(answer, Task.Delay(timeout));

        if (finished != answer)
            _agent.Complete(new AskResult(false, -1, null,
                $"Nobody answered within {timeout.TotalSeconds:0} seconds."));

        AskResult result = await answer;

        await Dispatcher.InvokeAsync(() =>
        {
            _pages.Remove(AgentState.AskPageId);
            if (_navigator.Current?.Id == AgentState.AskPageId) _navigator.PopToRoot();

            StatusLabel.Text = result.Answered
                ? $"Answered \"{result.Choice}\" to the agent."
                : "The agent's question expired.";
        });

        return result;
    }

    /// <summary>
    /// Finds a page by the name a person would use for it - an app's title, or the
    /// name of a page they made. Id last, so "Excel" works without anyone having to
    /// know it is stored as "app:Microsoft.Office.EXCEL.EXE.15".
    /// </summary>
    private DeckPage? FindPage(string name)
    {
        string wanted = name.Trim();

        return _pages.Values.FirstOrDefault(p =>
                   string.Equals(p.Title, wanted, StringComparison.OrdinalIgnoreCase))
               ?? _pages.Values.FirstOrDefault(p =>
                   string.Equals(p.Id, wanted, StringComparison.OrdinalIgnoreCase))
               ?? _pages.Values.FirstOrDefault(p =>
                   p.Title?.Contains(wanted, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// The next key nothing is using. Occupied cells on the target page are skipped,
    /// so adding to a page someone has arranged does not quietly overwrite it, and the
    /// two decision keys are left alone or the proposal would bury its own Accept.
    /// </summary>
    private static int NextFreeKey(DeckPage? target, IReadOnlySet<int> taken)
    {
        // Three cells carry the decision while a proposal is on screen.
        int[] decisions =
        [
            DeckLayout.ToProtocolIndex(2, 2),
            DeckLayout.ToProtocolIndex(2, 3),
            DeckLayout.ToProtocolIndex(2, 4)
        ];

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                int index = DeckLayout.ToProtocolIndex(row, column);

                if (decisions.Contains(index)) continue;
                if (taken.Contains(index)) continue;
                if (target?.Get(index) is not null) continue;

                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Puts a single proposed key in front of the user, filled in, and lets them save
    /// it or throw it away. Cancel is a rejection - the agent is told so.
    /// </summary>
    /// <summary>
    /// An application by the name a person would use, or by its identifier.
    ///
    /// The identifier is accepted because the display name is whatever the shell
    /// decided to call it, which is not always what the key says on the deck - the
    /// Apple TV app answers to "AppleInc.AppleTVWin_nzyj5cx40ttqa!App" and to
    /// something else entirely in the list.
    /// </summary>
    private InstalledApp? FindApp(string name)
    {
        string wanted = name.Trim();

        return _apps.FirstOrDefault(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase))
               ?? _apps.FirstOrDefault(a => string.Equals(a.AppUserModelId, wanted, StringComparison.OrdinalIgnoreCase))
               ?? _apps.FirstOrDefault(a => a.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
               ?? _apps.FirstOrDefault(a => a.AppUserModelId.Contains(wanted, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<AskResult> ReviewSingleKeyAsync(
        string pageName, (int Index, HotkeyBinding Binding) proposed, DeckPage? target, InstalledApp? targetApp = null)
    {
        var result = new AskResult(true, 1, "Reject", "The user cancelled the dialog.");

        await Dispatcher.InvokeAsync(() =>
        {
            // The window may be in the tray, and a modal dialog nobody can see is
            // indistinguishable from the app having hung.
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();

            var dialog = new HotkeyWindow(proposed.Binding, _renderer)
            {
                Owner = this,
                Title = $"Proposed by an agent  -  {pageName}"
            };

            dialog.AllowPostpone();

            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                if (dialog.Postponed)
                {
                    result = new AskResult(true, 2, "Later", "postponed");
                    StatusLabel.Text = "Put off for now - the agent was asked to come back to it.";
                    DeckLog.Note("mcp:propose", $"{pageName} postponed");
                    return;
                }

                StatusLabel.Text = "Proposal cancelled - nothing was saved.";
                DeckLog.Note("mcp:propose", $"{pageName} cancelled");
                return;
            }

            DeckPage page = target
                ?? (targetApp is not null ? GetOrCreateAppPage(targetApp) : BuildProposedPage(pageName));

            _pages[page.Id] = page;

            page.Set(proposed.Index, BuildHotkeyButton(dialog.Result));
            LibraryFor(page.Id).Add(dialog.Result);

            SaveProfile();

            _navigator.SwitchTo(page);
            _controller?.Invalidate(proposed.Index);
            RepaintKeys(highPriority: true);

            result = new AskResult(true, 0, "Accept", "saved");

            DeckLog.Note("mcp:propose", $"{dialog.Result.DisplayLabel} saved to key {proposed.Index}");
            StatusLabel.Text =
                $"Saved {dialog.Result.DisplayLabel} to key {proposed.Index} on \"{page.Title ?? page.Id}\".";
        });

        return result;
    }

    private DeckPage BuildProposedPage(string pageName)
    {
        var page = new DeckPage { Id = "page:" + pageName, Title = pageName };

        if (_catalog?.ById("nav.back") is { } back)
            page.SetAt(2, 0, back.Create(_navigator));

        return page;
    }

    /// <summary>
    /// Shows the proposed page, then asks on the deck whether to keep it.
    ///
    /// The user sees the actual keys, laid out exactly as they would be, before
    /// deciding. A list of shortcuts in a chat window is not the same thing as seeing
    /// them on the hardware.
    /// </summary>
    public async Task<AskResult> ProposePageAsync(
        string pageName, IReadOnlyList<ProposedKey> keys, TimeSpan timeout, string? targetPage = null)
    {
        DeckPage? target = targetPage is null ? null : FindPage(targetPage);

        // No page by that name, but an app by that name - so the page is one the user
        // simply has not opened yet. Resolved to the app rather than created here:
        // nothing should exist on disk because an agent asked a question.
        InstalledApp? targetApp = target is null && targetPage is not null ? FindApp(targetPage) : null;

        if (targetPage is not null && target is null && targetApp is null)
            return new AskResult(false, -1, null,
                $"There is no page or installed application called \"{targetPage}\".");

        // Object rather than HotkeyBinding: a proposal may now carry OBS actions too,
        // and the two are stored, drawn and merged the same way once built.
        var built = new List<(int Index, object Binding)>();
        var taken = new HashSet<int>();

        // Counted separately from keys that could not be built, because the two need
        // different answers.
        int full = 0;

        foreach (ProposedKey key in keys)
        {
            object? binding = null;

            if (key.Discord is { } dc && Enum.TryParse(dc.Action, ignoreCase: true, out DiscordAction discordAction))
            {
                binding = new DiscordBinding(discordAction, key.Label, key.Icon);
            }
            else if (key.Obs is { } obs && Enum.TryParse(obs.Action, ignoreCase: true, out ObsAction action))
            {
                binding = new ObsBinding(action, obs.Target, key.Label, key.Icon);
            }
            else if (KeySequence.Parse(key.Hotkey).Count > 0)
            {
                // A sequence is as valid as a chord here. Validating with Hotkey.Parse
                // rejected "Alt, H, M, C" outright, which is the one thing a ribbon
                // command can be written as.
                binding = new HotkeyBinding(key.Hotkey, key.Label, key.Icon);
            }

            if (binding is null) continue;

            int index = key.Index is { } wanted && DeckLayout.IsValid(wanted) && !DeckLayout.IsInfoCell(wanted)
                ? wanted
                : NextFreeKey(target, taken);

            if (index < 0)
            {
                full++;
                continue;
            }

            taken.Add(index);
            built.Add((index, binding));
        }

        // Two very different failures used to give the same answer. A page with no room
        // left reported "none of the keys could be understood", which sent whoever read
        // it looking at their key definitions - and since the page stayed full, every
        // attempt after it failed the same way and looked like corrupted state.
        if (built.Count == 0 && full > 0)
        {
            string where = target?.Title ?? targetApp?.Name ?? "That page";

            return new AskResult(false, -1, null,
                $"{where} has no free keys left. Twelve fit alongside Back and the accept "
                + "and reject keys, so clear one first or propose a new page instead.");
        }

        if (built.Count == 0)
            return new AskResult(false, -1, null, "None of the keys could be understood.");

        if (full > 0)
            DeckLog.Note("mcp:propose", $"{full} key(s) dropped: no room left on the page");

        // One key is an edit, not a page. Show it in the hotkey window with everything
        // the agent chose already filled in, so the combination, label and icon can be
        // changed before they are kept. Accept or reject on the deck is the right shape
        // for thirteen keys and the wrong one for a single suggestion, where it is take
        // it or leave it and no way to fix a name.
        if (built.Count == 1 && built[0].Binding is HotkeyBinding single)
            return await ReviewSingleKeyAsync(pageName, (built[0].Index, single), target, targetApp);

        if (!_agent.TryBeginAsk(out Task<AskResult> pending)) return await pending;

        await Dispatcher.InvokeAsync(() =>
        {
            // The proposed keys and the decision live on the same page. Showing the
            // proposal and then replacing it with a yes/no would mean deciding about
            // something you can no longer see.
            var preview = new DeckPage { Id = "agent:propose", Title = pageName + "  (proposed)" };

            foreach ((int index, object binding) in built)
                preview.Set(index, BuildProposedButton(binding));

            preview.SetAt(2, 2, DecisionKey("Later", Color.FromRgb(0x3A, 0x30, 0x14),
                Color.FromRgb(0xFF, 0xC9, 0x6B), () => _agent.Complete(new AskResult(true, 2, "Later", "postponed"))));

            preview.SetAt(2, 3, DecisionKey("Accept", Color.FromRgb(0x1B, 0x3A, 0x22),
                Color.FromRgb(0x6D, 0xE2, 0x8B), () => _agent.Complete(new AskResult(true, 0, "Accept", "answered"))));

            preview.SetAt(2, 4, DecisionKey("Reject", Color.FromRgb(0x3A, 0x1B, 0x1B),
                Color.FromRgb(0xFF, 0x7A, 0x7A), () => _agent.Complete(new AskResult(true, 1, "Reject", "answered"))));

            _pages["agent:propose"] = preview;
            _navigator.SwitchTo(preview);
            Pin();

            DeckLog.In("mcp:propose", $"{pageName}: {built.Count} keys awaiting approval");
            StatusLabel.Text = $"An agent proposes a \"{pageName}\" page with {built.Count} keys. Accept or reject on the deck.";
        });

        Task finished = await Task.WhenAny(pending, Task.Delay(timeout));

        if (finished != pending)
            _agent.Complete(new AskResult(false, -1, null,
                $"Nobody answered within {timeout.TotalSeconds:0} seconds."));

        AskResult answer = await pending;

        await Dispatcher.InvokeAsync(() =>
        {
            _pages.Remove("agent:propose");

            if (answer is { Answered: true, Index: 0 })
            {
                DeckPage page;
                DeckPage? destination = target ?? (targetApp is not null ? GetOrCreateAppPage(targetApp) : null);

                if (destination is not null)
                {
                    // Merging, not replacing. Everything already on the page stays
                    // where the user put it.
                    page = destination;

                    foreach ((int index, object binding) in built)
                    {
                        page.Set(index, BuildProposedButton(binding));

                        // Only hotkeys go in the page library. It is a list of things
                        // this application can do, and an OBS action is not one of them.
                        if (binding is HotkeyBinding hotkey) LibraryFor(page.Id).Add(hotkey);
                    }
                }
                else
                {
                    string id = "page:" + pageName;
                    page = new DeckPage { Id = id, Title = pageName };

                    if (_catalog?.ById("nav.back") is { } back)
                        page.SetAt(2, 0, back.Create(_navigator));

                    foreach ((int index, object binding) in built)
                    {
                        page.Set(index, BuildProposedButton(binding));

                        if (binding is HotkeyBinding hotkey) LibraryFor(id).Add(hotkey);
                    }

                    _pages[id] = page;
                }

                SaveProfile();

                _navigator.SwitchTo(page);

                DeckLog.Note("mcp:propose", $"{pageName} accepted and saved");
                StatusLabel.Text = destination is not null
                    ? $"Added {built.Count} key(s) to \"{page.Title ?? page.Id}\"."
                    : $"Saved the \"{pageName}\" page.";
            }
            else
            {
                _navigator.PopToRoot();

                bool later = answer is { Answered: true, Index: 2 };

                DeckLog.Note("mcp:propose", $"{pageName} {(later ? "postponed" : "rejected")}");
                StatusLabel.Text = later
                    ? "Put off for now - the agent was asked to come back to it."
                    : "Proposal rejected - nothing was saved.";
            }
        });

        return answer;
    }

    private static DeckButton DecisionKey(string label, Color background, Color ink, Action onPress) => new()
    {
        Tag = "agent.option",
        Visual = () => new CellVisual
        {
            Background = background,
            Label = label,
            LabelColor = ink,
            LabelSize = 12,
            LabelPosition = LabelPosition.Bottom,
            ReservedLabelLines = 1,
            BigText = label == "Accept" ? "✓" : "✕",
            BigTextColor = ink,
            BigTextScale = 1.1
        },
        OnPress = () =>
        {
            onPress();
            return Task.CompletedTask;
        }
    };

    public void Notify(string text, int? cell, Color? colour)
    {
        int index = cell is { } requested && DeckLayout.IsInfoCell(requested)
            ? requested
            : DeckLayout.LastInfoCell;

        Dispatcher.Invoke(() =>
        {
            _agent.NotificationsUntil[index] = DateTime.UtcNow + TimeSpan.FromSeconds(10);

            _controller?.Invalidate(index);
            _controller?.Update(index,
                _renderer.Render(AgentState.NotificationVisual(text, colour ?? WidgetTheme.StreamCyan)),
                highPriority: true);

            StatusLabel.Text = $"Agent message on cell {index}: {text}";
        });
    }

    public void SetKey(int index, string label, Color? colour)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_pages.TryGetValue(AgentState.AgentPageId, out DeckPage? page))
            {
                page = new DeckPage { Id = AgentState.AgentPageId, Title = "Agent" };

                if (_catalog?.ById("nav.back") is { } back)
                    page.SetAt(2, 0, back.Create(_navigator));

                _pages[AgentState.AgentPageId] = page;
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                page.Cells.Remove(index);
            }
            else
            {
                string text = label;
                Color ink = colour ?? Colors.White;

                // No OnPress. An agent may draw on the deck; it may not decide what a
                // key does when pressed.
                page.Set(index, new DeckButton
                {
                    Tag = "agent.key",
                    Visual = () => AgentState.AgentKeyVisual(text, ink)
                });
            }

            if (_navigator.Current?.Id != AgentState.AgentPageId && !_agent.IsAsking)
            {
                _navigator.SwitchTo(page);
            }

            _controller?.Invalidate(index);
            RepaintKeys(highPriority: true);
        });
    }

    public DeckStatus Status() => Dispatcher.Invoke(() => new DeckStatus(
        _transport?.Name ?? "none",
        _navigator.Current?.Title ?? _navigator.Current?.Id ?? "none",
        _navigator.Depth,
        _settings.FollowForegroundApp,
        _media.Snapshot?.DisplayLine));

    // ------------------------------------------------------- following the focus

    /// <summary>
    /// Opens an app's page when you switch to that app.
    ///
    /// Two rules keep it from being annoying. It only ever opens pages that already
    /// exist - it will not conjure an empty page for every window you touch. And it
    /// never overrides you: the moment you navigate by hand the deck is pinned, and
    /// automatic switching waits until you go Back or stop pressing keys.
    /// </summary>
    /// <summary>
    /// Debounced, because one alt-tab is several foreground events. Only the window
    /// still in front when things settle is worth reacting to - acting on each event
    /// in turn makes the deck visibly flick between pages.
    /// </summary>
    private void OnForegroundChanged(object? sender, ForegroundApp foreground)
    {
        // Updated even when following is switched off, and before the debounce: the
        // inspector's whole job is to show what was actually seen.
        _inspector?.Refresh();

        _pendingForeground = foreground;
        _focusSettled.Stop();
        _focusSettled.Start();
    }

    private void OnFocusSettled(object? sender, EventArgs e)
    {
        _focusSettled.Stop();

        if (_pendingForeground is not { } foreground) return;
        _pendingForeground = null;

        ApplyForeground(foreground);
    }

    private void ApplyForeground(ForegroundApp foreground)
    {
        if (!_settings.FollowForegroundApp) return;

        // A question on the deck is not a page to be switched away from. Pinning has a
        // duration, and switching to the application an agent is asking about outlasted
        // it - the proposal stayed open, waiting, while the page showing its Accept and
        // Reject keys had been pushed off the stack. There was then no way to answer.
        if (_agent.IsAsking) return;

        if (DateTime.UtcNow < _pinnedUntil) return;

        // A rule the user taught wins over anything worked out automatically. They
        // pointed at the window; there is nothing left to deduce.
        InstalledApp? app = MatchApp(foreground);
        string? pageId = _matches.PageFor(foreground)
                         ?? (app is null ? null : "app:" + app.AppUserModelId);

        if (pageId is not null && _pages.TryGetValue(pageId, out DeckPage? page))
        {
            if (_navigator.Current?.Id == pageId) return;

            _navigator.SwitchTo(page);
            _autoPushedPageId = pageId;

            // The name can come from the matched application, from the page, or from
            // the window itself - a taught rule may have matched something that is not
            // an installed application at all.
            string what = app?.Name ?? page.Title ?? foreground.ProcessName;

            DeckLog.Note("focus", $"{what} came to the front - opened its page");
            StatusLabel.Text = $"{what} came to the front - opened its page.";
            return;
        }

        // Focus moved somewhere with no page. Only tidy away a page this feature
        // opened; anything the user navigated to is theirs.
        if (_autoPushedPageId is not null && _navigator.Current?.Id == _autoPushedPageId)
            _navigator.PopToRoot();
    }

    /// <summary>
    /// Which installed application the window in front belongs to.
    ///
    /// A packaged app answers this exactly, so it is asked first: process names get
    /// nowhere near it, because Media Player runs as "Microsoft.Media.Player" while
    /// being installed as "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" and
    /// displayed, here, as "Mediespiller". Desktop programs have no such identity and
    /// fall back to matching on the name.
    /// </summary>
    private InstalledApp? MatchApp(ForegroundApp foreground)
    {
        if (foreground.AppUserModelId is { Length: > 0 } id &&
            _apps.FirstOrDefault(a =>
                string.Equals(a.AppUserModelId, id, StringComparison.OrdinalIgnoreCase)) is { } exact)
            return exact;

        return _apps.FirstOrDefault(app => AppIdentity.Matches(app, foreground.ProcessName));
    }

    /// <summary>Hold the deck where the user put it.</summary>
    private void Pin()
    {
        // Clamped at both ends. Below five seconds the deck moves while you are still
        // looking at it; "never" is stored as int.MaxValue, which is more seconds than
        // DateTime can hold.
        _pinnedUntil = _settings.PinSeconds >= int.MaxValue
            ? DateTime.MaxValue
            : DateTime.UtcNow + TimeSpan.FromSeconds(Math.Clamp(_settings.PinSeconds, 5, 86_400));
        ShowPinState();
    }

    /// <summary>
    /// The choices for how long a manual page stays put.
    ///
    /// A menu of durations rather than a number box: nobody wants to type 45, they want
    /// "longer than that". Never is included because on a deck that is only ever driven
    /// by hand, automatic switching is the wrong feature entirely.
    /// </summary>
    private void BuildPinMenu()
    {
        (string Label, int Seconds)[] choices =
        [
            ("10 seconds", 10),
            ("30 seconds", 30),
            ("1 minute", 60),
            ("5 minutes", 300),
            ("Never let go", int.MaxValue),
        ];

        PinMenu.Items.Clear();

        foreach ((string label, int seconds) in choices)
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.PinSeconds == seconds,
                Tag = seconds
            };

            item.Click += (s, _) =>
            {
                _settings.PinSeconds = (int)((MenuItem)s!).Tag;
                _settings.Save();

                BuildPinMenu();
                StatusLabel.Text = $"The deck now stays where you put it for {label.ToLowerInvariant()}.";
            };

            PinMenu.Items.Add(item);
        }
    }

    /// <summary>
    /// Shows how long manual navigation is holding the deck, and hides itself when it
    /// is not.
    ///
    /// Standing still and being broken look identical from the outside. Following
    /// simply stops for a while, with nothing anywhere saying why, and the reasonable
    /// conclusion is that the feature is faulty - which is exactly the conclusion the
    /// person who wrote it reached, twice, in one evening.
    /// </summary>
    private void ShowPinState()
    {
        if (PinBadge is null) return;

        double left = (_pinnedUntil - DateTime.UtcNow).TotalSeconds;

        if (left <= 0)
        {
            PinBadge.Visibility = Visibility.Collapsed;
            return;
        }

        PinLabel.Text = _pinnedUntil == DateTime.MaxValue
            ? "Held here"
            : $"Held here {Math.Ceiling(left):0} s";
        PinBadge.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Ends the hold now. Clicking the thing that explains why nothing is happening is
    /// the obvious way to ask for it to start happening.
    /// </summary>
    private void OnReleasePin(object sender, MouseButtonEventArgs e)
    {
        _pinnedUntil = DateTime.MinValue;
        ShowPinState();

        StatusLabel.Text = "Following the focused app again.";

        // Do not wait for the next window change: the app in front now is the one they
        // want, and they just said so.
        if (_watcher?.LastApp is { } current) ApplyForeground(current);
    }

    private void OnToggleMcp(object sender, RoutedEventArgs e)
    {
        if (_mcp?.IsRunning == true)
        {
            _mcp.Stop();
            _settings.McpEnabled = false;
            _settings.Save();
            UpdateMcpMenuItem();
            StatusLabel.Text = "Agent bridge stopped.";
            return;
        }

        StartMcp(announce: true);
    }

    /// <summary>
    /// Opens the page the MCP server serves, in the browser.
    ///
    /// It is written for an agent, but a person can read it and hand the address over -
    /// which is the point: "tell your assistant to look at this" needs somewhere to
    /// look. It is only reachable while the bridge is running, so offer to start it
    /// rather than opening a browser onto a refused connection.
    /// </summary>
    private void OnShowAgentHelp(object sender, RoutedEventArgs e)
    {
        if (_mcp?.IsRunning != true)
        {
            MessageBoxResult answer = MessageBox.Show(
                "The agent bridge is off, so the page is not being served.\n\n"
                + "Start it and open the page?",
                "Instructions for your AI assistant",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            StartMcp(announce: false);

            if (_mcp?.IsRunning != true) return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_mcp!.Url) { UseShellExecute = true });
            StatusLabel.Text = $"Opened {_mcp.Url} - the address an agent needs.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Could not open the browser: " + ex.Message;
        }
    }

    private void StartMcp(bool announce)
    {
        try
        {
            _mcp ??= new McpServer(this);
            _mcp.Start(_settings.McpPort);

            _settings.McpEnabled = true;
            _settings.Save();
            UpdateMcpMenuItem();

            if (announce) StatusLabel.Text = $"Agent bridge listening on {_mcp.Url} - loopback only.";
        }
        catch (Exception ex)
        {
            _settings.McpEnabled = false;
            UpdateMcpMenuItem();
            StatusLabel.Text = $"Could not start the agent bridge on port {_settings.McpPort}: {ex.Message}";
        }
    }

    private void UpdateMcpMenuItem() =>
        McpMenuItem.InputGestureText = _mcp?.IsRunning == true ? $"on :{_mcp.Port}" : "off";

    private void OnToggleFollow(object sender, RoutedEventArgs e)
    {
        _settings.FollowForegroundApp = !_settings.FollowForegroundApp;
        _settings.Save();
        UpdateFollowButton();

        StatusLabel.Text = _settings.FollowForegroundApp
            ? "Following the focused app. Build a page for an app and it opens when you switch to it."
            : "Not following the focused app - pages only open when you press their key.";
    }

    private void UpdateFollowButton()
    {
        FollowButton.Content = "Follow focus: " + (_settings.FollowForegroundApp ? "on" : "off");
        FollowButton.Foreground = _settings.FollowForegroundApp
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("TextMuted");
    }

    // ------------------------------------------------------------- drag & drop

    private void OnAppListMouseMove(object sender, MouseEventArgs e) =>
        MaybeStartDrag(e, AppList, AppDragFormat);

    private void OnActionListMouseMove(object sender, MouseEventArgs e) =>
        MaybeStartDrag(e, ActionList, ActionDragFormat);

    private void MaybeStartDrag(MouseEventArgs e, ListBox list, string format)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragOrigin = default;
            return;
        }

        Point position = e.GetPosition(this);

        if (_dragOrigin == default)
        {
            _dragOrigin = position;
            return;
        }

        Vector moved = position - _dragOrigin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        object? payload = list.SelectedItem;

        if (format == ActionDragFormat && list.SelectedItem is ActionListItem item)
            payload = (object?)item.Hotkey ?? item.Definition;

        if (payload is null) return;

        _dragOrigin = default;
        DragDrop.DoDragDrop(list, new DataObject(format, payload), DragDropEffects.Copy);
    }

    private void OnCellDropped(object? sender, DeckDropEventArgs e)
    {
        if (e.Data.GetDataPresent(DeckSimulatorControl.CellDragFormat) &&
            e.Data.GetData(DeckSimulatorControl.CellDragFormat) is int from)
        {
            MoveKey(from, e.ProtocolIndex);
            return;
        }

        if (e.Data.GetDataPresent(AppDragFormat) &&
            e.Data.GetData(AppDragFormat) is InstalledApp app)
        {
            Assign(e.ProtocolIndex, CreateAppButton(app), app.Name);
            return;
        }

        if (!e.Data.GetDataPresent(ActionDragFormat)) return;

        switch (e.Data.GetData(ActionDragFormat))
        {
            case HotkeyBinding hotkey:
                Assign(e.ProtocolIndex, BuildHotkeyButton(hotkey), hotkey.DisplayLabel);
                break;

            case ActionDefinition action:
                AssignAction(e.ProtocolIndex, action);
                break;
        }
    }

    /// <summary>
    /// Moves a key, swapping with whatever is already there.
    ///
    /// Swapping rather than overwriting because the target is usually occupied - a deck
    /// fills up - and losing a key to a slip of the mouse while rearranging is a poor
    /// trade for the two seconds saved by not putting the other one back.
    /// </summary>
    private void MoveKey(int from, int to)
    {
        if (from == to) return;
        if (_navigator.Current is not { } page) return;
        if (page.Get(from) is not { } moved) return;

        DeckButton? displaced = page.Get(to);

        page.Set(to, moved);

        if (displaced is not null) page.Set(from, displaced);
        else page.Cells.Remove(from);

        _controller?.Invalidate(from);
        _controller?.Invalidate(to);
        RepaintKeys(highPriority: true);
        SaveProfile();

        StatusLabel.Text = displaced is null
            ? $"Moved key {from} to {to}."
            : $"Swapped keys {from} and {to}.";
    }

    // ------------------------------------------------------------------ ticking

    private async void OnTick(object? sender, EventArgs e)
    {
        ShowPinState();

        if (_controller is null) return;

        await RefreshObsThumbnailsAsync();

        DeckPage? page = _navigator.Current;

        if (page?.RefreshInterval is not null)
        {
            await _media.RefreshAsync();
            RepaintKeys(highPriority: false);
        }

        DateTime now = DateTime.UtcNow;

        foreach (int index in DeckLayout.InfoCells())
        {
            // An agent message owns the cell until it expires.
            if (_agent.NotificationsUntil.TryGetValue(index, out DateTime showing))
            {
                if (now < showing) continue;

                _agent.NotificationsUntil.Remove(index);
                _widgetDue.Remove(index);
                _controller.Invalidate(index);
            }

            // A sub-page may claim an info cell; otherwise the home page owns it.
            DeckButton? button = page?.Get(index) ?? RootPage.Get(index);

            if (button is null)
            {
                _controller.Update(index, _renderer.Render(BlankInfoVisual()), highPriority: false);
                continue;
            }

            // Widgets carry their own refresh rate: a clock ticks every second, disk
            // free every thirty. Rendering one that is not due is wasted work even
            // though the hash check would throw the result away.
            if (button.Tag is WidgetPlacement placement)
            {
                if (_widgetDue.TryGetValue(index, out DateTime due) && now < due) continue;
                _widgetDue[index] = now + placement.Widget.Interval;
            }

            _controller.Update(index, _renderer.Render(button.Visual()), highPriority: false);
        }
    }

    private static CellVisual BlankInfoVisual() => new()
    {
        Background = Color.FromRgb(0x0A, 0x0A, 0x0D)
    };

    private DeckButton CreateWidgetButton(IInfoWidget widget, WidgetTheme? theme = null)
    {
        var placement = new WidgetPlacement { Widget = widget, Theme = theme ?? widget.DefaultTheme };

        return new DeckButton
        {
            Tag = placement,
            Visual = () => placement.Widget.Render(placement.Theme)
            // No OnPress: the info cells have no switch under them.
        };
    }

    // ----------------------------------------------------------------- app list

    private async Task LoadAppsAsync()
    {
        AppCountLabel.Text = "loading...";

        IReadOnlyList<InstalledApp> shellApps;

        try
        {
            shellApps = await AppsFolder.EnumerateAsync(iconSize: 256);
        }
        catch (Exception ex)
        {
            AppCountLabel.Text = "failed: " + ex.Message;
            return;
        }

        // Steam games are invisible to the shell - Steam stopped writing Start-menu
        // entries - so they are read from Steam's own bookkeeping and appended. A
        // failure here must not cost the user the app list they did get.
        IReadOnlyList<InstalledApp> games = [];

        try
        {
            games = await Task.Run(() => SteamLibrary.Enumerate());
            DeckLog.Out("steam-library", $"{games.Count} game(s) from {SteamLibrary.InstallPath ?? "no Steam install"}");
        }
        catch (Exception ex)
        {
            DeckLog.Out("steam-library", ex.Message);
        }

        _apps = games.Count == 0
            ? shellApps
            : [.. shellApps.Concat(games)
                    .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];

        _steamGameCount = games.Count;

        // Anyone who customised their app list did so before games could appear in it.
        _selection.AdoptNewSource(games);

        ApplyFilter(SearchBox.Text);

        RestoreOrSeed();

        _controller?.InvalidateAll();
        RepaintKeys(highPriority: true);
    }

    private void OnAppSelected(object sender, SelectionChangedEventArgs e)
    {
        if (AppList.SelectedItem is not InstalledApp app) return;

        ActionList.SelectedItem = null;
        StatusLabel.Text = $"Drag {app.Name} onto a key.";
    }

    private void OnActionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ActionList.SelectedItem is not ActionListItem item) return;

        AppList.SelectedItem = null;

        StatusLabel.Text = item.Definition?.Id == "input.hotkey"
            ? "Click to define a new hotkey for this page."
            : $"Drag {item.Name} onto a key.";
    }

    /// <summary>
    /// "Add a Hotkey" opens a dialog, so it needs a real click.
    ///
    /// It used to hang off SelectionChanged, and assigning a new ItemsSource makes WPF
    /// select the first item on its own - the list is bound to an ICollectionView, and
    /// a Selector synchronises with the view's current item. On a page with an empty
    /// library "Add a Hotkey" is that first item, so simply navigating to the page
    /// opened an empty dialog nobody asked for.
    /// </summary>
    private void OnActionListClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource as DependencyObject) is not { } item) return;
        if (item.Definition?.Id != "input.hotkey") return;

        e.Handled = true;
        AddHotkeyToLibrary();
    }

    /// <summary>
    /// Right-clicking a hotkey in the palette edits or removes it.
    ///
    /// Defining one was a one-way door: there was no gesture anywhere that took it
    /// back out of the page's library, so a typo lived there forever.
    /// </summary>
    private void OnActionListRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource as DependencyObject) is not { Hotkey: { } hotkey }) return;
        if (_navigator.Current is not { } page) return;

        e.Handled = true;

        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        var edit = new MenuItem { Header = $"Edit \"{hotkey.DisplayLabel}\"..." };
        edit.Click += (_, _) => EditLibraryHotkey(page, hotkey);
        menu.Items.Add(edit);

        menu.Items.Add(new Separator());

        var remove = new MenuItem { Header = "Remove from this page" };
        remove.Click += (_, _) => RemoveLibraryHotkey(page, hotkey);
        menu.Items.Add(remove);

        menu.IsOpen = true;
    }

    private static ActionListItem? ItemUnder(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);

        return (source as ListBoxItem)?.DataContext as ActionListItem;
    }

    private void EditLibraryHotkey(DeckPage page, HotkeyBinding hotkey)
    {
        var dialog = new HotkeyWindow(hotkey, _renderer) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        List<HotkeyBinding> library = LibraryFor(page.Id);
        int at = library.IndexOf(hotkey);
        if (at >= 0) library[at] = dialog.Result;

        // Keys already placed from this definition follow the edit. Leaving them on the
        // old combination would mean the palette and the deck disagree about what the
        // same named thing does.
        foreach (int index in DeckLayout.Keys())
        {
            if (ReferenceEquals(page.Get(index)?.Tag, hotkey))
            {
                page.Set(index, BuildHotkeyButton(dialog.Result));
                _controller?.Invalidate(index);
            }
        }

        SaveProfile();
        ShowActionsFor(page);
        RepaintKeys(highPriority: true);

        StatusLabel.Text = $"Updated {dialog.Result.DisplayLabel} on \"{page.Title ?? page.Id}\".";
    }

    private void RemoveLibraryHotkey(DeckPage page, HotkeyBinding hotkey)
    {
        if (!LibraryFor(page.Id).Remove(hotkey)) return;

        SaveProfile();
        ShowActionsFor(page);

        // Keys already placed are left alone on purpose: defining a hotkey and putting
        // it on a key are two decisions, and undoing the first should not silently
        // undo the second. Clear the key itself to remove it from the deck.
        StatusLabel.Text = $"Removed {hotkey.DisplayLabel} from the {page.Title ?? page.Id} palette. "
            + "Keys already placed still work - clear the key to remove one.";
    }

    private void AddHotkeyToLibrary()
    {
        DeckPage? page = _navigator.Current;
        if (page is null) return;

        ActionList.SelectedItem = null;

        var dialog = new HotkeyWindow(null, _renderer) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        LibraryFor(page.Id).Add(dialog.Result);
        SaveProfile();
        ShowActionsFor(page);

        StatusLabel.Text =
            $"Added {dialog.Result.DisplayLabel} ({dialog.Result.Combination}) to {page.Title}. Drag it onto a key.";
    }

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

    private void OnSelectApplications(object sender, RoutedEventArgs e)
    {
        if (_apps.Count == 0)
        {
            StatusLabel.Text = "Applications are still loading.";
            return;
        }

        var dialog = new AppSelectionWindow(_apps, _selection.Selected) { Owner = this };

        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        _selection.Set(dialog.Result);
        ApplyFilter(SearchBox.Text);
        StatusLabel.Text = $"Showing {dialog.Result.Count} applications. Saved to {AppSelectionStore.FilePath}.";
    }

    /// <summary>
    /// AppsFolder also lists help files, uninstallers and control panel applets.
    /// Until the user curates the list themselves, the heuristic decides.
    /// </summary>
    private IEnumerable<InstalledApp> VisibleApps => _selection.Apply(_apps);

    private void ApplyFilter(string? query)
    {
        if (AppList is null) return;

        List<InstalledApp> visible = Search(VisibleApps, query);
        AppList.ItemsSource = visible;

        string source = _steamGameCount > 0
            ? $"shell:AppsFolder + {_steamGameCount} Steam"
            : "shell:AppsFolder";

        AppCountLabel.Text = visible.Count == _apps.Count
            ? $"{_apps.Count} found via {source}"
            : $"{visible.Count} of {_apps.Count} via {source}";
    }

    /// <summary>
    /// Substring first, then the identifier, then a loose subsequence.
    ///
    /// Nobody types "Visual Studio Code" - they type "vscode", which is not a
    /// substring of anything. It is however a subsequence of "visualstudiocode", and
    /// that one fallback covers most of the gap between what an app is called in the
    /// Start menu and what people call it. Kept as a fallback rather than the primary
    /// rule because subsequence matching on its own is far too loose.
    /// </summary>
    private static List<InstalledApp> Search(IEnumerable<InstalledApp> apps, string? query)
    {
        List<InstalledApp> all = apps.ToList();
        if (string.IsNullOrWhiteSpace(query)) return all;

        string needle = query.Trim();

        List<InstalledApp> byName = all
            .Where(a => a.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        if (byName.Count > 0) return byName;

        List<InstalledApp> byId = all
            .Where(a => a.AppUserModelId.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byId.Count > 0) return byId;

        return all.Where(a => IsSubsequence(needle, a.Name)).ToList();
    }

    private static bool IsSubsequence(string needle, string haystack)
    {
        int at = 0;

        foreach (char candidate in haystack)
        {
            if (!char.IsLetterOrDigit(candidate)) continue;

            while (at < needle.Length && !char.IsLetterOrDigit(needle[at])) at++;
            if (at >= needle.Length) break;

            if (char.ToLowerInvariant(candidate) == char.ToLowerInvariant(needle[at])) at++;
        }

        while (at < needle.Length && !char.IsLetterOrDigit(needle[at])) at++;
        return at >= needle.Length;
    }

    private async void OnReloadApps(object sender, RoutedEventArgs e) => await LoadAppsAsync();

    private ConsoleWindow? _console;

    private void OnShowConsoleCommand(object sender, ExecutedRoutedEventArgs e) => ShowConsole();

    private void OnShowConsole(object sender, RoutedEventArgs e) => ShowConsole();

    private ForegroundWindowInspector? _inspector;

    private void OnShowForeground(object sender, RoutedEventArgs e)
    {
        if (_inspector is { IsLoaded: true })
        {
            _inspector.Activate();
            return;
        }

        _inspector = new ForegroundWindowInspector(
            () => _watcher?.LastApp,
            MatchApp,
            f => _matches.PageFor(f) is { } id
                ? _pages.GetValueOrDefault(id)?.Title ?? id
                : null)
        {
            Owner = this
        };

        _inspector.Closed += (_, _) => _inspector = null;
        _inspector.Show();
    }

    private void ShowConsole()
    {
        if (_console is { IsLoaded: true })
        {
            _console.Activate();
            return;
        }

        _console = new ConsoleWindow { Owner = this };
        _console.Closed += (_, _) => _console = null;
        _console.Show();
    }

    private void OnShowIcons(object sender, RoutedEventArgs e) =>
        new IconBrowserWindow(_renderer, picking: false) { Owner = this }.Show();

    private void OnAbout(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private void OnExit(object sender, RoutedEventArgs e) => ExitApplication();

    private void OnHideToTray(object sender, RoutedEventArgs e) => HideToTray();

    /// <summary>
    /// A list rather than a cycle. Four built-in families is fine to click through;
    /// the moment someone drops a dozen of their own into the fonts folder it is not.
    /// Each name is drawn in its own face, which is the whole point of choosing one.
    /// </summary>
    private void OnPickFont(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = FontButton,
            Placement = PlacementMode.Top
        };

        bool separated = false;

        foreach (FontOption option in _textLab.Fonts)
        {
            if (option.IsUserFont && !separated)
            {
                menu.Items.Add(new Separator());
                separated = true;
            }

            bool current = option.Name.Equals(_textLab.Font.Name, StringComparison.OrdinalIgnoreCase);

            var item = new MenuItem
            {
                Header = option.Name,
                FontFamily = option.Family,
                FontSize = 14,
                InputGestureText = current ? "in use" : option.IsUserFont ? "yours" : ""
            };

            if (current)
                item.Foreground = (Brush)FindResource("Accent");

            FontOption chosen = option;
            item.Click += (_, _) =>
            {
                _textLab.FontName = chosen.Name;
                ApplyTextLab();
                StatusLabel.Text = $"Font: {chosen.Name}.";
            };

            menu.Items.Add(item);
        }

        if (_textLab.Fonts.All(f => !f.IsUserFont))
        {
            menu.Items.Add(new Separator());

            var hint = new MenuItem { Header = "Add your own fonts..." };
            hint.Click += OnOpenFontsFolder;
            menu.Items.Add(hint);
        }

        menu.IsOpen = true;
    }

    private void OnCycleWeight(object sender, RoutedEventArgs e)
    {
        _textLab.CycleWeight();
        ApplyTextLab();
        StatusLabel.Text = $"Weight: {_textLab.Weight.Name}. Heavier strokes survive small sizes better.";
    }

    private void OnCycleFormat(object sender, RoutedEventArgs e)
    {
        _textLab.CycleFormatting();
        ApplyTextLab();
        StatusLabel.Text = _textLab.Format.Name == "Ideal"
            ? "Ideal: correct letter spacing, softer stroke tops."
            : "Display: stems snapped to the pixel grid, spacing quantised to whole pixels.";
    }

    private void OnCycleRender(object sender, RoutedEventArgs e)
    {
        _textLab.CycleRendering();
        ApplyTextLab();
        StatusLabel.Text = $"Antialiasing: {_textLab.Render.Name}.";
    }

    private void OnResetTextLab(object sender, RoutedEventArgs e)
    {
        var defaults = new TextLab();

        _textLab.FontName = defaults.FontName;
        _textLab.WeightName = defaults.WeightName;
        _textLab.FormattingName = defaults.FormattingName;
        _textLab.RenderingName = defaults.RenderingName;
        _textLab.LabelSize = defaults.LabelSize;

        LabelSizeSlider.Value = _textLab.LabelSize;
        ApplyTextLab();
        StatusLabel.Text = "Label rendering reset to defaults.";
    }

    private void OnOpenFontsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(TextLab.UserFontsPath);
            Process.Start(new ProcessStartInfo(TextLab.UserFontsPath) { UseShellExecute = true });
            StatusLabel.Text = "Drop .ttf or .otf files here; they appear in the font list after a restart.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Could not open the fonts folder: " + ex.Message;
        }
    }

    private void OnLabelSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // InitializeComponent raises this while applying the value declared in XAML,
        // before the saved settings have been pushed into the slider - acting on it
        // would overwrite the user's size with the markup default and persist it.
        if (!_ready) return;

        _textLab.LabelSize = e.NewValue;
        ApplyTextLab();
    }

    private void ApplyTextLab()
    {
        _renderer.LabelFontFamily = _textLab.Font.Family;
        _renderer.LabelWeight = _textLab.Weight.Weight;
        _renderer.FormattingMode = _textLab.Format.Mode;
        _renderer.RenderingMode = _textLab.Render.Mode;

        FontButton.Content = _textLab.Font.Name;
        WeightButton.Content = _textLab.Weight.Name;
        FormatButton.Content = _textLab.Format.Name;
        RenderButton.Content = _textLab.Render.Name;
        LabelSizeLabel.Text = _textLab.LabelSize.ToString("0.0", CultureInfo.InvariantCulture) + " px";

        _textLab.Save();
        RerenderEverything();
    }

    /// <summary>Forces every cell through the renderer again, past the hash check.</summary>
    private void RerenderEverything()
    {
        _controller?.InvalidateAll();
        _widgetDue.Clear();
        RepaintKeys(highPriority: true);
    }

    private async void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_transport is null) return;

        _settings.Brightness = (int)e.NewValue;
        _settings.Save();

        await _transport.SetBrightnessAsync(_settings.Brightness);
    }

    // ------------------------------------------------------------------ visuals

    private CellVisual BuildAppVisual(InstalledApp app)
    {
        string label = app.Name;

        Color tint = app.Icon is { } icon ? DominantColor(icon) : Color.FromRgb(0x20, 0x20, 0x26);

        return new CellVisual
        {
            Background = Darken(tint, 0.30),
            BackgroundGradientTo = Darken(tint, 0.12),
            Icon = app.Icon,
            IconScale = 0.88,
            Label = label,
            // One line at 13px rather than two at 10.2px. Measured on an 85px cell,
            // 10.2px Verdana gives ascenders under two pixels tall - they render, but
            // they are too short to read as ascenders, which is why the text looked
            // clipped. 13px buys a third more letter height and 25% more icon, in
            // exchange for names having to fit on one line - which is what per-key
            // labels are for.
            LabelSize = _textLab.LabelSize,
            LabelPosition = LabelPosition.Bottom,
            ReservedLabelLines = 1
        };
    }

    private static CellVisual EmptyKeyVisual(int index) => new()
    {
        Background = Color.FromRgb(0x0C, 0x0C, 0x0F),
        Label = index.ToString(),
        LabelColor = Color.FromRgb(0x2E, 0x2E, 0x35),
        LabelSize = 14,
        LabelPosition = LabelPosition.Bottom
    };

    /// <summary>
    /// Alpha-weighted average colour of an icon, used to tint the key behind it.
    /// Cheap and good enough - a proper palette extractor is not worth it here.
    /// </summary>
    private static Color DominantColor(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        double sumR = 0, sumG = 0, sumB = 0, sumWeight = 0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            if (alpha < 32) continue;

            // Ignore near-greyscale pixels so a white glyph does not wash out the tint.
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            double saturation = max == 0 ? 0 : (max - min) / (double)max;

            double weight = alpha / 255.0 * (0.15 + saturation);

            sumR += r * weight;
            sumG += g * weight;
            sumB += b * weight;
            sumWeight += weight;
        }

        if (sumWeight < 0.001) return Color.FromRgb(0x20, 0x20, 0x26);

        return Color.FromRgb(
            (byte)Math.Clamp(sumR / sumWeight, 0, 255),
            (byte)Math.Clamp(sumG / sumWeight, 0, 255),
            (byte)Math.Clamp(sumB / sumWeight, 0, 255));
    }

    private static Color Darken(Color color, double factor) => Color.FromRgb(
        (byte)Math.Clamp(color.R * factor, 0, 255),
        (byte)Math.Clamp(color.G * factor, 0, 255),
        (byte)Math.Clamp(color.B * factor, 0, 255));
}
