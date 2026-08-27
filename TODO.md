# dotStream — TODO

Ordered roughly by what unblocks the most. The **hardware** markers are historical:
the AKP153E arrived on 04.08.2026 and everything they gated is measured.

---

## Releases

Currently **1.0.1**, which leaves the deck properly dark on exit. `LIG 0` is the
lowest backlight step rather than off, and `HAN` removes what is left. Both halves
were checked before it shipped: the panel goes dark, and `DIS` at connect wakes it
on an ordinary restart.

**1.0.0 shipped with:**

- [x] The HID transport proven against real hardware. Was never negotiable: a deck
      application that cannot drive the deck is not 1.0.
- [x] Text macro, run program or script, open URL — the actions every competitor ships
- [x] The repository published

Three things were added on the strength of the hardware being real, none of which
were on this list — and all three came from using it rather than building it:

- Hot-plug. `TryOpen` was called exactly once, so a deck plugged in after startup was
  never looked for again.
- `STP` after `CLE`. Clears were never committed; every clear in the application
  happened to be followed by a repaint whose first image committed it by accident.
  Clearing on shutdown was the first one with nothing after it.
- Cell calibration in the application. 100×100 is this variant's measurement, and
  several VID/PID pairs ship under the name.

**1.1.0:** folders and the page-switch key, and probably per-key colour and image.

Version comes from the git tag. The workflow passes `-p:Version` and `/DAppVersion`, so
the number in the csproj is a local-build default that never reaches an artefact — tag
as `v1.0.1` and everything follows.

---

## 1. HID transport — **hardware**

The one thing every comparable project has and we do not. Protocol is fully
documented in [docs/PROTOCOL.md](docs/PROTOCOL.md); this is implementation, not
research. Estimated 1–2 days, but it is also where the surprises live.

### Measured on the real unit, 04.08.2026

The device arrived and was enumerated. Everything below is read from the hardware,
not from the protocol notes.

```
VID_0300  PID_3010  rev 0002        serial 0300D0781510
manufacturer  HOTSPOTEKUSB
product       HOTSPOTEKUSB HID DEMO

MI_00  vendor      usage page 0xFFA0  usage 0x01
       input  report   513 bytes
       output report  1025 bytes
       feature report    0 bytes
       link collections  2

MI_01  keyboard    usage page 0x0001  usage 0x06
       input 9 bytes, output 2 bytes
```

- [x] **Enumeration tool** — written and run. `0xFFA0` confirmed as the vendor
      collection, and it is the first of the two interfaces.
- [x] **The write size is 1025, not 513.** This is the correction that matters: the
      assumption was 512 payload plus one report-ID byte. The device asks for 1024
      plus one. Windows rejects a write that is not exactly `OutputReportByteLength`,
      so every write is `[0x00][up to 1024 bytes]`. Reads are 513, which does match
      512 plus the report ID — the asymmetry is real.
- [x] **A serial number exists**, and Windows uses it in the instance ID rather than
      a port path. Identity therefore survives moving the deck to another port, which
      is better than section 9 assumed. Whether it is unique across units is unknown
      until there is a second deck; "old" units reportedly share one.
- [ ] The product string reads **"HOTSPOTEKUSB HID DEMO"** — unmodified vendor demo
      firmware on a generic Hotspot-EK USB HID controller. That string is searchable
      and is the thread to pull on for the firmware question in section 9.
- [ ] Find out what the keyboard interface (MI_01) sends. Likely the three keys that
      emulate an Elgato device. Harmless, but it means the deck can type.
- [x] **The transport works.** `LIG 5` dimmed the deck and `LIG 80` brought it back,
      on the real unit. Open the `0xFFA0` collection with read and write access — no
      exclusive-access fight — and write exactly 1025 bytes.
- [x] Packet layout in [docs/PROTOCOL.md](docs/PROTOCOL.md) is correct as written.
      With the report-ID byte in front, everything shifts by one: `"CRT"` at frame
      1–3, opcode at **6–8**, payload from **11**. Two attempts were wasted guessing
      those offsets from memory before reading the file that was already in the
      repository. Read the document first.
- [x] **Upload cost measured, and the estimate was ten times too pessimistic.**

      ```
      one cell                 1.0 ms
      full 18-cell repaint    18.0 ms      (55 fps ceiling)
      JPEG encode              0.26 ms per cell
      serialised vs not       no difference
      ```

      Thirty frames per second across all eighteen cells held for six seconds with
      nothing dropped, and looked smooth on the hardware. The README said "no
      animation, full repaint is ~0.2 s"; that constraint never existed.

      Serialising costs nothing measurable — the write lock never contends, because
      each write returns in a millisecond. Leave the queue in `DeckController` alone.
