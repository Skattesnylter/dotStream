# dotStream

Own software for the AJAZZ AKP153E stream controller, because the bundled
software is not good.

Not an Elgato-compatible plugin host. Deliberately.

## Status

Running on real hardware. The HID transport is written and measured against an
AKP153E: brightness, image upload, key presses and releases, and the full 6×3 cell
map, all confirmed on the device.

**You do not need the deck to start.** dotStream runs against a built-in simulator
with the same eighteen cells, the same indices, and the same behaviour down to
reporting press and release separately — so a long press or a key that repeats while
held works identically on screen and on the desk. Set up your pages while yours is in
the post; when it arrives, plug it in and everything is already there.

With a deck attached the two run side by side: the window mirrors the hardware, and a
click on either counts. Useful when the deck is behind a monitor, or when you are
working somewhere else.

| | |
|---|---|
| ✅ | 18-cell 6×3 model, key ↔ protocol index mapping |
| ✅ | Cell renderer: gradients, icons, labels, gauge rings, pixel hashing |
| ✅ | Icon extraction from `shell:AppsFolder` at 256×256 with real alpha |
| ✅ | Upload controller: dirty-tracking, coalescing, priority queue |
| ✅ | Simulator transport with realistic per-upload latency |
| ✅ | Info-cell widgets: CPU, RAM, clock |
| ✅ | File / Help menu, About, dark title bars |
| ✅ | Page stack: press a running app's key to open its own page, with Back |
| ✅ | Action palette — drag apps and actions onto keys |
| ✅ | Media session integration: transport, live play state, album art |
| ✅ | App selection dialog, persisted to `%APPDATA%\dotStream\selection.json` |
| ✅ | Pages and key assignments persisted to `%APPDATA%\dotStream\profile.json` |
| ✅ | Tray icon, close-to-tray, deliberate exit |
| ✅ | Follows the focused app, with manual-navigation pinning |
| ✅ | MCP server — an agent can ask a question on physical keys and wait |
| ✅ | HID transport — measured against the hardware, not taken from the notes |
| ✅ | Hot-plug: plug the deck in at any point and it comes to life |
| ✅ | Hotkeys, sequence macros, text macros, run a program, open a link |
| ✅ | Steam games, which the Windows shell cannot see at all |
| ✅ | Cell calibration for variants nobody has measured |
| ✅ | Start with Windows, into the tray |
| ⬜ | Generic MCP client action — a key that calls someone else's tool |
| ⬜ | Folders and a page-switch key |

Full list in [TODO.md](TODO.md).

## Installing

Releases carry two downloads, both self-contained — no .NET runtime to install
first. Requires Windows 10 2004 (build 19041) or newer, x64.

| Download | For |
|---|---|
| `dotStream-<version>-win-x64-setup.exe` | Normal install, Start menu entry, uninstaller. Per-user, so no UAC prompt. |
| `dotStream-<version>-win-x64-portable.zip` | A single exe. Writes nothing outside `%APPDATA%\dotStream`. |

