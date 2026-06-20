using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
// System.Drawing (WinForms) is in the global usings, so disambiguate the WPF types.
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ClaudeTray;

/// <summary>
/// A bespoke, on-brand "toast" for the unexpected-reset event — a borderless WPF window that slides
/// up from the bottom-right with the Claude clay/coral gradient, a confetti burst, and the weekly
/// usage bar visibly emptying from its old level to 0%. It deliberately replaces the plain system
/// balloon for this one happy event: the reset hands your quota back, so it should feel like good
/// news. Shown non-modally from the tray; auto-dismisses, or the user can open Claude Code or close it.
/// </summary>
internal partial class ToastWindow : Window
{
    private bool _closing;

    // Festive palette for the confetti — golds, white, coral and soft pastels read well on the gradient.
    private static readonly Color[] ConfettiColors =
    {
        (Color)ColorConverter.ConvertFromString("#FFD93D"),
        Colors.White,
        (Color)ColorConverter.ConvertFromString("#FF8A65"),
        (Color)ColorConverter.ConvertFromString("#9DE0AD"),
        (Color)ColorConverter.ConvertFromString("#C39BD3"),
        (Color)ColorConverter.ConvertFromString("#FFF3B0"),
    };

    public ToastWindow(string headline, double fromFraction, string ahead)
    {
        InitializeComponent();

        double used = Math.Clamp(fromFraction, 0, 1);
        Subtitle.Text = headline;
        AvailPct.Text = "100%";
        Caption.Text = $"Was {(int)Math.Round(used * 100)}% used · {ahead}";
        FillScale.ScaleX = 1 - used; // available quota before the reset; animates up to full

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionBottomRight();
        PlayEntrance();
        FillTheBar();
        LaunchConfetti();

        // Auto-dismiss after a comfortable read; the user can also close or act before then.
        var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        life.Tick += (_, _) => { life.Stop(); Dismiss(); };
        life.Start();
    }

    // Park the window above the taskbar, against the right edge of the work area.
    private void PositionBottomRight()
    {
        Rect wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
    }

    // Slide up + fade in.
    private void PlayEntrance()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
        SlideT.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(44, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
    }

    // The headline metaphor: available quota fills back up to 100%, after a beat.
    private void FillTheBar()
    {
        var fill = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(550),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        FillScale.BeginAnimation(ScaleTransform.ScaleXProperty, fill);
    }

    // A short burst of confetti falling through the card.
    private void LaunchConfetti()
    {
        double w = Confetti.ActualWidth > 0 ? Confetti.ActualWidth : Width - 28;
        double h = Confetti.ActualHeight > 0 ? Confetti.ActualHeight : Height - 28;
        var rnd = new Random();

        for (int i = 0; i < 16; i++)
        {
            double size = 6 + rnd.NextDouble() * 6;
            bool round = rnd.Next(2) == 0;
            Shape piece = round
                ? new Ellipse { Width = size, Height = size }
                : new Rectangle { Width = size, Height = size * 0.6, RadiusX = 1, RadiusY = 1 };
            piece.Fill = new SolidColorBrush(ConfettiColors[rnd.Next(ConfettiColors.Length)]);

            double startX = rnd.NextDouble() * w;
            Canvas.SetLeft(piece, startX);
            Canvas.SetTop(piece, -size);

            var move = new TranslateTransform();
            var spin = new RotateTransform();
            piece.RenderTransformOrigin = new Point(0.5, 0.5);
            piece.RenderTransform = new TransformGroup { Children = { spin, move } };
            Confetti.Children.Add(piece);

            var dur = TimeSpan.FromMilliseconds(1100 + rnd.Next(1100));
            var begin = TimeSpan.FromMilliseconds(rnd.Next(450));
            var fall = new DoubleAnimation(0, h + size + 10, dur)
            {
                BeginTime = begin,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn },
            };
            var drift = new DoubleAnimation(0, (rnd.NextDouble() - 0.5) * 60, dur) { BeginTime = begin };
            var rotate = new DoubleAnimation(0, rnd.Next(180, 540), dur) { BeginTime = begin };
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
            {
                BeginTime = begin + dur - TimeSpan.FromMilliseconds(400),
            };

            move.BeginAnimation(TranslateTransform.YProperty, fall);
            move.BeginAnimation(TranslateTransform.XProperty, drift);
            spin.BeginAnimation(RotateTransform.AngleProperty, rotate);
            piece.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Dismiss();

    // Fade out, then close (guarded so the button and the auto-dismiss timer can't double-run it).
    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => Close();
        Root.BeginAnimation(OpacityProperty, fade);
    }
}
