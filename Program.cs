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
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--render")
        {
            RenderTest(args.Length >= 2 ? args[1] : ".");
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }

    // Dev helper: dump sample icons as PNG at real tray sizes for visual inspection.
    private static void RenderTest(string dir)
    {
        Directory.CreateDirectory(dir);
        (double pct, IconRenderer.State st, bool fl)[] cases =
        {
            (0.08, IconRenderer.State.Ok, false),
            (0.54, IconRenderer.State.Ok, false),
            (1.00, IconRenderer.State.Ok, true),
        };
        foreach (int size in new[] { 16, 20, 32 })
            foreach (var (pct, st, fl) in cases)
                using (var bmp = IconRenderer.Render(pct, st, fl, size))
                    bmp.Save(Path.Combine(dir, $"icon_{(int)(pct * 100)}_{size}.png"));
        Console.WriteLine("rendered to " + Path.GetFullPath(dir));
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
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 300_000 }; // 5 min
    private readonly System.Windows.Forms.Timer _flash = new() { Interval = 500 };
    private readonly List<ToolStripMenuItem> _metricItems = new();

    private UsageData? _data;
    private string _metric = "5h";
    private bool _flashOn;
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

        _poll.Tick += async (_, _) => await RefreshAsync();
        _poll.Start();
        _flash.Tick += (_, _) => { if (CurrentPct() >= 0.90) { _flashOn = !_flashOn; Render(); } };
        _flash.Start();

        _ = RefreshAsync(); // fire first fetch immediately
    }

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

        var refresh = new ToolStripMenuItem("Refresh now");
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

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

    private async Task RefreshAsync()
    {
        _data = await _api.FetchAsync();
        _flashOn = false;
        Render();
    }

    private double CurrentPct()
        => _data != null && _data.Error == null ? Math.Min(1.0, _data.Metric(_metric)) : 0.0;

    private void Render()
    {
        IconRenderer.State state =
            _data == null ? IconRenderer.State.Connecting :
            _data.Error != null ? IconRenderer.State.Error :
            IconRenderer.State.Ok;

        bool flash = CurrentPct() >= 0.90 && _flashOn;
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);

        using Bitmap bmp = IconRenderer.Render(CurrentPct(), state, flash, size);
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
        lines.Add($"Status: {_data.Status}");
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
        _tray.Visible = false;
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _tray.Dispose();
        ExitThread();
    }
}
