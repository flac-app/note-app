using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace StickyNotes;
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string heading, string message, string confirm, bool danger, bool cancellable)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight; ShowInTaskbar = false; Title = heading; FontFamily = new FontFamily("Segoe UI");
        AppTheme.ApplyIcon(this); AppTheme.ApplyResources(this);
        var card = new Border { Width = 340, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(22, 18, 18, 16), Margin = new Thickness(18),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = .35, Color = Colors.Black }, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(.97, .97), Opacity = 0 };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Line");
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Content = card;
        var panel = new StackPanel(); card.Child = panel;
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold });
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 6, 0, 18) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); panel.Children.Add(body);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(actions);
        if (cancellable) actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 76 });
        var ok = new Button { Content = confirm, IsDefault = true, IsCancel = !cancellable, MinWidth = 76, FontWeight = FontWeights.SemiBold };
        if (danger) ok.SetResourceReference(Control.ForegroundProperty, "Danger");
        ok.Click += (_, _) => DialogResult = true; actions.Children.Add(ok);
        Loaded += (_, _) =>
        {
            ok.Focus();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut }; var time = TimeSpan.FromMilliseconds(160);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
            card.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
            card.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
        };
    }
    public static bool Ask(Window? owner, string heading, string message, string confirm = "OK", bool danger = false) => Show(owner, new ConfirmDialog(heading, message, confirm, danger, true));
    public static void Inform(Window? owner, string heading, string message) => Show(owner, new ConfirmDialog(heading, message, "OK", false, false));
    private static bool Show(Window? owner, ConfirmDialog dialog)
    {
        if (owner != null && owner.IsVisible) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; dialog.Topmost = owner.Topmost; }
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
}