- [ ] Neither `DIS` nor `LIG` produced a reply frame. Either the device does not
      acknowledge these, or reads need a different approach than a blocking
      `ReadFile`. Less urgent now: uploads clearly do not need an ACK to be safe.
- [x] **`CLE` works, but only with an `STP` after it.** The clear sits in the device
      uncommitted otherwise. This hid for a long time: every clear in the application
      was followed by a repaint, and the first image's own `STP` committed the clear
      along with it. Clearing on shutdown was the first clear with nothing after it,
      and it did nothing at all.
- [x] **Image upload works.** An 85×85 JPEG at q90 rendered with `JpegBitmapEncoder`
      appeared on the deck. `BAT` with the size big-endian and a 1-based cell index,
      then the data, then `STP`.
- [x] **Chunks are one report's worth.** The document says 512 bytes, but that is the
      513-byte variant. Ours takes 1024 payload bytes per report and accepted a
      1348-byte JPEG as two chunks without complaint.
- [x] **Orientation is a plain 270° rotation, no mirroring.** Sent upright, the glyph
      appeared rotated 90° clockwise; pre-rotating 270° put it right, and it also
      centred correctly. The document's reduction of "90° plus H and V mirroring" to
      a single 270° turn was correct — now measured rather than trusted.
- [x] Cell `01` is the **top right** key, confirming the grid in `DeckLayout.cs` at
      least at that corner.
- [ ] **Persistence test**: upload, unplug, replug. Framebuffer or flash? Decides
      whether a 1 Hz info widget is safe.
- [x] **The whole 6×3 layout is confirmed.** A numbered image went to each of the
      eighteen cells and was read back off the deck:

      ```
      13  10  07  04  01 | 16
      14  11  08  05  02 | 17
      15  12  09  06  03 | 18
      ```

      Every position matched, including 16–18 in column 5. That was the one thing
      `PROTOCOL.md` marked VERIFY and the one place `DeckLayout.cs` said would need
      fixing if wrong. It needed no fixing.
- [x] 85×85 is accepted and renders correctly on both a key and an info cell, so the
      two are the same size.
- [x] **Cells are 100×100, measured.** Not 85, and not the 126 in AJAZZ's own manual.
      `DeckLayout.CellPixels` is the single place it is stated and everything else
      derives from it.
- [x] **Cells are persistent framebuffers.** An upload only writes where the image
      reaches; the rest keeps its previous contents. That is what the pale border and
      the vendor-logo fragments were. At the correct size a full-cell image covers it,
      so no clearing is needed in normal use — but anything that writes smaller must
      `CLE` first.
- [x] **Folded into dotStream as View → Calibrate cells**, and the standalone tool
      deleted. Better inside than out: the tool had to open the device itself, so
      dotStream had to be closed first, which meant calibrating against a test image
      instead of your own keys. Size and rotation now live in `settings.json`, so
      whoever plugs in a variant nobody measured can fix it without a rebuild.
- [ ] JPEG encode at ~q90 via `JpegBitmapEncoder`, chunked after `BAT`, wait for
      the `ACK..OK` frame before the next upload.
- [x] **Input reports decoded.** All fifteen keys pressed and captured. Frames are
      513 bytes with the header `ACK\0\0OK`, the cell index at byte 10 and the state
      at byte 11 — `0x01` press, `0x00` release.

      Two things turned out easier than the notes said. Press and release are
      **explicit**, so no edge synthesis by diffing is needed. And the firmware
      debounces cleanly: thirteen presses gave thirteen pairs, no doubles.

      The `ACK..OK` header is the input frame's own, not a separate upload
      acknowledgement — which is why listening for one after an upload found
      nothing.
- [x] **Hot-plug.** Pulling the cable never took the application down — that much was
      already true — but nothing noticed the deck coming back, so it sat dark showing
      its own boot logo until the app was restarted.

      Now the transport raises `Disconnected` once, on a failed read or a failed write,
      and the mirror lets the dead device go and looks for it every two seconds. A
      returning deck is a *new* device as far as Windows is concerned, so reconnecting
      enumerates from scratch rather than reopening anything.

      Two details worth keeping: writes that fail while the cable is out are swallowed,
      or one yanked cable mid-repaint would surface as eighteen errors when the only
      thing worth saying is said once. And reconnecting clears all eighteen cells before
      repainting — `RepaintKeys` covers 1–15, while the info cells wait for their widget
      interval, so without the clear the vendor logo sat in column five for a second.