Settings live in `%APPDATA%\dotStream\` and are deliberately left alone by the
uninstaller:

| File | Holds |
|---|---|
| `profile.json` | The deck layout — pages and key assignments |
| `selection.json` | Which applications appear in the palette |
| `labels.json` | Custom key names, keyed by application, not by key |
| | Widget choice and colours live in `profile.json` with the cell |
| `text.json` | Label font, weight, formatting, antialiasing and size |
| `fonts\` | Drop `.ttf` or `.otf` files here to add them to the font list |

Fonts are scanned at startup, so a newly added file appears after a restart.
**File › Open fonts folder** opens it.

### Windows will warn you, and that is expected

dotStream is not code signed yet, so Windows SmartScreen shows
**"Windows protected your PC"** on first run. Choose **More info**, then
**Run anyway**.

This is not a judgement about the file — it is what Windows shows for any
executable without a paid certificate behind it. Signing is on the
[roadmap](TODO.md); free certificates for open-source projects exist but take an
application and a public release to qualify for.

### Verify what you downloaded

Every release includes `SHA256SUMS.txt`. Check your download against it:

```powershell
Get-FileHash .\dotStream-0.1.0-win-x64-setup.exe -Algorithm SHA256
```

Compare the result with the matching line in `SHA256SUMS.txt`. Builds are produced
by [GitHub Actions](.github/workflows/release.yml) from a tagged commit, so the
checksum can be traced back to the source that produced it.

## Using it

### Putting something on a key

**Drag it.** Everything you place is dragged from a list on the left onto a key —
applications from the home page, actions from an app's own page. Deliberately not
click-to-select-then-click-to-place: with fifteen keys already carrying something,
a stray click that overwrites one is too easy and too quiet.

Drag a placed key onto another key to move it. Right-click any key for **Clear**,
**Rename** and the widget options.

### Pages

The home page is your applications. **Press an app's key and it launches**; press it
again once it is running and its own page opens, holding the actions that belong to
that program. **Back** returns home.

Pages can also open themselves. **Follow the focused app** (the toolbar button) opens
a program's page when you switch to it, so alt-tabbing to Excel puts the Excel keys
under your fingers. Navigating by hand pins the deck for thirty seconds so it does
not move while you are using it.

### What a key can do

| | |
|---|---|
| **Application** | Launch it, and open its page |
| **Hotkey** | One combination — `Ctrl+Shift+P` |
| **Sequence macro** | Several in order — `Alt, H, M, C` for Merge and Centre. Ribbon commands are reachable this way without any application support |
| **Text macro** | Types a string. Quote a step in a sequence to type it mid-way |
| **Run** | A program or script, with arguments |
| **Link** | A URL or a file. `steam://rungameid/892970` starts Valheim |
| **Media** | Play/pause, next, previous, volume, mute — with live state and album art |
| **Widget** | CPU, RAM or clock, on the three cells in column five |

Volume keys repeat while held. Every key reports press and release separately,
because the hardware does.

### Which applications are listed

dotStream shows what Windows considers installed, filtered down to things that look
like applications rather than uninstallers and help files. **File → Select
applications** overrides that list entirely if the guess is wrong.

Steam games are added separately, because Steam stopped writing Start-menu entries
and the shell genuinely cannot see them. They are read from Steam's own files and
launched through `steam://`. If a game's icon looks low-resolution, browse your
library in Steam once — that fills its artwork cache, and dotStream uses it.

If dotStream misidentifies which program is in front, **View → What's in front?**
shows what it saw, and lets you correct it. The correction is remembered.

### If the pictures look wrong

**View → Calibrate cells.** Several devices ship under this product name and this
one measured 100×100, against notes claiming 85×85 and a manual recommending
126×126. If your icons come out cropped, offset, or leave a border of the previous
image behind, drag the size slider until all four coloured bands of the measuring
pattern look equally thick. Rotation is there for a panel mounted a different way.

### Starting automatically

**File → Start with Windows** runs dotStream in the tray when you sign in. Windows
offers no way to start an application when a USB device appears — not without
enabling a log that records every USB event on the machine — so this comes at it
from the other side: dotStream is already running, and it watches for the deck
continuously. Plug in whenever; it wakes up.

Closing the window hides it to the tray, because the deck has to keep working. **File
→ Exit** quits properly, and leaves the deck dark.

### Agents

**File → Agent bridge (MCP)** lets an AI assistant read and write your deck — propose
a page of keys for you to accept or reject, put a question on physical keys and wait
for the answer. Off by default. **Help → Instructions for your AI assistant** gives
you the address to hand over.

## Build & run

Needs the .NET 10 SDK.

```powershell
dotnet build DotStream.slnx
dotnet run --project src\DotStream.App
```

## Layout

```
src/
  DotStream.Core        Cell model, IDeckTransport, DeckController, IDeckAction
  DotStream.Rendering   CellRenderer, system metrics, info widgets
  DotStream.Icons       shell:AppsFolder enumeration, IShellItemImageFactory
  DotStream.Simulator   Virtual 6×3 deck + SimulatorTransport
  DotStream.App         WPF host
docs/
  PROTOCOL.md           Wire protocol, verified sources, open questions
```

## Design decisions worth remembering

