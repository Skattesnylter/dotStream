# dotStream — TODO

Ordered roughly by what unblocks the most. Anything marked **hardware** waits for
the AKP153E to arrive.

---

## Releases

Currently **0.9.0**. It was 0.1.0 long after that stopped being true, which mattered
because the version number is what people read before downloading — and media
integration, MCP in both directions, sequence macros, eight widgets, persistence,
focus-following, an installer and a build pipeline is not a prototype.

**1.0.0 needs:**

- [ ] The HID transport proven against real hardware. Not negotiable: a deck
      application that cannot drive the deck is not 1.0.
- [ ] Text macro, run program or script, open URL — the actions every competitor ships
- [ ] The repository published

**1.1.0:** folders and the page-switch key, and probably per-key colour and image.

Version comes from the git tag. The workflow passes `-p:Version` and `/DAppVersion`, so
the number in the csproj is a local-build default that never reaches an artefact — tag
as `v0.9.1` and everything follows.

---

## 1. HID transport — **hardware**

The one thing every comparable project has and we do not. Protocol is fully
documented in [docs/PROTOCOL.md](docs/PROTOCOL.md); this is implementation, not
research. Estimated 1–2 days, but it is also where the surprises live.

- [ ] **Enumeration tool** — list every HID collection with VID/PID/usage page and
      report lengths. Four candidate VID/PID pairs are in circulation; a Temu unit
      may well report a fifth. Do not hardcode.
- [ ] Open the **`0xFFA0`** vendor collection, not the first one enumerated.
- [ ] Writes prefixed with a `0x00` report-ID byte — **513 bytes**, not 512.
- [ ] `DIS` → `LIG 50` → `CLE` to prove the transport end to end.
- [ ] **Orientation brute-force**: render an asymmetric glyph, try all 8
      transforms, keep the one that comes out upright. Do not trust the
      "90° + mirror" note in the docs.
- [ ] **Persistence test**: upload, unplug, replug. Framebuffer or flash? Decides
      whether a 1 Hz info widget is safe.
- [ ] Verify cell resolution — 85×85 documented, info cells 16–18 may differ.
- [ ] Verify the info cells really are `0x10`–`0x12` in column 5. If not, only
      `DeckLayout.cs` needs changing.
- [ ] JPEG encode at ~q90 via `JpegBitmapEncoder`, chunked after `BAT`, wait for
      the `ACK..OK` frame before the next upload.
- [ ] Input report parsing: byte 9, synthesise press/release edges by diffing
      successive frames, discard frames shorter than 16 bytes.
- [ ] Hot-plug: detect connect/disconnect, fall back to the simulator.

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
- [ ] Per-key step delay — 90 ms is a guess that held on one machine. Make it
      configurable if a slower one needs longer.
These four are what every other deck application ships with, including the cheapest.
Nothing here is unknown; they are an evening each, and until they exist "ahead on
features" is a claim that does not survive being looked at.

- [ ] Text macro
- [ ] Run program or script
- [ ] Open URL, file or folder
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

- [ ] Publish the repository (Apache-2.0, `LICENSE` and `CREDITS.md` are ready)
- [ ] Set `AppUrl` in `installer/dotstream.iss` to the real repository URL
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

## 10. Nice to have

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
