using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClaudeTray;

/// <summary>
/// A small, modern settings dialog. Follows the system light/dark theme (including the
/// Windows 11 immersive dark title bar), uses the Segoe UI system font, and lays its fields
/// out in a grid so new options can be added as extra rows without reflowing the window.
/// Returns the edited <see cref="Settings"/> via <see cref="Result"/> when accepted.
/// </summary>
internal sealed class SettingsForm : Form
{
    // DwmSetWindowAttribute: paint the title bar dark to match a dark Windows theme.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly Settings _settings;
    private readonly NumericUpDown _refresh;
    private readonly bool _dark = IsSystemDark();

    /// <summary>The edited settings, valid only after the dialog returns <see cref="DialogResult.OK"/>.</summary>
    public Settings Result => _settings;

    public SettingsForm(Settings current)
    {
        // Edit a copy so a Cancel leaves the caller's instance untouched.
        _settings = new Settings { RefreshSeconds = current.RefreshSeconds };

        Text = "Settings";
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        // Grow to fit the content (the caption is the widest element) so nothing clips at any
        // DPI, with a sensible minimum width.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(380, 0);
        Padding = new Padding(20, 18, 20, 16);

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

        var ok = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(8, 0, 0, 0),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(8, 0, 0, 0),
        };
        StyleButton(ok, accent: true);
        StyleButton(cancel, accent: false);
        ok.Click += (_, _) => _settings.RefreshSeconds = (int)Math.Round(_refresh.Value * 60m);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            Margin = new Padding(0, 22, 0, 0),
        };
        buttons.Controls.Add(ok);     // RightToLeft → Save sits rightmost
        buttons.Controls.Add(cancel);

        // Top-to-bottom stack: heading, caption, field row, buttons. The panel auto-sizes to
        // its widest row (the caption) and the form grows to match.
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(caption, 0, 1);
        layout.Controls.Add(fieldRow, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
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
