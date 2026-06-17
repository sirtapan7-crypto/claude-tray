<div align="center">

<img src="docs/logo.png" alt="Claude Code Tray logo" width="120">

# Claude Code Tray

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
- **Vertical fill bar** (Task Manager style) rises from the bottom up, proportional to usage
  (50% = bottom half; 100% = whole tile). **Blue** normally; turns **vivid red** when the
  projection says usage will hit 100% **before** the window resets (see below)
- **3D bevel border**: light highlight on the top/left and shadow on the bottom/right → relief
- Number: large digits, white with a **dark outline** (readable at any size)
- ≥90%: the background flashes
- Amber = API error · while connecting (before the first reading) it shows the **app logo**

The **app icon** (`.exe`, installer, shortcuts) is the same clay tile with a white spark mark —
generated as a multi-resolution `.ico` from the same GDI+ renderer (`ClaudeTray.ico`).

Tooltip (hover): 5h session, 7d week, extra usage, countdown to reset, the **projected
time to 100%**, and status.

## Projection (observability)

Beyond the current percentage, the app tracks the **burn rate** — how fast usage is
climbing. It keeps a short rolling history of utilization samples per window, estimates
the slope by least-squares regression, and projects when usage would reach 100%:

- **on track** — at the current pace, usage stays under 100% until the window resets → the
  fill bar stays its normal blue (no extra signal)
- **danger** — at the current pace, usage hits 100% *before* the reset (you'll run out early)
  → the fill bar turns **vivid red**

The bar color and the tooltip's projected time follow whichever metric you have **Show on
icon** set to (session 5h, week 7d, or extra). It kicks in after a couple of polls (~5–10 min),
once there is enough history to trust the trend; resets are detected and clear the history.

## Usage insights (last 24h)

<div align="center">
<img src="docs/usage.png" alt="Usage insights submenu" width="70%">
</div>

The right-click menu has a **Usage insights (24h)** submenu computed locally from your
Claude Code session transcripts (`~/.claude/projects/**/*.jsonl`) — no API call. Tokens are
weighted by per-model price (Opus/Sonnet/Haiku/Fable) so each percentage reflects share of
*usage*, not just request count:

- **Last 24h** — request and session counts
- **From subagents** — share of usage from subagent (sidechain) requests
- **>150k context** — share of usage from requests with a large prompt context
- **By model** — top models by share of usage

Only token counts, model ids, and flags are read — never message content. The scan is
bounded to files touched in the last 24h and runs in the background (refreshed on each poll).

## Data source

A minimal call to the Anthropic API (Haiku, 1 token) every 5 min reads the
`anthropic-ratelimit-unified-*` headers, using the OAuth token Claude Code keeps in
`~/.claude/.credentials.json`. No extra configuration. The usage-insights submenu instead
reads the local session transcripts (see above).

## Credentials (setup)

There is **nothing to configure manually** — the app reuses Claude Code's own credentials.
On each poll it reads the OAuth token from:

```
%USERPROFILE%\.claude\.credentials.json
```

looking up the `claudeAiOauth.accessToken` field. That file is created and refreshed
automatically by Claude Code when you log in. There is no API key to paste and no
environment variable to set.

1. **Install Claude Code** (if you don't have it yet).
2. **Log in at least once** by running `claude` in a terminal — this writes
   `~/.claude/.credentials.json` with the OAuth token.
3. **Run the tray app** — it finds the file on its own (see
   [Build and run](#build-and-run)).

If you want to confirm the token exists, check that
`%USERPROFILE%\.claude\.credentials.json` contains `claudeAiOauth.accessToken`. When the
token expires the icon turns amber — just run `claude` again to refresh it (see
[Troubleshooting](#troubleshooting)).

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
- **Usage insights (24h)** — local cost breakdown from session transcripts (see below)
- **Refresh now** — immediate API read
- **Start with Windows** — toggle the `HKCU\…\Run` autostart entry
- **Quit**

## Structure

| File | Responsibility |
|---|---|
| `Program.cs` | entry point, `ApplicationContext`, tray icon, menu, poll/flash timers |
| `ApiClient.cs` | reads credentials, calls the API, parses the rate-limit headers |
| `BurnTracker.cs` | tracks utilization history, estimates the burn rate, projects exhaustion |
| `UsageInsights.cs` | aggregates the last 24h of session transcripts into a cost-weighted breakdown |
| `IconRenderer.cs` | draws the icon with GDI+ (vector + outline + projection dot) at the actual size |

> Dev tips: `dotnet run -- --render <dir>` dumps sample PNGs at 16/20/32 px for visual
> inspection; `dotnet run -- --insights` prints the 24h usage breakdown to the console;
> `dotnet run -- --makeicon ClaudeTray.ico` regenerates the app icon (multi-resolution `.ico`).

## Troubleshooting

- **Logo icon (spark)** → still connecting; wait for the first call.
- **Amber icon / "API error" tooltip** → token may have expired. Run `claude` in the terminal.
- **Only one icon even if launched twice** → by design: a named mutex enforces a single
  instance, so re-running the `.exe` while it's already in the tray just exits silently.
