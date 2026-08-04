# Credits

dotStream contains no third-party source code. It has **no NuGet dependencies** —
everything outside the .NET runtime, the Windows API, one embedded font and one icon
set is written for this project.

## Bundled icons

**[Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)** —
MIT Licence, © 2020 Microsoft Corporation.

Sixty-nine icons are compiled into `src/DotStream.Rendering/FluentIcons.g.cs` as path
data by `tools/fetch-fluent-icons.ps1`. The MIT licence carries no trademark clause
and permits redistribution, which is what makes shipping them possible at all.

They are drawn on a 24×24 grid — the same one `CellRenderer` uses — so the path data
is used unchanged.

Note the contrast with the icons in `SHELL32.dll` and `imageres.dll`: those are also
Microsoft's, but not licensed for redistribution. dotStream can *show* them because it
reads them from the user's own installation at runtime and stores only a path and an
index. It never ships a copy.

## Bundled font

**[Atkinson Hyperlegible](https://www.brailleinstitute.org/freefont/)** — SIL Open
Font Licence 1.1, © Braille Institute of America. The licence text ships alongside
it in `src/DotStream.App/Resources/fonts/OFL.txt`.

Drawn to maximise the distinction between letterforms that are easily confused,
which is exactly what an 85×85 pixel key seen at a desk angle needs. Selectable at
runtime alongside Verdana, Segoe UI Variable Small and Tahoma.

What it does owe is knowledge. The AKP153 protocol is not documented by the
manufacturer, and dotStream would have needed weeks of USB packet capture without
the people below, who did that work first and published it.

None of the code from these projects has been copied, translated, or adapted. They
are credited as **sources of protocol knowledge** — facts about a wire format, which
are not themselves copyrightable.

## Protocol research

| Project | Licence | What it contributed |
|---|---|---|
| [Aiacos/ajazz-control-center](https://github.com/Aiacos/ajazz-control-center) | docs | Clean-room protocol write-ups and the device matrix |
| [mishamyrt/ajazz-sdk](https://github.com/mishamyrt/ajazz-sdk) | MPL-2.0 | USB identifier and geometry catalogue, opcode names |
| [4ndv/mirajazz](https://github.com/4ndv/mirajazz) | MPL-2.0 | Protocol version taxonomy, packet framing details |
| [superdeee/pyajazz](https://github.com/superdeee/pyajazz) | MIT | The 18-position layout, image rotation, JPEG metadata behaviour |
| [Uriziel01/Ajazz-AKP153-reverse-engineering](https://github.com/Uriziel01/Ajazz-AKP153-reverse-engineering) | — | Raw capture of the image upload sequence |
| [ZCube protocol gist](https://gist.github.com/ZCube/430fab6039899eaa0e18367f60d36b3c) | — | First published byte-level description, and the 6×3 / 18-cell insight |
| [4ndv/opendeck-akp153](https://github.com/4ndv/opendeck-akp153) | GPL-3.0 | Device and rebadge list only — **no code was read or used** |
| [OpenActionAPI/rust-elgato-streamdeck](https://github.com/OpenActionAPI/rust-elgato-streamdeck) | MPL-2.0 | Upstream wire-format conventions |

Full technical detail, including which claims are still unverified against
hardware, is in [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Platform

- **.NET** and **WPF** — MIT, © Microsoft Corporation
- Windows APIs used directly: the Shell (`IShellItem`, `IShellItemImageFactory`),
  GDI, DWM, and `Windows.Media.Control` for media sessions

## Trademarks

AJAZZ, Mirabox, Stream Deck and Elgato are trademarks of their respective owners.
dotStream is an independent project and is not affiliated with, authorised by, or
endorsed by any of them. Those names appear only to describe which hardware this
software works with.