**The side screen is three ordinary cells.** Not an 854×480 panel. They are
addressed with the same `BAT` command as the keys, at indices 16–18, and have no
switch under them. This is what makes live info display cheap. See
[docs/PROTOCOL.md](docs/PROTOCOL.md) §5.

**Rendering is device-agnostic.** `CellRenderer` always produces upright images.
Any device-specific orientation transform belongs in the transport, so the
simulator and the real device share one rendering path.

**Uploads are hash-gated.** The firmware ACKs each image and corrupts the cell if
a new upload starts early, so writes are serialised. That makes a redundant
upload genuinely expensive, hence `DeckController` comparing pixel hashes and
keeping only the newest pending image per cell.

**Icons come from the shell, not from PE resources.** `ExtractAssociatedIcon`
caps at 32×32 and finds nothing for Store/MSIX apps. `IShellItemImageFactory`
via `shell:AppsFolder` handles Win32, MSIX, shortcuts and folders uniformly, at
256×256 with a real alpha channel — and the same AppUserModelID launches all of
them.

**`TextFormattingMode` must be passed to the `FormattedText` constructor.**
`TextOptions.SetTextFormattingMode` on a `DrawingVisual` is a silent no-op — it is an
attached property inherited by framework elements, while `DrawingContext.DrawText`
uses whatever the `FormattedText` itself was built with. It looks like it works.

**Label rendering is a control panel, not a constant.** Font, weight, formatting
mode, antialiasing and size are all switchable at runtime and saved to
`%APPDATA%\dotStream\text.json`. There is no answer that can be found on a monitor:
`Display` snaps stems to the pixel grid so stroke tops stay solid but quantises every
advance width, `Ideal` keeps the spacing and lets the tops fray. Which reads better
at 100×100, under a plastic cap, at a desk angle, is a judgement only the hardware
can settle — which is why it is a slider rather than a decision taken here.

**Shell icons need their row order checked.** Shell handlers return both top-down
and bottom-up DIBs. Copying the rows straight into a `BitmapSource` flips roughly
half of all icons, and because it only shows on asymmetric artwork it is easy to
ship without noticing. `ShellInterop` reads `DIBSECTION.dsBmih.biHeight` and
flips when needed — `BITMAP.bmHeight` is always positive and tells you nothing.

**Buttons are closures; profiles are declarations.** A `DeckButton` holds `Func`s,
which cannot be serialised. Every button therefore carries a `Tag` — an
`InstalledApp` or a catalogue action id — and that is what goes to disk. Closures
are rebuilt on load from the app list and `ActionCatalog`, so a profile stays valid
when an action's implementation changes.

**An agent may draw on the deck; it may never say what a key does.** The MCP tools
set labels, colours and questions. Nothing binds a key to an action — those come only
from the user's profile. A confused or compromised agent can therefore make the deck
look wrong, but it cannot relabel "Pause music" into something that runs a program.
The server binds `127.0.0.1` only and is off until switched on.

**Animation is possible after all.** This used to say the opposite, on the strength of
an estimate that turned out to be ten times too pessimistic. Measured against the
hardware: one cell takes **1.0 ms**, a full eighteen-cell repaint takes **18 ms**, and
JPEG encoding adds 0.26 ms per cell. Thirty frames per second across the whole deck
ran for six seconds without dropping one, and looked smooth.

Static icons that change on state is still the design point — a deck that moves all
the time is a deck you stop reading. But the constraint was never real, so where
motion earns its place it is available.

## Licence and warranty

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

dotStream is provided **as is, without warranty of any kind**. You use it at your own
risk, and the author accepts no liability for any damage or loss arising from its use.
It talks to hardware over a reverse-engineered protocol; that is the whole point of the
project, and it is also the reason this paragraph is here.

Unofficial. Not affiliated with, authorised by, or endorsed by AJAZZ, Mirabox or
Elgato.

## Next

1. HID enumeration tool — identify what this unit actually reports
2. `DIS` → `LIG 50` → `CLE` to prove the transport
3. Orientation brute-force, 8 variants of an asymmetric glyph
4. Persistence test: upload, unplug, replug — framebuffer or flash?
5. Media sessions + a Spotify page
6. Foreground-window hook and profile switching, with manual navigation pinning