## 2. Automatic profile switching — done

Switching to an app opens the page you built for it; switching away closes it again.
Only pages that already exist, and never over the top of manual navigation.

One thing worth remembering: a single alt-tab is several `EVENT_SYSTEM_FOREGROUND`
events. Tool windows and shell surfaces take the foreground on the way, so the events
are filtered and then debounced by 400 ms. Without that the deck opened a page and
closed it again within the same switch.

Remaining:

Packaged apps are now matched by the identifier Windows gives them rather than by their
process name. A UWP window belongs to `ApplicationFrameHost`, not to the app, so the
real process is found by walking the frame's child windows: Media Player runs as
`Microsoft.Media.Player`, is installed as `Microsoft.ZuneMusic_8wekyb3d8bbwe!…` and is
displayed as "Mediespiller" — three names, no two of which resemble each other. Desktop
programs have no such identity and still fall back to token matching.

That fixes the apps we know about. The next one will be different, and the user cannot
patch code:

- [x] **"Follow the app I was just in".** Right-click a key on a page and bind that page
      to whatever was last in front. The user *shows* us the application rather than
      describing it, so there is no syntax to learn and nothing to document. It works
      because the hook skips this process: by the time anyone reaches a menu here,
      dotStream is in front and the application they meant is not, so "last" is exactly
      right. A taught rule beats anything worked out automatically.
- [x] **View → "What's in front?"** — window title, process name and identifier, and
      whether it was recognised, taught, or matched nothing at all. Every value shown is
      one the matcher used, and there is a Copy button, so "it does not follow my app"
      becomes an observation instead of a shrug — which is what the *Report a bug* item
      in section 8 needs.
- [x] Overrides in `%APPDATA%\dotStream\matches.json`, page id → identifier, process,
      title. Kept out of `profile.json` for the same reason as the Hue key: a profile is
      meant to be shared, and which process a page follows on one machine should not
      travel to another.
- [ ] **Reconsider the default now that the hardware is here.** Following the focused
      app reads very differently on a physical deck than it did in a window: eighteen
      screens on the desk changing by themselves while you type is far more insistent
      than a panel that changed while you watched it. Nothing is wrong with the
      feature — it may simply want to be off by default, or slower, or limited to
      pages the user has actually built.
- [ ] Explicit match rules — window-title regex, so one browser can drive different
      pages depending on the site. Today the match is process name against app name.
- [ ] A per-app "never follow this one" opt-out

## 3. MCP server — done

Built and verified end to end against the simulator: an agent called `deck_ask` with
three options, the deck showed them, a key was pressed, and the agent received
`Rewrite (option 2 of 3, chosen on the deck)`.

`HttpListener` on `127.0.0.1` only — never a wildcard, and loopback needs no urlacl
and no elevation. Off by default; **File › Agent bridge (MCP)** turns it on.

The security boundary holds: `deck_set_key` draws a label and nothing else. No tool
binds a key to an action, so an agent cannot turn "Pause music" into something that
runs a program.

`deck_propose_page` closes the loop the security boundary opened. An agent asked for
"the shortcuts people use most in Excel" can now offer a whole page of hotkeys; the
deck shows them with their combinations, and green ✓ / red ✕ sit on the same page so
the decision is made while looking at what is being decided. Nothing is written until
the key is pressed. Verified: eight Excel shortcuts proposed, accepted, and present in
`profile.json`.

A proposal is answered three ways, not two: green **Accept**, red **Reject**, amber
**Later**. Later is not a refusal and the tool result says so in as many words, because
an agent told "no" will drop an idea the user actually wanted — it just was not the
moment. The single-key dialog offers the same three, or Cancel would mean "rejected"
there and "later" on the deck.

A question also holds the deck while it waits. Automatic page switching used to sweep
the proposal away: switching to the very application being asked about pushed the page
carrying Accept and Reject off the stack, leaving the request open with no way to answer
it. Pinning has a duration; a question needs a lock.

Remaining:

- [ ] Should `deck_ask` offer "Later" too? Its options belong to the caller, so a third
      key is not obviously free, but "I saw it, ask again" is different from a timeout
      there as well.
