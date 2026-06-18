using System.Windows;

namespace ClaudeTray;

/// <summary>
/// The settings window, built with WPF + the built-in .NET Fluent theme (<c>ThemeMode="System"</c>),
/// so it follows the Windows light/dark setting and gets the Windows 11 look (Mica, rounded corners,
/// Fluent controls) with no extra dependencies. The layout lives entirely in
/// <c>SettingsWindow.xaml</c> as a declarative grid — there is no imperative z-order stacking, which
/// is what made the old WinForms sidebar fragile.
///
/// Shown non-modally from the tray; on Save it applies the edited <see cref="Settings"/> through the
/// <c>onSave</c> callback supplied at construction. The interval is edited in minutes (the model
/// stores seconds).
/// </summary>
internal partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly Action<Settings> _onSave;

    private static double MinMinutes => Settings.MinRefreshSeconds / 60.0;
    private static double MaxMinutes => Settings.MaxRefreshSeconds / 60.0;

    public SettingsWindow(Settings current, Action<Settings> onSave)
    {
        _onSave = onSave;
        // Edit a copy so closing without saving leaves the caller's instance untouched.
        _settings = new Settings
        {
            RefreshSeconds = current.RefreshSeconds,
            ShowPercentage = current.ShowPercentage,
        };

        InitializeComponent();

        IntervalSlider.Minimum = MinMinutes;
        IntervalSlider.Maximum = MaxMinutes;
        IntervalSlider.Value = Math.Clamp(_settings.RefreshSeconds / 60.0, MinMinutes, MaxMinutes);
        UpdateIntervalLabel();

        ShowPctCheck.IsChecked = _settings.ShowPercentage;
        VersionText.Text = $"Version {Updater.CurrentVersion}";

        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath)); }
        catch { /* fall back to the default window icon */ }
    }

    private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateIntervalLabel();

    private void UpdateIntervalLabel()
    {
        // IntervalValue can be null while the slider's initial value is set during InitializeComponent.
        if (IntervalValue is null) return;
        double m = IntervalSlider.Value;
        IntervalValue.Text = m == 1.0 ? "1 minute" : $"{m:0.#} minutes";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.RefreshSeconds = (int)Math.Round(IntervalSlider.Value * 60.0);
        _settings.ShowPercentage = ShowPctCheck.IsChecked == true;
        _onSave(_settings);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
