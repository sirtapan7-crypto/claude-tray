<div align="center">

# 🟧 Claude Code Tray

**A native Windows tray monitor for your Claude Code usage — at a glance, always on.**

A WinForms (`NotifyIcon` + GDI+) rewrite that lives **only in the Windows tray** and
shows your **rate-limit usage percentage** as a crisp, DPI-aware icon.

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-WinForms%20%2B%20GDI%2B-239120?logo=csharp&logoColor=white)

<img src="docs/tooltip.png" alt="Tray icon with usage tooltip" width="46%">
&nbsp;&nbsp;
<img src="docs/menu.png" alt="Right-click menu" width="46%">

</div>

---

Why .NET instead of Python: the number is drawn as a **vector** (`GraphicsPath`,
with an outline), **at the actual size** the tray requests (`SM_CXSMICON`) and with
**DPI awareness** (`PerMonitorV2`). No downscaling a 64px bitmap — the number stays
crisp, especially on 125–200% displays (20–32px icons).

## Look

- Background: Claude clay/coral `#D97757`
- **Vertical blue fill bar** (Task Manager style) rises from the bottom up,
  proportional to usage (50% = bottom half blue; 100% = whole tile blue)
- **3D bevel border**: light highlight on the top/left and shadow on the bottom/right → relief
- Number: large digits, white with a **dark outline** (readable at any size)
- ≥90%: the background flashes
- Amber = API error · gray = connecting

Tooltip (hover): 5h session, 7d week, extra usage, countdown to reset and status.

## Data source

A minimal call to the Anthropic API (Haiku, 1 token) every 5 min reads the
`anthropic-ratelimit-unified-*` headers, using the OAuth token Claude Code keeps in
`~/.claude/.credentials.json`. No extra configuration.

## Requirements

- Windows 10/11
- .NET 10 SDK (to build) — the self-contained `.exe` does not require .NET to be installed to run
- Claude Code installed and logged in (run `claude` at least once)

## Build and run

```
dotnet run -c Release            # build and run
```

### Produce a single .exe (self-contained, no dependencies)

```
dotnet publish -c Release
```

The executable is emitted at `bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe`.
It can be copied anywhere and runs without .NET installed.

### Start with Windows

Three ways, from simplest to most complete:

1. **From the app menu** (recommended): right-click the icon → **Start with Windows**.
   Writes/removes a key under `HKCU\…\Run` pointing to the current `.exe`. No admin.
2. **Installer** (see below): check "Start with Windows" during installation.
3. **Manual**: `Win + R` → `shell:startup` → create a shortcut to `ClaudeTray.exe`.

### Build the installer (Inno Setup)

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php).

```
dotnet publish -c Release
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
```

Produces `dist\ClaudeTray-Setup.exe` — a per-user install (no admin) at
`%LocalAppData%\ClaudeTray`, with a Start Menu shortcut, an autostart option and an
uninstaller. The script is [installer.iss](installer.iss).

## Menu (right-click the icon)

- **Show on icon** — Session 5h / Week 7d / Extra
- **Refresh now** — immediate API read
- **Start with Windows** — toggle the `HKCU\…\Run` autostart entry
- **Quit**

## Structure

| File | Responsibility |
|---|---|
| `Program.cs` | entry point, `ApplicationContext`, tray icon, menu, poll/flash timers |
| `ApiClient.cs` | reads credentials, calls the API, parses the rate-limit headers |
| `IconRenderer.cs` | draws the icon with GDI+ (vector + outline) at the actual size |

> Dev tip: `dotnet run -- --render <dir>` dumps sample PNGs at 16/20/32 px
> for visual inspection.

## Troubleshooting

- **Gray icon** → still connecting; wait for the first call.
- **Amber icon / "API error" tooltip** → token may have expired. Run `claude` in the terminal.
