using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StickyNotes;
internal sealed class ImageResizeAdorner : Adorner
{
    private const double HandleHit = 18, HandleSize = 10, LockSize = 20, LockInset = 4;
    private static readonly Brush Ink = Frozen(Color.FromArgb(230, 242, 242, 242));
    private static readonly Brush Shade = Frozen(Color.FromArgb(215, 24, 24, 24));
    private static readonly Pen Outline = FrozenPen(Color.FromArgb(200, 242, 242, 242), 1);
    private static readonly Pen OutlineShadow = FrozenPen(Color.FromArgb(110, 0, 0, 0), 3);
    private readonly VisualCollection children;
    private readonly Thumb? handle;
    private readonly Border? hint;
    private readonly Button lockButton;
    public bool Locked { get; }
    public ImageResizeAdorner(Image image, Action toggleLock, Action began, Action<double> resize) : this(image, false, toggleLock)
    {
        handle = new Thumb { Width = HandleHit, Height = HandleHit, Cursor = Cursors.SizeNWSE, Template = HandleTemplate() };
        var text = new TextBlock { FontSize = 11, Foreground = Ink, FontFamily = new FontFamily("Segoe UI") };
        text.Inlines.Add(new Run("Esc") { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new Run(" to confirm"));
        hint = new Border { Background = Shade, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3, 7, 4), Child = text, IsHitTestVisible = false, SnapsToDevicePixels = true };
        children.Insert(0, hint);
        children.Insert(1, handle);
        handle.DragStarted += (_, _) => began();
        handle.DragDelta += (_, e) =>
        {
            var adorned = (Image)AdornedElement;
            double ratio = adorned.ActualHeight / Math.Max(1, adorned.ActualWidth);
            double delta = (e.HorizontalChange + e.VerticalChange * ratio) / (1 + ratio * ratio);
            resize(adorned.Width + delta);
        };
    }
    public static ImageResizeAdorner ForLocked(Image image, Action unlock) => new(image, true, unlock);
    private ImageResizeAdorner(Image image, bool locked, Action toggleLock) : base(image)
    {
        Locked = locked;
        children = new VisualCollection(this);
        var glyph = new TextBlock { Text = locked ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lockButton = new Button { Content = glyph, Width = LockSize, Height = LockSize, Margin = new Thickness(0), Padding = new Thickness(0), Cursor = Cursors.Hand, Focusable = false, Template = LockTemplate(), ToolTip = locked ? "Unlock to resize" : "Lock size" };
        lockButton.Click += (_, _) => toggleLock();
        children.Add(lockButton);
    }
    public void Fade(bool show, Action? hidden = null)
    {
        var animation = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 140 : 200));
        if (hidden != null) animation.Completed += (_, _) => hidden();
        BeginAnimation(OpacityProperty, animation);
    }
    public static bool NearLock(Image image, Point point)
    {
        var size = image.RenderSize;
        double reach = LockSize + LockInset * 2 + 12;
        return point.X >= size.Width - reach && point.X <= size.Width + 8 && point.Y >= -8 && point.Y <= reach;
    }
    private static ControlTemplate HandleTemplate()
    {
        var hit = new FrameworkElementFactory(typeof(Border));
        hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var dot = new FrameworkElementFactory(typeof(Border));
        dot.SetValue(WidthProperty, HandleSize);
        dot.SetValue(HeightProperty, HandleSize);
        dot.SetValue(Border.CornerRadiusProperty, new CornerRadius(2.5));
        dot.SetValue(Border.BackgroundProperty, Shade);
        dot.SetValue(Border.BorderBrushProperty, Ink);
        dot.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        dot.SetValue(SnapsToDevicePixelsProperty, true);
        hit.AppendChild(dot);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = hit };
    }
    private static ControlTemplate LockTemplate()
    {
        var chip = new FrameworkElementFactory(typeof(Border), "chip");
        chip.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        chip.SetValue(Border.BackgroundProperty, Shade);
        chip.SetValue(Border.BorderBrushProperty, Frozen(Color.FromArgb(90, 242, 242, 242)));
        chip.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chip.SetValue(SnapsToDevicePixelsProperty, true);
        chip.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(Button)) { VisualTree = chip };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Ink, "chip"));
        template.Triggers.Add(hover);
        return template;
    }
    private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    private static Pen FrozenPen(Color color, double thickness) { var pen = new Pen(Frozen(color), thickness); pen.Freeze(); return pen; }
    protected override int VisualChildrenCount => children.Count;
    protected override Visual GetVisualChild(int index) => children[index];
    protected override Size MeasureOverride(Size constraint)
    {
        hint?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        handle?.Measure(new Size(HandleHit, HandleHit));
        lockButton.Measure(new Size(LockSize, LockSize));
        return AdornedElement.RenderSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = AdornedElement.RenderSize;
        handle?.Arrange(new Rect(size.Width - HandleHit / 2, size.Height - HandleHit / 2, HandleHit, HandleHit));
        hint?.Arrange(new Rect(new Point(0, size.Height + 8), hint.DesiredSize));
        lockButton.Arrange(new Rect(Math.Max(0, size.Width - LockSize - LockInset), LockInset, LockSize, LockSize));
        return finalSize;
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Locked) return;
        var bounds = new Rect(AdornedElement.RenderSize);
        drawingContext.DrawRectangle(null, OutlineShadow, bounds);
        drawingContext.DrawRectangle(null, Outline, bounds);
    }
}
