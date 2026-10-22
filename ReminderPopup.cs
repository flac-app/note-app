using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StickyNotes;
internal sealed class ReminderPopup : Window
{
    private static readonly List<ReminderPopup> open = new();
    private const double Shadow = 16, CardGap = 10;
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(10);
    private readonly Guid? noteId;
    private readonly Border card = new();
    private readonly DispatcherTimer autoClose = new() { Interval = Linger };
    private bool closing;

    public static void Ring(NoteWindow window)
    {
        var note = window.Note;
        open.FirstOrDefault(p => p.noteId == note.Id)?.Dismiss(animate: false);
        new ReminderPopup(note.Id, note.Title, note.ReminderMessage, ReminderSounds.LoopsFor(note),
            () => window.Snooze(TimeSpan.FromMinutes(10)), window.DismissReminder, window.Reveal).Present();
    }
    public static bool ShowsFor(NoteData note) => note.ReminderPopup ?? App.CurrentApp.Store.Preferences.ReminderPopup;
    public static void Preview(string title = "Chinese Noodles Recipe", string message = "It's time to take the noodles out of the water.")
        => new ReminderPopup(null, title, message, false, null, null, null).Present();
    public static void CloseAnswered()
    {
        foreach (var popup in open.ToArray())
            if (popup.noteId is Guid id && App.CurrentApp.Store.Notes.FirstOrDefault(n => n.Id == id) is not { ReminderActive: true }) popup.Dismiss();
    }

    private ReminderPopup(Guid? noteId, string title, string message, bool stay, Action? snooze, Action? dismiss, Action? reveal)
    {
        this.noteId = noteId;
        Title = "note.app - Reminder"; Width = 420 + 2 * Shadow; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        AppTheme.ApplyResources(this); FontFamily = AppFonts.Get(AppFonts.Sans);
        SourceInitialized += (_, _) => WindowVisibility.SetNoActivate(this);

        card.Margin = new Thickness(Shadow); card.CornerRadius = new CornerRadius(14); card.BorderThickness = new Thickness(1);
        card.SetResourceReference(Border.BackgroundProperty, "Page"); card.SetResourceReference(Border.BorderBrushProperty, "Line");
        card.Effect = new DropShadowEffect { BlurRadius = Shadow, ShadowDepth = 3, Direction = 270, Opacity = 0.35 };
        card.RenderTransform = new TranslateTransform();
        Content = card;
        var layout = new DockPanel(); card.Child = layout;

        var header = new DockPanel { LastChildFill = true };
        var headerBar = new Border { CornerRadius = new CornerRadius(13, 13, 0, 0), Padding = new Thickness(14, 8, 14, 8), Child = header };
        headerBar.SetResourceReference(Border.BackgroundProperty, "Hover");
        DockPanel.SetDock(headerBar, Dock.Top); layout.Children.Add(headerBar);
        var mark = new Image { Source = Mark(), Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
        header.Children.Add(mark);
        header.Children.Add(new TextBlock { Text = "note.app - Reminder", FontFamily = AppFonts.Brand, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });

        var body = new StackPanel { Margin = new Thickness(28, 16, 28, 22) }; layout.Children.Add(body);
        body.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 52, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrWhiteSpace(message))
            body.Children.Add(new TextBlock { Text = message.Trim(), FontSize = 14.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap, MaxHeight = 120, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0) });

        var actions = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); actions.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(actions);
        Button Action(string glyph, string text, int column, Action? run)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 9, 0) });
            content.Children.Add(new TextBlock { Text = text, FontSize = 15.5, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0), Focusable = false };
            button.SetResourceReference(BorderBrushProperty, "Muted");
            button.Click += (_, _) => { run?.Invoke(); Dismiss(); };
            Grid.SetColumn(button, column); actions.Children.Add(button);
            return button;
        }
        Action("", "Snooze", 0, snooze).ToolTip = "Remind me again in 10 minutes";
        Action("", "Dismiss", 2, dismiss);
        if (reveal != null) { card.Cursor = Cursors.Hand; card.MouseLeftButtonUp += (_, _) => { reveal(); Dismiss(); }; }

        if (!stay)
        {
            autoClose.Tick += (_, _) => Dismiss();
            MouseEnter += (_, _) => autoClose.Stop();
            MouseLeave += (_, _) => { if (!closing) autoClose.Start(); };
        }
        Closed += (_, _) => { autoClose.Stop(); open.Remove(this); Stack(); };
    }
    private static BitmapSource Mark()
    {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/note.app;component/NoteappFinal.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.Where(f => f.PixelWidth <= 24).MaxBy(f => f.PixelWidth) ?? decoder.Frames[0];
    }
    private static bool Smooth => App.CurrentApp.Store.Preferences.SmoothHover;
    private void Present()
    {
        open.Add(this);
        Opacity = Smooth ? 0 : 1;
        Show(); Stack();
        if (Smooth)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            card.RenderTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(48, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }
        if (!IsMouseOver) autoClose.Start();
    }
    private static void Stack()
    {
        var area = SystemParameters.WorkArea;
        double bottom = area.Bottom - 4;
        foreach (var popup in open.Where(p => !p.closing))
        {
            popup.UpdateLayout();
            popup.Left = area.Right - popup.ActualWidth - 4;
            popup.Top = bottom - popup.ActualHeight;
            bottom = popup.Top + 2 * Shadow - CardGap;
        }
    }
    private void Dismiss(bool animate = true)
    {
        if (closing) return;
        closing = true; autoClose.Stop();
        if (!animate || !Smooth) { Close(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
