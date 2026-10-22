using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickyNotes;
internal static class NotebookStyle
{
    public static readonly (string Key, string Name)[] TabLooks = { ("Minimal", "Minimal"), ("Worn", "Worn"), ("Notebook", "Notebook separator"), ("NotebookSmooth", "Notepad Separator Smooth") };
    public static string TabLookName(string look) => TabLooks.FirstOrDefault(l => l.Key == look).Name ?? look;
    public static bool IsSeparator(string look) => look is "Notebook" or "NotebookSmooth";
    public static bool PaperFor(NoteData note) => note.Paper switch { "Notebook" => true, "Clean" => false, _ => App.CurrentApp.Store.Preferences.NotebookStyle };
    public static string TabLookFor(NoteData note) => note.TabStyle ?? DefaultTabLook;
    public static string DefaultTabLook
    {
        get
        {
            var preferences = App.CurrentApp.Store.Preferences;
            if (preferences.NotebookTabs == true) return preferences.SmoothSeparators ? "NotebookSmooth" : "Notebook";
            return preferences.WornTabs ? "Worn" : "Minimal";
        }
    }
    public static bool Pull => App.CurrentApp.Store.Preferences.NotebookPull && App.CurrentApp.Store.Preferences.SmoothHover;
    public static double Corner(bool notebook) { double radius = Math.Clamp(App.CurrentApp.Store.Preferences.CornerRadius, 0, 24); return notebook ? Math.Round(radius / 3) : radius; }
    public const double TabWidth = 52, TabHeight = 60;
    private const int TextureSize = 384;
    private static BitmapSource? texture;
    private static readonly Dictionary<Color, Brush> papers = new();