- [ ] **`deck_propose_page` switches behaviour on key count** — one key opens the
      prefilled hotkey dialog, several show accept/reject on the deck. Both are right
      for their case, but an implicit switch is a smell. Make it an explicit argument.
- [ ] Custom glyphs and icons in `deck_set_key`, not just a label and a colour
- [ ] A tool to ask for free text rather than a choice — needs an on-deck keyboard,
      or a prompt in the window with the deck used only to confirm
- [ ] Streamable HTTP with SSE, if a client ever needs server-initiated messages.
      Plain request/response covers every tool here.

## 4. Generic MCP client action — done

Drag **Call an MCP tool** onto a key: enter a URL, press Connect, and the tool list
comes from the server itself. Pick one, set arguments, test it, save. The binding
lives with the key in the profile, and right-click reopens it for editing.

Deliberately generic — dotStream knows nothing about any particular server, which is
what keeps this project free of couplings to whatever happens to be installed.

Compatibility beyond our own server: `initialize` is sent first and any
`Mcp-Session-Id` is carried on later requests, and a reply is accepted as either
plain JSON or an SSE frame.

Remaining:

- [ ] **Info-cell widget bound to an MCP tool, polled.** Not built. Needs a widget
      with per-instance configuration and a poll interval, plus a rule for turning a
      text result into something readable on 85×85.
- [ ] Saved server list, so the URL is picked rather than typed each time
- [ ] Show the tool's input schema in the dialog instead of a bare JSON box

## 5. More actions

- [x] **Hotkey** — captured by pressing it, sent with `SendInput`, stored as text like
      `Ctrl+Shift+V`. Focus is handed back to the last foreground window first, which
      only matters in the simulator: a physical key never steals focus.
- [x] **Sequence macros** — commas make one field several steps: `Alt, H, L, N` walks
      the ribbon to New Formatting Rule. 90 ms between steps, because sending them at
      once aims step two at a menu that has not opened yet. A bare modifier is a valid
      step (that is how KeyTips are raised) and the splitter leaves `Ctrl+,` alone.
      Verified end to end from an MCP proposal through to Excel opening the dialog.
- [x] **Press, hold and repeat.** The device reports both edges explicitly, so a key
      can mean one thing tapped and another held, and volume can keep moving while the
      finger stays down. Threshold 450 ms, repeat every 140 ms.

      A key with a hold action cannot fire on the way down — until the finger lifts,
      nobody knows which of the two was meant. Everything without one stays instant, so
      no ordinary key got slower.

      This is what turns fifteen keys into thirty without a single folder, and it is
      deliberately not being used for that yet. **Decided 04.08.2026: volume only.**
      A gesture spread everywhere before anyone knows where it earns its place becomes
      something the user has to learn rather than something that helps them.

      Revisit when a second real use turns up. The app key used to be the candidate,
      and is less so now: it launches every time, and additionally opens the app's page
      when the app was already running. Asking the shell to start something that is
      already started is how Windows brings an application forward, and the only thing
      that works for one sitting in the tray — Steam was the case that proved it, with
      nine processes, no main window handle on any of them, and its real window hidden
      rather than minimised.
- [ ] Per-key step delay — 90 ms is a guess that held on one machine. Make it
      configurable if a slower one needs longer.
These four are what every other deck application ships with, including the cheapest.
Nothing here is unknown; they are an evening each, and until they exist "ahead on
features" is a claim that does not survive being looked at.

- [x] **Run a program or script.** Path, arguments, working directory, and two
      switches: use the shell or not, hide the window or not. A Test button, because
      the difference between a path that works and one that nearly works does not show
      in a text box. No focus handover — unlike a hotkey this does not go through
      whatever window is in front, which is the whole reason it exists.

      Deliberately not reachable over MCP. An agent proposes hotkeys and nothing else:
      a key that types Ctrl+S and a key that starts an executable are different kinds
      of thing, and only one should be suggestible by software.
- [x] **Open a link.** A web address, a file or a folder. Technically a narrow case of
      the run action, kept separate anyway — telling somebody to configure "run a
      program" in order to open a bookmark is how software ends up feeling like it was
      written for the person who wrote it. A missing `https://` is filled in.
