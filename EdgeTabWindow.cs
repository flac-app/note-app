using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
namespace StickyNotes;
internal sealed class EdgeTabWindow : Window
{
    private const double SnapDistance = 64;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly Button button;
    private readonly NoteWindow note;
    private bool pressed, dragging;
    private Point dragStart, windowStart;
    private DateTime suppressHover;
    private DateTime lastNear = DateTime.MinValue;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private static bool IsHeld(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public EdgeTabWindow(NoteWindow note)
    {
        this.note = note;
        Width = Height = 44; SizeToContent = SizeToContent.Manual; WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent;
        SourceInitialized += (_, _) => WindowVisibility.SetHiddenFromSwitcher(this, true);
        button = new Button { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0), Padding = new Thickness(0) };
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter };
        Content = button; RefreshAppearance();
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            StopMotion();
            pressed = true; dragging = false; dragStart = ScreenCursor(); windowStart = new Point(Left, Top);
            Mouse.Capture(this); e.Handled = true;
        };
        PreviewMouseMove += (_, e) =>
        {
            if (!pressed) return;
            var point = ScreenCursor(); var delta = point - dragStart;
            if (!dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) > 5) { dragging = true; note.HideEdgePreview(); }
            if (dragging)
            {
                Left = windowStart.X + delta.X; Top = windowStart.Y + delta.Y;
                Opacity = NearSide(point) ? 1 : .6;
            }
            e.Handled = true;
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            bool moved = dragging; pressed = false; dragging = false; ReleaseMouseCapture(); Opacity = 1;
            if (moved) DropAt(ScreenCursor());
            else Clicked();
            e.Handled = true;
        };
        LostMouseCapture += (_, _) => { if (pressed) { pressed = false; Opacity = 1; if (dragging) SnapToEdge(); dragging = false; } };
        button.Click += (_, _) => Clicked();
        PreviewMouseRightButtonUp += (_, e) => { note.ShowNoteMenu(button); e.Handled = true; };
        timer.Tick += (_, _) =>
        {
            if (!IsVisible || pressed || DateTime.UtcNow < suppressHover || NotebookStyle.Pull) return;
            var cursor = System.Windows.Forms.Cursor.Position;
            var screen = new Point(cursor.X, cursor.Y);
            bool near = new Rect(-24, -24, ActualWidth + 48, ActualHeight + 48).Contains(PointFromScreen(screen)) || note.PointerOverPreview(screen);
            bool held = IsHeld(0x11) || IsHeld(0x01);
            if (near && !held) { lastNear = DateTime.UtcNow; note.ShowEdgePreview(); }
            else if (near) lastNear = DateTime.UtcNow;
            else if (note.AutoHideDelay is int delay ? (DateTime.UtcNow - lastNear).TotalMilliseconds > delay : IsHeld(0x01) || IsHeld(0x02)) note.HideEdgePreview();
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) { lastNear = DateTime.MinValue; timer.Start(); } else timer.Stop(); };
        Closed += (_, _) => timer.Stop();
    }
    private void Clicked() { if (NotebookStyle.Pull) note.ToggleEdgePage(); else note.DetachFromEdge(); }
    private Point ScreenCursor()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(new Point(cursor.X, cursor.Y));
    }
    private Rect AreaAt(Point dips)
    {
        var toPixels = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var pixel = toPixels.Transform(dips);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)pixel.X, (int)pixel.Y)).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(area.Left, area.Top)), transform.Transform(new Point(area.Right, area.Bottom)));
    }
    private bool NearSide(Point dips)
    {
        var area = AreaAt(dips);
        return dips.X - area.Left < SnapDistance || area.Right - dips.X < SnapDistance;
    }
    private void DropAt(Point dips)
    {
        suppressHover = DateTime.UtcNow.AddMilliseconds(500);
        if (NearSide(dips)) { SnapToEdge(); return; }
        note.Note.X = dips.X - 24; note.Note.Y = dips.Y - 14;
        note.DetachFromEdge();
    }
    internal Rect WorkArea()
    {
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(area.Left, area.Top)), transform.Transform(new Point(area.Right, area.Bottom)));
    }
    internal void Place()
    {
        var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == note.Note.EdgeTabScreen)
            ?? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(note).Handle);
        var transform = PresentationSource.FromVisual(note)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var pixels = screen.WorkingArea;
        var area = new Rect(transform.Transform(new Point(pixels.Left, pixels.Top)), transform.Transform(new Point(pixels.Right, pixels.Bottom)));
        StopMotion(); SetCovered(false);
        Left = note.Note.Edge == "Left" ? area.Left : area.Right - Width;
        Top = Math.Clamp(note.Note.EdgeTabY ?? note.Note.Y, area.Top, area.Bottom - Height);
    }
    private double EdgeLeft { get { var area = WorkArea(); return note.Note.Edge == "Left" ? area.Left : area.Right - Width; } }
    private void StopMotion()
    {
        double left = Left, top = Top;
        BeginAnimation(LeftProperty, null); BeginAnimation(TopProperty, null);
        Left = left; Top = top;
    }
    private void Glide(DependencyProperty property, double to, int milliseconds)
    {
        double from = (double)GetValue(property);
        BeginAnimation(property, null); SetValue(property, to);
        if (!IsVisible || !App.CurrentApp.Store.Preferences.SmoothHover || Math.Abs(from - to) < .5) return;
        BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop });
    }
    internal void SetCovered(bool covered) => Opacity = covered ? 0 : 1;
    internal FrameworkElement CreateTabVisual() => EdgeTabAppearance.Create(note.Note.EdgeTabColor, note.Note.EdgeTabIcon, note.Note.EdgeTabLabel, note.Note.EdgeTabTopLabel, note.Note.EdgeTabOpacity, note.Note.EdgeTabIconOpacity, note.Note.Edge == "Left", $"edge-icons/{note.Note.Id}.png", NotebookStyle.TabLookFor(note.Note), note.Note.EdgeTabScale);
    internal void BringToFront() { if (!IsVisible) return; Topmost = false; Topmost = true; }
    internal static void Arrange(IEnumerable<NoteWindow> notes, EdgeTabWindow? anchor = null)
    {
        double gap = NotebookStyle.Pull ? 8 : 4;
        var tabs = notes.Where(n => n.IsDocked && n.EdgeTab is { IsVisible: true }).Select(n => n.EdgeTab!).ToList();
        foreach (var group in tabs.GroupBy(t => (t.note.Note.Edge, t.WorkArea())))
        {
            var area = group.Key.Item2;
            var ordered = group.OrderBy(t => t.note.Note.EdgeTabY ?? t.Top).ThenBy(t => t == anchor ? 0 : 1).ToList();
            var tops = ordered.Select(t => Math.Clamp(t.note.Note.EdgeTabY ?? t.Top, area.Top, Math.Max(area.Top, area.Bottom - t.Height))).ToArray();
            int pivot = Math.Max(0, anchor == null ? 0 : ordered.IndexOf(anchor));
            for (int i = pivot + 1; i < ordered.Count; i++) tops[i] = Math.Max(tops[i], tops[i - 1] + ordered[i - 1].Height + gap);
            for (int i = pivot - 1; i >= 0; i--) tops[i] = Math.Min(tops[i], tops[i + 1] - ordered[i].Height - gap);
            double floor = area.Top;
            for (int i = 0; i < ordered.Count; i++) { tops[i] = Math.Max(tops[i], floor); floor = tops[i] + ordered[i].Height + gap; }
            double ceiling = area.Bottom;
            for (int i = ordered.Count - 1; i >= 0; i--) { tops[i] = Math.Max(area.Top, Math.Min(tops[i], ceiling - ordered[i].Height)); ceiling = tops[i] - gap; }
            for (int i = 0; i < ordered.Count; i++)
            {
                var tab = ordered[i];
                tab.Glide(LeftProperty, tab.EdgeLeft, 220);
                tab.Glide(TopProperty, tops[i], 220);
                tab.note.Note.EdgeTabY = tops[i];
            }
        }
    }
    internal void SnapToEdge()
    {
        var area = WorkArea();
        SnapWithin(area, Left + Width / 2, Top);
        note.Note.EdgeTabScreen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).DeviceName;
        App.CurrentApp.ArrangeTabs(this); App.CurrentApp.Save();
    }
    internal void SnapWithin(Rect area, double centerX, double top)
    {
        note.Note.Edge = centerX < area.Left + area.Width / 2 ? "Left" : "Right";
        Left = note.Note.Edge == "Left" ? area.Left : area.Right - Width;
        Top = Math.Clamp(top, area.Top, area.Bottom - Height);
        note.Note.EdgeTabY = Top;
        RefreshAppearance();
    }
    public void RefreshAppearance()
    {
        var tab = CreateTabVisual();
        button.Content = tab;
        if (Width != tab.Width || Height != tab.Height)
        {
            bool right = note.Note.Edge != "Left"; double rightEdge = Left + Width;
            StopMotion(); Width = tab.Width; Height = tab.Height;
            if (right && IsLoaded) Left = rightEdge - Width;
        }
    }
}
