using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeTray;

// Manages the "start with Windows" entry under HKCU Run (per-user, no admin needed).
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeTray";

    // Real .exe path, correct even for a single-file self-contained publish.
    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v &&
               string.Equals(v.Trim('"'), ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

internal static class Program
{
    // Held for the whole process lifetime so a second launch can't spawn a duplicate tray icon.
    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--render")
        {
            RenderTest(args.Length >= 2 ? args[1] : ".");
            return;
        }

        if (args.Length >= 1 && args[0] == "--makeicon")
        {
            MakeIcon(args.Length >= 2 ? args[1] : "ClaudeTray.ico");
            return;
        }

        if (args.Length >= 1 && args[0] == "--social")
        {
            string path = args.Length >= 2 ? args[1] : "docs/social-preview.png";
            using Bitmap bmp = IconRenderer.RenderSocial(1280, 640);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("wrote " + Path.GetFullPath(path));
            return;
        }

        if (args.Length >= 1 && args[0] == "--insights")
        {
            var d = UsageInsights.Compute(DateTimeOffset.UtcNow.UtcDateTime);
            if (d.Error != null) { Console.WriteLine("error: " + d.Error); return; }
            Console.WriteLine($"24h: {d.Requests} reqs  {d.Sessions} sessions");
            Console.WriteLine($"subagents: {d.SubagentPct:P0}   >150k ctx: {d.HeavyContextPct:P0}");
            foreach (var (model, pct) in d.ByModel)
                Console.WriteLine($"  {model}: {pct:P0}");
            return;
        }

        // Single instance: if the tray app is already running, just exit — don't add a second icon.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\ClaudeTray.SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }

    // Dev helper: dump sample icons as PNG at real tray sizes for visual inspection.
    private static void RenderTest(string dir)
    {
        Directory.CreateDirectory(dir);
        (double pct, IconRenderer.State st, bool fl, Projection verdict)[] cases =
        {
            (0.08, IconRenderer.State.Ok, false, Projection.Unknown),
            (0.08, IconRenderer.State.Ok, false, Projection.Ok),
            (0.54, IconRenderer.State.Ok, false, Projection.Danger),
            (1.00, IconRenderer.State.Ok, true, Projection.Danger),
        };
        foreach (int size in new[] { 16, 20, 32 })
            foreach (var (pct, st, fl, verdict) in cases)
                using (var bmp = IconRenderer.Render(pct, st, fl, size, verdict))
                    bmp.Save(Path.Combine(dir, $"icon_{(int)(pct * 100)}_{size}.png"));
        Console.WriteLine("rendered to " + Path.GetFullPath(dir));
    }

    // Dev helper: build a multi-resolution .ico for the app (PNG-compressed entries, valid on
    // Windows Vista+) from the GDI+ logo renderer.
    private static void MakeIcon(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        byte[][] pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using Bitmap bmp = IconRenderer.RenderLogo(sizes[i]);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs[i] = ms.ToArray();
        }

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0);              // reserved
        bw.Write((short)1);              // type: icon
        bw.Write((short)sizes.Length);   // image count

        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s)); // width (0 = 256)
            bw.Write((byte)(s >= 256 ? 0 : s)); // height
            bw.Write((byte)0);                  // palette
            bw.Write((byte)0);                  // reserved
            bw.Write((short)1);                 // color planes
            bw.Write((short)32);                // bits per pixel
            bw.Write(pngs[i].Length);           // image size in bytes
            bw.Write(offset);                   // image offset
            offset += pngs[i].Length;
        }
        foreach (byte[] png in pngs) bw.Write(png);

        Console.WriteLine("wrote " + Path.GetFullPath(path));
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private static readonly string[] Metrics = { "5h", "7d", "extra" };
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["5h"] = "Session 5h", ["7d"] = "Week 7d", ["extra"] = "Extra",
    };

    private readonly NotifyIcon _tray;
    private readonly ApiClient _api = new();
    private readonly BurnTracker _burn = new();
    private readonly Updater _updater = new();
    private readonly Settings _settings = Settings.Load();
    private volatile InsightsData? _insights;
    private readonly System.Windows.Forms.Timer _poll = new(); // interval set from settings
    private readonly System.Windows.Forms.Timer _flash = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _updateCheck = new() { Interval = 21_600_000 }; // 6 h
    private readonly List<ToolStripMenuItem> _metricItems = new();
    private ToolStripMenuItem _updateItem = null!;

    private UsageData? _data;
    private DateTime? _lastRefresh;
    private UpdateInfo? _update;
    private string _metric = "5h";
    private bool _flashOn;
    private bool _updating;
    private IntPtr _iconHandle = IntPtr.Zero;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayContext()
    {
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Claude Code — connecting…",
            ContextMenuStrip = BuildMenu(),
        };
        Render(); // initial "connecting" icon

        _poll.Interval = _settings.RefreshSeconds * 1000;
        _poll.Tick += async (_, _) => await RefreshAsync();
        _poll.Start();
        _flash.Tick += (_, _) => { if (CurrentPct() >= 0.90) { _flashOn = !_flashOn; Render(); } };
        _flash.Start();
        _updateCheck.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateCheck.Start();

        _tray.BalloonTipClicked += (_, _) => { if (_update != null) _ = ApplyUpdateAsync(); };

        _ = RefreshAsync(); // fire first fetch immediately
        _ = CheckForUpdateAsync(); // look for a newer release on launch
        RecomputeInsights(); // build the 24h usage breakdown in the background
    }

    // Scan local transcripts off the UI thread; the submenu reads the cached result.
    private void RecomputeInsights()
        => _ = Task.Run(() => _insights = UsageInsights.Compute(DateTimeOffset.UtcNow.UtcDateTime));

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var showOn = new ToolStripMenuItem("Show on icon");
        foreach (string key in Metrics)
        {
            var item = new ToolStripMenuItem(Labels[key]) { Tag = key, Checked = key == _metric };
            item.Click += (_, _) => SetMetric((string)item.Tag!);
            _metricItems.Add(item);
            showOn.DropDownItems.Add(item);
        }
        menu.Items.Add(showOn);

        var insights = new ToolStripMenuItem("Usage insights (24h)");
        insights.DropDownOpening += (_, _) => PopulateInsights(insights);
        insights.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        menu.Items.Add(insights);

        var refresh = new ToolStripMenuItem("Refresh now");
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        // Hidden until a newer release is found; then shows "Update to vX.Y.Z".
        _updateItem = new ToolStripMenuItem("Update available") { Visible = false, Font = new Font(menu.Font, FontStyle.Bold) };
        _updateItem.Click += (_, _) => { if (!_updating) _ = ApplyUpdateAsync(); };
        menu.Items.Add(_updateItem);

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = StartupManager.IsEnabled() };
        startup.Click += (_, _) =>
        {
            try
            {
                StartupManager.SetEnabled(!startup.Checked);
                startup.Checked = StartupManager.IsEnabled();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not change startup setting:\n{ex.Message}",
                    "Claude Code Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        menu.Items.Add(startup);

        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => ExitApp();
        menu.Items.Add(quit);

        return menu;
    }

    private void SetMetric(string key)
    {
        _metric = key;
        foreach (var it in _metricItems)
            it.Checked = (string)it.Tag! == key;
        Render();
    }

    // The settings window is shown non-modally; keep a reference so we reuse the open one
    // instead of stacking duplicates.
    private SettingsForm? _settingsForm;

    // Open the settings window (non-modal); on Save it calls ApplySettings to persist and apply.
    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings, ApplySettings);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    // Persist the edited settings and apply the new values immediately.
    private void ApplySettings(Settings updated)
    {
        bool intervalChanged = updated.RefreshSeconds != _settings.RefreshSeconds;
        _settings.RefreshSeconds = updated.RefreshSeconds;
        _settings.ShowPercentage = updated.ShowPercentage;

        try { _settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save settings:\n{ex.Message}",
                "Claude Code Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (intervalChanged)
        {
            // Restart the timer so the new cadence takes effect from now.
            _poll.Stop();
            _poll.Interval = _settings.RefreshSeconds * 1000;
            _poll.Start();
        }
        Render(); // reflect a possible show-percentage change immediately
    }

    // Fill the "Usage insights" submenu from the cached scan; trigger a refresh for next time.
    private void PopulateInsights(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        InsightsData? d = _insights;

        if (d == null)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("Computing…") { Enabled = false });
        }
        else if (d.Error != null)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem($"Unavailable: {d.Error}") { Enabled = false });
        }
        else if (d.Requests == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("No usage in the last 24h") { Enabled = false });
        }
        else
        {
            void Line(string text) => parent.DropDownItems.Add(new ToolStripMenuItem(text) { Enabled = false });

            Line($"Last 24h: {d.Requests} requests, {d.Sessions} sessions");
            Line($"From subagents: {Pct(d.SubagentPct)}");
            Line($">150k context: {Pct(d.HeavyContextPct)}");
            if (d.ByModel.Count > 0)
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
                Line("By model:");
                foreach (var (model, pct) in d.ByModel.Take(5))
                    Line($"   {model}: {Pct(pct)}");
            }
        }

        parent.DropDownItems.Add(new ToolStripSeparator());
        var refresh = new ToolStripMenuItem("Recompute");
        refresh.Click += (_, _) => RecomputeInsights();
        parent.DropDownItems.Add(refresh);

        // Keep the cache reasonably fresh for the next open.
        RecomputeInsights();
    }

    private async Task RefreshAsync()
    {
        _data = await _api.FetchAsync();
        _lastRefresh = DateTime.Now;
        if (_data is { Error: null })
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (string key in Metrics)
                _burn.Record(key, _data.Metric(key), _data.ResetOf(key), now);
        }
        _flashOn = false;
        Render();
        RecomputeInsights();
    }

    // Ask GitHub for the latest release; if newer, surface it in the menu and notify once.
    private async Task CheckForUpdateAsync()
    {
        if (_updating) return;
        UpdateInfo? info = await _updater.CheckAsync();
        if (info == null || (_update != null && info.Version <= _update.Version)) return;

        _update = info;
        _updateItem.Text = $"Update to {info.Tag}";
        _updateItem.Visible = true;

        _tray.BalloonTipTitle = "Claude Code Tray — update available";
        _tray.BalloonTipText = $"Version {info.Version} is available (you have {Updater.CurrentVersion}). Click to install.";
        _tray.ShowBalloonTip(10_000);
    }

    // Download the installer and hand off to it; the app exits so its .exe can be replaced.
    private async Task ApplyUpdateAsync()
    {
        if (_updating || _update is not { } info) return;
        _updating = true;
        _updateItem.Text = $"Downloading {info.Tag}…";
        _updateItem.Enabled = false;

        try
        {
            string setup = await _updater.DownloadAsync(info);
            Updater.RunInstaller(setup);
            ExitApp(); // release the single-instance mutex and unlock the .exe for the installer
        }
        catch (Exception ex)
        {
            _updating = false;
            _updateItem.Text = $"Update to {info.Tag}";
            _updateItem.Enabled = true;
            MessageBox.Show($"Could not download the update:\n{ex.Message}",
                "Claude Code Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private double CurrentPct()
        => _data != null && _data.Error == null ? Math.Min(1.0, _data.Metric(_metric)) : 0.0;

    // Projection for the currently displayed metric (session vs. week vs. extra).
    private (Projection verdict, double eta) CurrentProjection()
    {
        if (_data is not { Error: null }) return (Projection.Unknown, 0);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (verdict, eta, _) = _burn.Project(_metric, _data.Metric(_metric), _data.ResetOf(_metric), now);
        return (verdict, eta);
    }

    private void Render()
    {
        IconRenderer.State state =
            _data == null ? IconRenderer.State.Connecting :
            _data.Error != null ? IconRenderer.State.Error :
            IconRenderer.State.Ok;

        bool flash = CurrentPct() >= 0.90 && _flashOn;
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);

        Projection verdict = CurrentProjection().verdict;

        // While connecting (no data yet), show the app logo instead of a gray "0".
        using Bitmap bmp = state == IconRenderer.State.Connecting
            ? IconRenderer.RenderLogo(size)
            : IconRenderer.Render(CurrentPct(), state, flash, size, verdict, _settings.ShowPercentage);
        SetTrayIcon(bmp);
        _tray.Text = Truncate(BuildTooltip(), 127);
    }

    private void SetTrayIcon(Bitmap bmp)
    {
        IntPtr newHandle = bmp.GetHicon();
        Icon? old = _tray.Icon;
        IntPtr oldHandle = _iconHandle;

        _tray.Icon = Icon.FromHandle(newHandle);
        _iconHandle = newHandle;

        old?.Dispose();
        if (oldHandle != IntPtr.Zero) DestroyIcon(oldHandle);
    }

    private string BuildTooltip()
    {
        if (_data == null) return "Claude Code — connecting…";
        if (_data.Error != null) return $"Claude Code — API error\n{_data.Error}";

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r5 = _data.Reset5h > 0 ? FmtCountdown(_data.Reset5h - now) : "--";
        string r7 = _data.Reset7d > 0 ? FmtDays(_data.Reset7d - now) : "--";

        var lines = new List<string>
        {
            $"Session 5h: {Pct(_data.Session5h)}  ⟳ {r5}",
            $"Week 7d: {Pct(_data.Week7d)}  ⟳ {r7}",
        };
        if (_data.Extra > 0.001)
        {
            string re = _data.ResetExtra > 0 ? FmtDays(_data.ResetExtra - now) : "--";
            lines.Add($"Extra: {Pct(_data.Extra)}  ⟳ {re}");
        }

        var (verdict, eta) = CurrentProjection();
        if (verdict == Projection.Danger)
            lines.Add($"⚠ Projection: 100% in {FmtDays(eta)} (before reset)");
        else if (verdict == Projection.Ok)
            lines.Add(double.IsInfinity(eta)
                ? "✓ Projection: on track"
                : $"✓ Projection: 100% in {FmtDays(eta)} (after reset)");

        string updated = _lastRefresh is { } t ? $"  ⟳ {t:HH:mm:ss}" : "";
        lines.Add($"Status: {_data.Status}{updated}");
        return string.Join("\n", lines);
    }

    private static string Pct(double v) => $"{(int)Math.Round(Math.Min(v, 1.0) * 100)}%";

    private static string FmtCountdown(double s)
    {
        if (s <= 0) return "now";
        int h = (int)(s / 3600), m = (int)(s % 3600 / 60);
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }

    private static string FmtDays(double s)
    {
        if (s <= 0) return "now";
        int d = (int)(s / 86400), h = (int)(s % 86400 / 3600);
        return d > 0 ? $"{d}d {h}h" : FmtCountdown(s);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private void ExitApp()
    {
        _poll.Stop();
        _flash.Stop();
        _updateCheck.Stop();
        _tray.Visible = false;
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _tray.Dispose();
        ExitThread();
    }
}