- [ ] **Page switch — a key that opens a page, which is also folders.** These are one
      feature, not two: a folder is a key that opens a page, with a label and an icon on
      it. `Home → Utvikling → VS Code → its hotkeys` already works structurally — the
      navigator is a stack with no depth limit and Back steps out one level at a time.
      Three small pieces are missing:
      - a cell kind that opens a page, alongside app, action, widget, hotkey and mcp
      - a way to create a named page with a label and an icon from the library
      - Back seeded automatically, the way app pages already get it

      Suggested gesture: right-click an empty key → *New page…*, asking for name and
      icon and placing the key at once. Keeps it in the same gesture as the rest of the
      editing rather than adding a menu-bar dialog.

      Known consequence: focus-following jumps straight to an app's page and does not
      rebuild `Home → folder → app`, so Back from there goes home rather than to the
      folder. Probably right — you arrived by switching applications, not by navigating
      — but it will be noticed, so decide it deliberately.

      This also answers running out of room on the home page, which holds fifteen keys.
      Folders group by meaning; paging through Home 1 / Home 2 groups by nothing. Worth
      building folders first and seeing whether paging is still missed.
- [x] **Media keys follow their page.** Transport used to act on whatever Windows calls
      the current session, so Next on a Media Player page skipped a Spotify track and
      the Now playing key showed Spotify's cover art over a running video. An app page
      now sets `MediaHub.PreferredSource`, and both the transport and the snapshot come
      from that application's session; the home page keeps the old behaviour of
      following whatever is playing. Matching is deliberately forgiving — a packaged app
      reports the identifier it was installed under, a desktop one usually its
      executable.
- [x] **Seek by a fixed distance** — Back 10 s and Forward 30 s, matching Media Player's
      own two buttons. Implemented as an absolute position rather than
      `TryFastForwardAsync`, which means whatever each application decided it means; the
      target is clamped to the seekable range. Measured first: both Media Player and
      Spotify report `IsPlaybackPositionEnabled`. Worth knowing that a video player with
      one file open reports next and previous as *unavailable*, so seeking is the pair
      that actually applies there.
- [ ] Make the seek distances configurable per key rather than fixed at 10 and 30

### Call an HTTP endpoint — **to discuss before building**

Method, URL, headers, body. One action that reaches Philips Hue, Home Assistant, OBS,
Zigbee2MQTT and anything else on the local network with an API — rather than a Hue
integration, which would be exactly the coupling to whatever happens to be installed
that this project set out not to have. Hue then becomes a *setup helper* that finds the
bridge, does the pairing and writes ordinary HTTP actions.

It also serves the agent path directly: give an LLM the bridge address and key and it
can propose a page of lighting scenes through the same propose-and-accept flow as the
Excel keys.

**Decided — Hue API v2.** Chosen for the feedback: the event stream means a key can be
lit while the lamp is on, rather than firing blind. That is the whole reason to prefer
it, and it is worth the cost — v2 speaks HTTPS with a certificate signed by Philips'
own root, with the bridge ID as its common name.

- [ ] Decide how that certificate is handled. Pinning the bridge ID, or trusting the
      Philips root, are both defensible. Disabling validation is not, and is the path of
      least resistance, so write down which one was chosen and why.

**Decided — credentials are encrypted at rest, entered through a password field.** Same
approach as the AFRY OPC UA connector, which moved its passwords out of the source and
into Windows' own protected storage.

- [ ] Windows Credential Manager or DPAPI. Credential Manager is a real store the user
      can see and revoke; DPAPI is a per-user encrypt-this-blob call and less ceremony.
      The OPC UA connector set the precedent with Credential Manager.
- [ ] It follows that `profile.json` holds only a **reference** to the credential, never
      the key itself — which keeps profile export and import safe by construction rather
      than by remembering to strip something.

Pairing already suits the project: the bridge refuses to issue a key until its physical
button is pressed. Human in the loop, enforced by hardware.

Still open — **Thomas is thinking about these two**:

- Does a response ever need to reach the key face — a temperature, a state — or is
  fire-and-forget enough for the first cut? (Note that choosing v2 for its feedback
  points at yes, at least eventually.)
- An action that can call any URL is also an action that can exfiltrate. It is the user's
  own machine and their own key, but it deserves a deliberate decision rather than
  arriving as a side effect.

## 6. Editor polish

- [ ] Per-key **background colour and custom icon file** — the same colour editor the
      widgets already have, pointed at a key. Label text is done.
- [ ] **A default cell background the user picks.** Black is the default today and
      should stay it: it is what the physical bezel and the unlit gaps look like, so
      anything else makes the deck read as a grid of tiles rather than one surface.
      But it is a preference, and somebody will want a dark blue or a plain white deck.
      Belongs next to the label-rendering panel, which is where the other visual
      defaults already live.