    public static Brush Paper(Color color)
    {
        if (papers.TryGetValue(color, out var cached)) return cached;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(0, 0, TextureSize, TextureSize))));
        group.Children.Add(new ImageDrawing(Texture, new Rect(0, 0, TextureSize, TextureSize)));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, TextureSize, TextureSize), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        brush.Freeze(); papers[color] = brush; return brush;
    }

    public static Brush Vignette { get; } = CreateVignette();
    private static Brush CreateVignette()
    {
        var brush = new RadialGradientBrush { GradientOrigin = new Point(.42, .38), Center = new Point(.45, .42), RadiusX = .85, RadiusY = .85 };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), .35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(46, 20, 12, 4), .75));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(105, 15, 9, 3), 1));
        brush.Freeze(); return brush;
    }

    private static BitmapSource Texture => texture ??= CreateTexture();
    private static BitmapSource CreateTexture()
    {
        const int size = TextureSize;
        var random = new Random(1907);
        var field = new double[size * size];
        foreach (var (cells, amplitude) in new[] { (3, 1.0), (6, .55), (12, .32), (24, .18), (48, .1), (96, .06) })
        {
            var lattice = new double[cells * cells];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = random.NextDouble() * 2 - 1;
            for (int y = 0; y < size; y++)
            {
                double fy = (double)y * cells / size; int y0 = (int)fy; double ty = Smooth(fy - y0); int y1 = (y0 + 1) % cells;
                for (int x = 0; x < size; x++)
                {
                    double fx = (double)x * cells / size; int x0 = (int)fx; double tx = Smooth(fx - x0); int x1 = (x0 + 1) % cells;
                    double top = Lerp(lattice[y0 * cells + x0], lattice[y0 * cells + x1], tx);
                    double bottom = Lerp(lattice[y1 * cells + x0], lattice[y1 * cells + x1], tx);
                    field[y * size + x] += amplitude * Lerp(top, bottom, ty);
                }
            }
        }
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < field.Length; i++)
        {
            double value = Math.Clamp(field[i] / 1.4, -1, 1);
            if (value > 0) Set(i, 255, 250, 238, (byte)(value * 34));
            else Set(i, 30, 20, 10, (byte)(-value * 62));
        }
        for (int f = 0; f < 160; f++)
        {
            double x = random.NextDouble() * size, y = random.NextDouble() * size, angle = random.NextDouble() * Math.PI, length = 4 + random.NextDouble() * 10;
            bool light = random.NextDouble() < .5; byte alpha = (byte)random.Next(5, 13);
            for (double t = 0; t < length; t += .7)
            {
                int px = Wrap((int)(x + Math.Cos(angle) * t)), py = Wrap((int)(y + Math.Sin(angle) * t));
                Blend(py * size + px, light ? (byte)255 : (byte)25, alpha);
            }
        }
        for (int s = 0; s < 900; s++)
        {
            int px = random.Next(size), py = random.Next(size);
            Blend(py * size + px, 20, (byte)random.Next(22, 70));
            if (s % 9 == 0) { Blend(py * size + Wrap(px + 1), 20, 45); Blend(Wrap(py + 1) * size + px, 20, 45); }
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze(); return bitmap;

        void Set(int index, byte r, byte g, byte b, byte a) { pixels[index * 4] = b; pixels[index * 4 + 1] = g; pixels[index * 4 + 2] = r; pixels[index * 4 + 3] = a; }
        void Blend(int index, byte shade, byte alpha)
        {
            if (pixels[index * 4 + 3] >= alpha && pixels[index * 4] == shade) return;
            Set(index, shade, shade, shade, Math.Max(alpha, pixels[index * 4 + 3]));
        }
        static int Wrap(int value) => ((value % size) + size) % size;
    }
    private static double Smooth(double t) => t * t * (3 - 2 * t);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static FrameworkElement Tab(Color color, string icon, string label, string topLabel, double opacity, double iconOpacity, bool leftEdge, ImageSource? customIcon, bool worn = true)
    {
        const double w = TabWidth, h = TabHeight, r = 9;
        bool dark = color.R * .299 + color.G * .587 + color.B * .114 < 120;
        var ink = new SolidColorBrush(dark ? Color.FromRgb(243, 233, 210) : Color.FromRgb((byte)(color.R * .3), (byte)(color.G * .3), (byte)(color.B * .3)));
        var outline = Geometry.Parse($"M{w},0 L{r},0 Q0,0 0,{r} L0,{h - r} Q0,{h} {r},{h} L{w},{h} Z").Clone();
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(new LinearGradientBrush(Lighten(color, 1.06), Lighten(color, .82), 90), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new ImageBrush(Texture) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 96, 96), ViewportUnits = BrushMappingMode.Absolute, Opacity = .9 }, null, new Rect(0, 0, w, h));
            if (worn)
            {
                var cream = new SolidColorBrush(Color.FromArgb(225, 239, 227, 196));
                dc.DrawGeometry(cream, null, Geometry.Parse("M0,0 L15,0 11,2 12,4 7,4 5,7 2,9 2,14 0,16 Z"));
                dc.DrawGeometry(cream, null, Geometry.Parse($"M0,{h} L0,{h - 9} 2,{h - 6} 4,{h - 5} 5,{h - 2} 9,{h - 1} 12,{h} Z"));
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 239, 227, 196)), null, Geometry.Parse($"M1,16 L2,{h - 12} 1.2,{h - 12} 0.4,16 Z"));
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(80, 30, 20, 10)), 1), outline);
        }
        drawing.Freeze();
        var visual = new Border { Width = w, Height = h, Background = new DrawingBrush(drawing) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, Clip = outline, Cursor = System.Windows.Input.Cursors.Hand };
        if (leftEdge) { visual.RenderTransformOrigin = new Point(.5, .5); visual.RenderTransform = new ScaleTransform(-1, 1); }
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(leftEdge ? 0 : 4, 0, leftEdge ? 4 : 0, 0), IsHitTestVisible = false };
        if (!string.IsNullOrEmpty(topLabel)) content.Children.Add(new TextBlock { Text = topLabel, Foreground = ink, FontSize = 9, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (customIcon != null) content.Children.Add(new Image { Source = customIcon, Width = 26, Height = 26, Opacity = Math.Clamp(iconOpacity, 0, 1) });
        else content.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(icon) ? "▤" : icon, Foreground = ink, FontSize = string.IsNullOrEmpty(label) && string.IsNullOrEmpty(topLabel) ? 26 : 21, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Opacity = Math.Clamp(iconOpacity, 0, 1) * .92 });
        if (!string.IsNullOrEmpty(label)) content.Children.Add(new TextBlock { Text = label, Foreground = ink, FontSize = 9, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var tab = new Grid { Width = w, Height = h, Opacity = Math.Clamp(opacity, 0, 1), Background = Brushes.Transparent };
        tab.Children.Add(visual); tab.Children.Add(content);
        return tab;
    }
    private static Color Lighten(Color color, double factor) => Color.FromRgb((byte)Math.Clamp(color.R * factor, 0, 255), (byte)Math.Clamp(color.G * factor, 0, 255), (byte)Math.Clamp(color.B * factor, 0, 255));
}
