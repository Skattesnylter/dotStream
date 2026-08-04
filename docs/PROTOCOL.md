# AJAZZ AKP153E — wire protocol

Distilled from the open-source reverse-engineering work listed at the bottom.
Nothing here has been verified against hardware yet — the device is in the post.
Everything marked **VERIFY** is a hypothesis.

---

## 1. Device identity

The AKP153E is a Mirabox HSV293S OEM. Sources disagree on USB IDs, most likely
because of firmware revisions and regional batches:

| Source | VID:PID |
|---|---|
| ajazz-control-center docs | `0300:1010` |
| Bitfocus companion issue #7 | `0300:3010` |
| PhV-80 (built against a real AKP153E) | `260d:1125` |
| ajazz-sdk (Mirabox V1) | `5548:6674` |

**Do not hardcode.** Enumerate, match on the vendor-defined HID usage page and
let the user confirm. A Temu-sourced unit may well report a fifth combination.

Hardware IDs reported on Windows for the `0300:1010` variant:

```
HID\VID_0300&PID_1010&REV_0300
HID\VID_0300&PID_1010
HID\VID_0300&UP:FFA0_U:0001
```

## 2. Two Windows gotchas that will eat a day each

1. **Report ID prefix.** Every write must be prefixed with a `0x00` report-ID
   byte, so a 512-byte packet is **513 bytes on the wire**. Get this wrong and
   writes fail silently.

2. **Pick the right HID collection.** The device exposes several. Only the
   vendor-defined usage page **`0xFFA0`** accepts these commands. Opening the
   first enumerated collection appears to succeed and then does nothing.

These two are the most likely reason the `PhV-80/ajazz-controller` project
stalled on a "Windows HID access problem".

## 3. Packet format

All packets are exactly 512 bytes, zero-padded.

```
Offset  Content
0–2     "CRT"  (0x43 0x52 0x54)
3–4     0x00 0x00
5–7     3-byte ASCII command
8–9     0x00 0x00
10–511  payload, zero-padded
```

Frames not starting with `CRT` are silently dropped by the firmware.

## 4. Commands

| Cmd | Bytes | Purpose |
|---|---|---|
| `DIS` | `44 49 53` | Display init |
| `LIG` | `4C 49 47` | Brightness — byte 10 = 0–100, firmware clamps above |
| `BAT` | `42 41 54` | Announce JPEG: bytes 10–11 = size big-endian, byte 12 = 1-based cell index |
| `STP` | `53 54 50` | Flush / commit |
| `CLE` | `43 4C 45` | Clear — see tag table |
| `LOG` | `4C 4F 47` | Boot logo (shown during power-up, **not** the info cells) |
| `HAN` | `48 41 4E` | Sleep |
| `CONNECT` | ASCII | Keep-alive |

### CLE tags

| Byte 10 | Byte 11 | Effect |
|---|---|---|
| `0x00` | `0x01`–`0x0F` | Clear one cell |
| `0x00` | `0xFF` | Clear all |
| `0x44` | `0x43` | Restore vendor logo |

## 5. Cell layout — 6×3, eighteen cells

**This is the key insight for this project.** The "side screen" the vendor
markets is *not* a separate 854×480 panel. The device is an 18-cell 6×3 grid
where the last column has no switches under the LCDs:

```
13  10  07  04  01 | 16
14  11  08  05  02 | 17
15  12  09  06  03 | 18
     keys 1–15     | info cells 16–18
```

Consequences:

- Live info display uses the **same `BAT` command** as the keys. No separate
  opcode, no USB sniffing required.
- No flash-wear risk. Key cells are volatile framebuffers, so a 1 Hz CPU widget
  is harmless. (`LOG` writes the persistent boot logo — leave it alone.)
- Three cells at ~5 kB each is ~20 packets, not the ~160 a full-panel upload
  would have cost.

**VERIFY:** the key numbering comes from the ZCube gist and pyajazz. The mapping
of 16–18 onto column 5 is inferred from product photos plus the "18-position 6×3
grid, `0x10`–`0x12` lack physical buttons" note. Encoded in exactly one place:
`DeckLayout.cs`.

## 6. Images

- **85×85 px, JPEG, ~q90.** VERIFY — the info cells may not match the keys.
- Sent in 512-byte chunks after the `BAT` header; final chunk zero-padded.
- Device replies with an ACK frame: `41 43 4B 00 00 4F 4B` — ASCII `ACK..OK`.
- **Uploads must be serialised.** Starting a new image before the ACK arrives
  corrupts the target cell. This is why `DeckController` exists.
- Metadata matters: pyajazz reports the device dislikes EXIF. WPF's
  `JpegBitmapEncoder` writes minimal metadata, so this should be free.

### Orientation

Documented as "90° rotation plus horizontal and vertical mirroring". Note that
H+V mirroring *is* a 180° rotation, so the whole thing reduces to a plain **270°
rotation** — which suggests whoever wrote that was describing something they had
not fully worked out.

Do not trust it. Brute-force all 8 orientations with an asymmetric test glyph
(a large "F") and keep whichever renders upright. Five minutes, zero guessing.

## 7. Input reports

```
Byte 0–8 : 0x00
Byte 9   : cell index 1–15, or 0x00 for no event
Byte 10+ : 0x00 padding
```

- One frame per transition, **no press/release discriminator** on this protocol
  version. Edges must be synthesised by diffing successive frames.
- Frames shorter than 16 bytes are invalid and must be discarded.
- Cells 16–18 have no switch and never appear here.

## 8. Timing budget

An 85×85 JPEG at q90 is roughly 3–6 kB, i.e. 6–12 packets plus the ACK
round-trip. At HID interrupt rates that is a few milliseconds per cell, so:

- Full 18-cell repaint: ~0.2 s. Fine for a page switch.
- Animation: **no.** Do not try. Static icons that change on state are the
  design point, and it is why the vendor software feels sluggish.

Keys and info cells share one pipe, so info-cell refreshes must be lower
priority than anything the user is waiting on.

---

## Sources

| Project | Licence | What it gives |
|---|---|---|
| [ajazz-control-center](https://github.com/Aiacos/ajazz-control-center) `docs/protocols/streamdeck/` | docs | Clean-room protocol write-ups, device matrix |
| [mishamyrt/ajazz-sdk](https://github.com/mishamyrt/ajazz-sdk) | MPL-2.0 | USB ID and geometry catalogue, opcodes |
| [4ndv/mirajazz](https://github.com/4ndv/mirajazz) | MPL-2.0 | Protocol version taxonomy, write path |
| [superdeee/pyajazz](https://github.com/superdeee/pyajazz) | MIT | 18-position layout, rotation, metadata stripping |
| [Uriziel01/Ajazz-AKP153-reverse-engineering](https://github.com/Uriziel01/Ajazz-AKP153-reverse-engineering) | — | Raw BAT upload captures |
| [ZCube gist](https://gist.github.com/ZCube/430fab6039899eaa0e18367f60d36b3c) | — | First byte-level description; the 6×3 / 18-cell note |
| [4ndv/opendeck-akp153](https://github.com/4ndv/opendeck-akp153) | **GPL-3.0** | Device list — read the docs, not this code |

**Licence hygiene:** if this ever goes public, keep to the MPL/MIT sources and
the clean-room documentation. Protocol *facts* are not copyrightable, but do not
transliterate GPL-3.0 Rust into C#.

## Capture notes, if sniffing ever becomes necessary

USBPcap does not work properly against USB 3.0 ports. Plug the device into a USB
2.0 port, or through a USB 2.0 hub, before capturing.