- [ ] More than one sub-page per app
- [x] **Reorder by dragging between cells** — drag a key onto another and they swap;
      onto an empty one and it moves. This forced the press to move from mouse-down to
      mouse-up: firing on the way down meant a key about to be dragged had already sent
      its hotkey into whatever had focus. Simulator only — a physical switch has nothing
      to do with the mouse.
- [ ] **Search box in the icon picker** — 73 icons in a grid, found by scrolling and
      hovering for a tooltip. Fine at 40, not at 73.
- [ ] Drag widgets between the info cells (16–18); today they are right-click only
- [ ] Profile import/export
- [ ] Undo for clear and reassign

## 7. Widgets

Eight are built: CPU, memory, GPU, video memory, disk free, network, clock, uptime.
All read through plain P/Invoke or PDH counters — no package, no driver, no elevation.

- [ ] **Temperatures, fan speeds and power draw.** Windows exposes no public API;
      the ACPI thermal zone is empty on most desktops. The only route is
      LibreHardwareMonitorLib, which loads a kernel driver and needs administrator
      rights — and that costs the per-user, no-UAC install that
      `installer/dotstream.iss` deliberately chose. If it happens, it should be an
      opt-in mode the user turns on knowingly, not a default.
- [ ] CPU per core, CPU clock speed
- [ ] Disk I/O rate as well as free space
- [ ] Media progress ring
- [ ] Calendar / next meeting
- [ ] Drag widgets from a palette as well as assigning them from the cell menu

## 8. Distribution

Build and packaging are done — `release.yml` produces installer, portable zip and
checksums from a version tag. What remains is reducing the friction of the download
itself. **Do none of this before the transport works**; a release that cannot drive
the hardware invites "does it actually work?" as issue #1.

