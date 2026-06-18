using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeTray;

/// <summary>
/// A small, modern settings window. Follows the system light/dark theme (including the
/// Windows 11 immersive dark title bar), uses the Segoe UI system font, and lays its fields
/// out in a grid so new options can be added as extra rows without reflowing the window.
/// Shown non-modally (it is a normal, resizable window that appears in the taskbar) so it can
/// grow into a richer settings surface over time; on Save it applies the edited
/// <see cref="Settings"/> through the <c>onSave</c> callback supplied at construction.
/// </summary>
internal sealed class SettingsForm : Form
{
    // DwmSetWindowAttribute: paint the title bar dark to match a dark Windows theme.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly Settings _settings;
    private readonly Action<Settings> _onSave;
    private readonly NumericUpDown _refresh;
    private readonly CheckBox _showPct;
    private readonly bool _dark = IsSystemDark();

    public SettingsForm(Settings current, Action<Settings> onSave)
    {
        _onSave = onSave;
        // Edit a copy so closing without saving leaves the caller's instance untouched.
        _settings = new Settings
        {
            RefreshSeconds = current.RefreshSeconds,
            ShowPercentage = current.ShowPercentage,
        };

        Text = "Settings";
        Font = new Font("Segoe UI", 9.75f);
        // A normal, resizable window (not a modal dialog) so it can host more settings later.
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        MaximizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 420);
        MinimumSize = new Size(420, 320);
        // Open maximized; the window can host much more as settings grow over time.
        WindowState = FormWindowState.Maximized;
        Padding = new Padding(20, 18, 20, 16);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* default icon */ }

        Color back = _dark ? Color.FromArgb(32, 32, 32) : Color.White;
        Color fore = _dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(30, 30, 30);
        Color subtle = _dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(110, 110, 110);
        BackColor = back;
        ForeColor = fore;

        var heading = new Label
        {
            Text = "Refresh",
            AutoSize = true,
            ForeColor = fore,
            Font = new Font("Segoe UI Semibold", 11f),
            Margin = new Padding(0, 0, 0, 2),
        };
        var caption = new Label
        {
            Text = "How often the tray checks your Claude usage.",
            AutoSize = true,
            ForeColor = subtle,
            Margin = new Padding(0, 0, 0, 12),
        };

        var fieldLabel = new Label
        {
            Text = "Interval",
            AutoSize = true,
            ForeColor = fore,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 0),
        };
        _refresh = new NumericUpDown
        {
            Minimum = (decimal)Settings.MinRefreshSeconds / 60m,
            Maximum = (decimal)Settings.MaxRefreshSeconds / 60m,
            DecimalPlaces = 1,
            Increment = 0.5m,
            Value = Math.Clamp((decimal)_settings.RefreshSeconds / 60m,
                               (decimal)Settings.MinRefreshSeconds / 60m,
                               (decimal)Settings.MaxRefreshSeconds / 60m),
            Width = 80,
            TextAlign = HorizontalAlignment.Right,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _dark ? Color.FromArgb(45, 45, 45) : Color.White,
            ForeColor = fore,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 8, 0),
        };
        var unit = new Label
        {
            Text = "minutes",
            AutoSize = true,
            ForeColor = subtle,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 0, 0),
        };

        var fieldRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
        };
        fieldRow.Controls.Add(fieldLabel, 0, 0);
        fieldRow.Controls.Add(_refresh, 1, 0);
        fieldRow.Controls.Add(unit, 2, 0);

        var displayHeading = new Label
        {
            Text = "Display",
            AutoSize = true,
            ForeColor = fore,
            Font = new Font("Segoe UI Semibold", 11f),
            Margin = new Padding(0, 18, 0, 6),
        };
        _showPct = new CheckBox
        {
            Text = "Show the usage percentage on the icon",
            Checked = _settings.ShowPercentage,
            AutoSize = true,
            ForeColor = fore,
            FlatStyle = FlatStyle.Standard,
            Margin = new Padding(0, 0, 0, 0),
        };

        var ok = new Button
        {
            Text = "Save",
            AutoSize = true,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(8, 0, 0, 0),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(8, 0, 0, 0),
        };
        StyleButton(ok, accent: true);
        StyleButton(cancel, accent: false);
        ok.Click += (_, _) =>
        {
            _settings.RefreshSeconds = (int)Math.Round(_refresh.Value * 60m);
            _settings.ShowPercentage = _showPct.Checked;
            _onSave(_settings);
            Close();
        };
        cancel.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,   // keep Save and Cancel on one line, never wrap one out of view
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            AutoSize = true,
            Margin = Padding.Empty,
        };
        buttons.Controls.Add(ok);     // RightToLeft → Save sits rightmost
        buttons.Controls.Add(cancel);

        // Footer: product version on the left, action buttons on the right, both pinned to the
        // bottom so they stay together no matter how tall the (resizable/maximized) window is.
        var version = new Label
        {
            Text = $"Version {Updater.CurrentVersion}",
            AutoSize = true,
            ForeColor = subtle,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Margin = new Padding(0, 0, 0, 6),
        };
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(version, 0, 0);
        footer.Controls.Add(buttons, 1, 0);

        // Top-to-bottom stack: Refresh section, Display section, then a spacer that pushes the
        // footer (version + buttons) to the bottom of the resizable window.
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // heading
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // caption
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // fieldRow
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // displayHeading
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // _showPct
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // footer (anchored bottom)
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(caption, 0, 1);
        layout.Controls.Add(fieldRow, 0, 2);
        layout.Controls.Add(displayHeading, 0, 3);
        layout.Controls.Add(_showPct, 0, 4);
        layout.Controls.Add(footer, 0, 5);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void StyleButton(Button b, bool accent)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = accent ? 0 : 1;
        b.UseVisualStyleBackColor = false;
        if (accent)
        {
            Color a = SystemColors.Highlight; // honors the user's Windows accent color
            b.BackColor = a;
            b.ForeColor = Color.White;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(a, 0.1f);
        }
        else
        {
            b.BackColor = _dark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(251, 251, 251);
            b.ForeColor = ForeColor;
            b.FlatAppearance.BorderColor = _dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(210, 210, 210);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_dark)
        {
            int on = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