- [x] Publish the repository (Apache-2.0, `LICENSE` and `CREDITS.md` are ready)
- [x] Set `AppUrl` in `installer/dotstream.iss` to the real repository URL
- [ ] **Apply to [SignPath Foundation](https://signpath.org/)** for free code signing.
      Requires a public repo, an OSI licence and CI-produced builds — all satisfied.
      Takes an application, so start early. Note it is OV, not EV: it removes
      "Unknown publisher" and builds reputation per *publisher* rather than per
      binary hash, but only EV clears SmartScreen instantly, and EV is not free.
- [ ] **WinGet manifest** — PR to `microsoft/winget-pkgs`. Free, and users who
      install this way never meet the SmartScreen dialog at all. The best download
      experience available without paying for anything.
- [ ] **Scoop bucket** — trivial, own repo, reaches the developer audience
- [ ] Help menu: *Report a bug* / *Request a feature*, opening a prefilled GitHub
      issue with version, Windows build, .NET version, transport in use, detected
      VID/PID and the last error. Plus *Copy diagnostics* for people who would
      rather email. Blocked on the repository URL existing.
- [ ] Auto-update check (read the Releases atom feed, notify only — never
      self-update silently)

## 9. Talking to applications properly

Hotkeys work with everything and that is why they are the default. But they are blind:
they fire and hope. Some applications expose a channel that also reports state, which
is what a key that *lights up when muted* would need. Any of this belongs behind an
optional provider — never replacing the hotkey path.

**Office — COM.** Measured against a running Excel, not assumed:
`Marshal.GetActiveObject("Excel.Application")` attaches; `Selection.Address()` and
`ActiveWorkbook.Name` read fine; `CommandBars.GetEnabledMso("WrapText")` returned True
and **`GetPressedMso("WrapText")` returned False** — toggle state is readable.

- [ ] **Test `ExecuteMso` in an empty workbook.** `Get-Member` did not list it, but COM
      does not enumerate late-bound members, so that is not evidence. Verify before
      building on it — remembering that `GetImageMso` failed with `E_UNEXPECTED` when we
      tried to pull ribbon icons, so this surface is not uniformly reliable.
- [ ] If it works: ribbon commands by name, locale-independent, no KeyTip walking
- [ ] Toggle-aware keys — Wrap Text lit when the selection has it

Office.js add-ins were considered and set aside: one add-in per Office application,
alive only while loaded, manifest sideloading or AppSource review, and a second codebase
in JavaScript. The only thing they buy over COM is Excel on the web and on Mac.

**Discord — RPC over a named pipe.** `\\.\pipe\discord-ipc-0` exists while Discord runs;
confirmed on this machine. The API can set mute and deafen, switch voice channel, and
report who is speaking.

What the public RPC surface offers, if the scopes are granted:

| command | gives |
|---|---|
| `SET_VOICE_SETTINGS` / `GET_VOICE_SETTINGS` | mute, deafen, in/out volume, input device, PTT vs voice activation |
| `SELECT_VOICE_CHANNEL` | join or leave a channel — a key per channel |
| `SET_USER_VOICE_SETTINGS` | per-person volume and local mute |
| events `VOICE_SETTINGS_UPDATE`, `SPEAKING_START`/`STOP`, `VOICE_CONNECTION_STATUS` | **state back** |

The events are the real prize: a mute key that is lit when actually muted, or a cell
showing who is speaking. No hotkey can do either.

Go Live, screen share and camera are **not** in the public documentation as far as we
know. Elgato's plugin has them, but Elgato is a partner — that may mean a private API or
a scope ordinary applications are not given. Do not promise these.

- [ ] **Settle the whitelist question.** The `rpc`, `rpc.voice.read` and
      `rpc.voice.write` scopes are believed to require Discord to approve the
      application, which is why almost no third-party tool does this. Not verified.
      Register an application in the Developer Portal, handshake over the pipe, attempt
      `AUTHORIZE` with `rpc.voice.write`, and see whether it is granted or refused. Ten
      minutes, and the answer decides whether the route is worth anything.
- [ ] Meanwhile: a Discord page built on **global keybinds**. `Ctrl+Shift+M` and
      `Ctrl+Shift+D` are Discord's defaults but only fire when Discord has focus - which
      is precisely not when you want mute. Register a global keybind inside Discord
      (`Ctrl+Alt+F9`, or F13–F24) and have the key send that instead.

**OBS Studio — `obs-websocket`.** The reason stream decks exist in the first place, and
the only integration here that ordinary buyers already expect. Every rebrand of this
hardware advertises "OBS ready" on the box, so its absence is the first thing a streamer
will notice about dotStream.

Built into OBS since version 28, so nothing to install. JSON over WebSocket on port 4455,
SHA256 challenge-response for the password, and it reports state as well as accepting
commands — which puts it in the same category as Discord's RPC events rather than the
blind hotkey path.

- [ ] Scene switching, with the **current scene lit**. `GetCurrentProgramScene` plus the
      `CurrentProgramSceneChanged` event. A key that shows which scene is live is the
      whole point; one that only switches is a hotkey with extra steps.
- [ ] Start and stop recording and streaming, lit while active, with the recording timer
      as a candidate for an info cell
- [ ] Mute toggles per audio source, lit when muted. `SetInputMute` and the
      `InputMuteStateChanged` event
- [ ] Scene item visibility — show or hide an overlay, a camera, a browser source
- [ ] Virtual camera and replay buffer, since both are one call each
- [ ] Populate a page from OBS itself. `GetSceneList` returns the scenes, so a proposed
      page of real scene keys beats making somebody type them. Same shape as
      `deck_propose_page` already uses.

**vMix — HTTP.** Simpler than OBS and worth doing at the same time, since the action
model is identical. A GET to `http://localhost:8088/api/?Function=Cut&Input=2` does the
work, and the same endpoint without a function returns the full state as XML, so lit
keys are available here too.

- [ ] Cut, fade and transition to an input, with the active input lit
- [ ] Recording, streaming and multicorder toggles
- [ ] Overlay on and off

Both belong behind the same optional-provider rule as Office and Discord. Neither should
replace the hotkey path, and neither should be reachable in the palette when the
application is not running - an OBS page on a machine with no OBS is a page of keys that
silently do nothing.

## 10. Motion, now that it turns out to be free

The deck sustains thirty frames per second across all eighteen cells, measured. That
was assumed impossible and is not. Worth using sparingly — constant movement is what
makes a dashboard unreadable — but these have earned a look:

- [ ] A key that is *doing something* should say so: a spinner while a script runs, a
      ring while an agent works. Today a long action looks identical to a dead key.
- [ ] Media progress as a sweeping ring rather than a number
- [ ] A brief press animation, so a key that did nothing is distinguishable from one
      that never registered the press
- [ ] Transitions on page change

## 11. A phone or tablet as a second surface

The GameGlass idea: a touchscreen that becomes another control panel. Architecturally
this is **a third transport** and almost nothing else — `IDeckTransport` already has
two implementations and `MirroringTransport` already proves several can run side by
side. A browser takes the same cell images and sends back the same key indices; pages,
actions, hotkeys and the MCP layer never learn the difference. The HTTP server exists
too, serving MCP on 8787.

- [ ] **Pairing has to come first, and it is the only genuinely hard part.** Reaching a
      phone means binding to the network instead of `127.0.0.1`, and at that moment
      there is a control surface for this machine open on the LAN — one that presses
      keys, runs programs and sends hotkeys. Everything so far has been loopback for
      exactly that reason. Needs a one-time code shown on the PC, a token stored on the
      phone, and a way to revoke it.
- [ ] **Mirror first, decide later whether it may be more.** A screen is not bound to
      fifteen keys at 100×100 — GameGlass has sliders, dials and large panels. Mirroring
      keeps one thing to build and one thing to learn; letting the web surface grow its
      own controls makes it a better tool and a second product to maintain. Start by
      mirroring and see what people actually reach for.
- [ ] Cell images are already JPEG. Pushing them over a WebSocket and posting an index
      back is most of the work.

## 12. Nice to have

- [ ] AI-generated SVG glyphs — describe the button, get an icon in the house style.
      No image model needed; an 85×85 monochrome glyph is well within text generation.
- [ ] Natural-language profile authoring (we own the JSON schema, so this is cheap)
- [ ] Start with Windows
- [ ] Start minimised to tray
- [ ] **Multiple devices at once.** Nothing happens today: there is no HID transport, so
      a second deck is invisible. Measured on this machine, **20 of 20 HID devices are
      identified by port path and none reports a serial number** — not the keyboard, not
      the mouse, not the Logitech hardware. Expect the same of the deck, and note that
      "old" AKP153E units are reported to ship a shared serial anyway.

      What that means:
      - Two decks can always be told apart *while both are connected* — Windows derives
        a unique instance path from the hub and port.
      - Which one is which cannot be known across a replug into a different port. So do
        not deduce it: draw a large **1** and **2** on them and let the user say. Same
        principle as "follow the app I was just in" — the human points, we stop guessing.
      - An unknown device path means an unknown deck: ask again rather than quietly
        applying someone else's pages.

      Two things are cheap now and expensive later:
      - [ ] `DeckKeyEventArgs` carries only a protocol index, so a press has no sender.
            Adding identity later means touching every handler it flows through.
      - Note: the measured unit **does** report a serial number (`0300D0781510`), and
        Windows uses it in the instance ID instead of a port path. So identity survives
        a move to another port after all. The open question is no longer whether an
        identifier exists, but whether two units ship the same one.
      - [ ] The profile knows one deck. If decks are ever to hold different pages it
            needs a device dimension, and adding one after 1.0 means migrating profiles
            that already exist — the expensive kind of change.

      `DeckController` is already fine: per-instance transport, hashes and worker, no
      shared state. Two would coexist untouched.

---

## Done

- Cell model, 6×3 layout, protocol index mapping
- Renderer: gradients, icons, vector glyphs, gauges, label scrim, pixel hashing
- Icon extraction from `shell:AppsFolder` at 256×256 with real alpha
- Upload controller: dirty-tracking, coalescing, priority queue
- Simulator transport with realistic per-upload latency
- Page stack with drill-down and Back
- Action palette with drag & drop, contextual to the current page
- Media session integration: transport, live play state, album art
- App selection dialog + persistence
- Profile persistence (pages and key assignments)
- Tray icon, close-to-tray, deliberate exit
- File/Help menus, About, dark title bars
- Editable key labels, remembered against the app rather than the key
- Clear a key, reset the whole layout
- Label rendering panel: font, weight, formatting mode, antialiasing, size — live,
  saved, and needed again once the panel is in hand
- Atkinson Hyperlegible embedded; user fonts from `%APPDATA%\dotStream\fonts`
- Eight info widgets, assignable per cell, with per-cell colour editing
- Automatic page switching on the focused app, with manual navigation pinning
- MCP server: `deck_ask`, `deck_notify`, `deck_set_key`, `deck_status`,
  `deck_propose_page`
- Generic MCP client action — a key that calls someone else's tool
- Hotkey action, and a palette that hides what a page cannot use
- Sequence macros, and `deck_propose_page` able to merge into a page that already
  exists rather than only creating new ones
- Placement is drag & drop only. Selecting in the palette used to arm the next key
  press, which meant a click on another page was silently eaten as an assignment —
  a hotkey defined for Excel landed on top of an app on the home page that way.
- Edit and remove hotkeys from a page's palette; defining one used to be a one-way door
- Console: every key press, agent request and outbound call, split by direction
