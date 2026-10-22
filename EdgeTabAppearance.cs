using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace StickyNotes;
internal static class EdgeTabAppearance
{
    public static bool IsValidColor(string color) => Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$");
    public static FrameworkElement Create(string color, string icon, string label, string topLabel = "", double opacity = 1, double iconOpacity = 1, bool leftEdge = false, string? iconFile = null, string look = "Minimal", double scale = 1)
    {
        var tab = CreateUnscaled(color, icon, label, topLabel, opacity, iconOpacity, leftEdge, iconFile, look);
        scale = Math.Clamp(scale, .6, 1.6);
        if (Math.Abs(scale - 1) < .001) return tab;
        return new Viewbox { Child = tab, Stretch = Stretch.Fill, Width = tab.Width * scale, Height = tab.Height * scale };
    }
    private static FrameworkElement CreateUnscaled(string color, string icon, string label, string topLabel, double opacity, double iconOpacity, bool leftEdge, string? iconFile, string look)
    {
        var background = IsValidColor(color) ? (Color)ColorConverter.ConvertFromString(color) : Color.FromRgb(255, 241, 186);
        var customIcon = Asset(iconFile ?? "edge-icons/default.png") ?? Asset("edge-icons/default.png");
        if (NotebookStyle.IsSeparator(look))
            return NotebookStyle.Tab(background, icon, label, topLabel, opacity, iconOpacity, leftEdge, customIcon, worn: look == "Notebook");
        var foreground = background.R * .299 + background.G * .587 + background.B * .114 < 145 ? Brushes.White : Brushes.Black;
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(topLabel)) panel.Children.Add(new TextBlock { Text = topLabel, Foreground = foreground, FontSize = 9, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 0) });
        if (customIcon != null) panel.Children.Add(new Image { Source = customIcon, Width = 23, Height = 23, Stretch = Stretch.Uniform, Opacity = Math.Clamp(iconOpacity, 0, 1) });
        else panel.Children.Add(new TextBlock { Opacity = Math.Clamp(iconOpacity, 0, 1), Text = string.IsNullOrWhiteSpace(icon) ? "▤" : icon, Foreground = foreground, FontSize = !string.IsNullOrEmpty(topLabel) && !string.IsNullOrEmpty(label) ? 16 : string.IsNullOrEmpty(label) && string.IsNullOrEmpty(topLabel) ? 23 : 19, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(label)) panel.Children.Add(new TextBlock { Text = label, Foreground = foreground, FontSize = 9, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 0) });
        if (look != "Worn")
            return new Border { Opacity = Math.Clamp(opacity, 0, 1), Width = 44, Height = 44, Background = new SolidColorBrush(background), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = panel, Cursor = System.Windows.Input.Cursors.Hand };
        var outline = Geometry.Parse("M44,0 L7,0 5,2 3,2 1,5 2,8 0,11 1,19 0,22 1,31 0,35 2,38 2,41 6,44 44,44 Z").Clone();
        if (leftEdge) outline.Transform = new ScaleTransform(-1, 1, 22, 22);
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(new LinearGradientBrush(background, Color.FromRgb((byte)(background.R * .88), (byte)(background.G * .88), (byte)(background.B * .88)), 90), null, new Rect(0, 0, 44, 44));
            var grain = new Random(17);
            for (int i = 0; i < 100; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)grain.Next(5, 17), 35, 28, 18)), null, new Point(grain.NextDouble() * 44, grain.NextDouble() * 44), .3, .3);
            var texture = Asset("edge-tab-texture.png");
            if (texture != null) dc.DrawImage(texture, new Rect(0, 0, 44, 44));
            var wear = Geometry.Parse("M0,0 L12,0 8,3 9,5 5,4 2,10 0,9 Z M0,36 L3,39 6,40 5,44 0,44 Z").Clone();
            if (leftEdge) wear.Transform = new ScaleTransform(-1, 1, 22, 22);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(170, 245, 232, 195)), null, wear);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(65, 38, 30, 20)), 1), outline);
        }
        drawing.Freeze();
        return new Border { Opacity = Math.Clamp(opacity, 0, 1), Width = 44, Height = 44, Background = new DrawingBrush(drawing) { Stretch = Stretch.None }, Clip = outline, Child = panel, Cursor = System.Windows.Input.Cursors.Hand };
    }
    public static BitmapImage? IconImage(NoteData note) => Asset($"edge-icons/{note.Id}.png") ?? Asset("edge-icons/default.png");
    private static BitmapImage? Asset(string name)
    {
        try
        {
            var path = Path.Combine(App.CurrentApp.Store.Root, name);
            if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 256; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException) { return null; }
    }
}
internal sealed class EdgeTabEditor : StackPanel
{
    private readonly bool leftEdge;
    private readonly ComboBox style;
    private static readonly string?[] Looks = NotebookStyle.TabLooks.Select(l => (string?)l.Key).Prepend(null).ToArray();
    private string SelectedLook => Looks[Math.Max(0, style.SelectedIndex)] ?? NotebookStyle.DefaultTabLook;
    private readonly Slider size;
    private readonly TextBlock iconStatus = new() { FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly string iconFile;
    private readonly TextBox color;
    private readonly ComboBox icon;
    private readonly TextBox label;
    private readonly TextBox topLabel;
    private readonly Slider opacity;
    private readonly Slider iconOpacity;
    private readonly Border preview = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock warning = new() { Foreground = Brushes.Firebrick, FontSize = 11 };
    public bool IsValid => EdgeTabAppearance.IsValidColor(color.Text.Trim());
    public event Action? Changed;
    public EdgeTabEditor(NoteData note)
    {
        leftEdge = note.Edge == "Left"; iconFile = $"edge-icons/{note.Id}.png";
        Children.Add(new TextBlock { Text = "Edge tab appearance", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
        var styleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var styleCaption = new TextBlock { Text = "Tab style", Width = 96, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(styleCaption, Dock.Left);
        style = new ComboBox { ItemsSource = NotebookStyle.TabLooks.Select(l => l.Name).Prepend("App default").ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(Looks, note.TabStyle)), ToolTip = "This note's edge tab look, separate from its paper" };
        styleRow.Children.Add(styleCaption); styleRow.Children.Add(style); Children.Add(styleRow);
        var swatches = new WrapPanel(); Children.Add(swatches);
        color = new TextBox { Text = note.EdgeTabColor, MaxLength = 7, Padding = new Thickness(5), ToolTip = "Tab color in #RRGGBB format" };
        foreach (var hex in new[] { "#FFF1BA", "#B9DFC8", "#ACD4EF", "#D0BDF0", "#F2ADC0", "#303846" })
        {
            var button = new Button { Width = 32, Height = 25, Background = (Brush)new BrushConverter().ConvertFromString(hex)!, ToolTip = hex };
            button.Click += (_, _) => color.Text = hex; swatches.Children.Add(button);
        }
        Children.Add(color);
        Children.Add(new TextBlock { Text = "Icon · choose one or type a symbol / emoji", FontSize = 11, Margin = new Thickness(0, 8, 0, 4) });
        icon = new ComboBox { IsEditable = true, ItemsSource = new[] { "▤", "★", "♥", "✓", "!", "☕", "✦", "⌂", "▦", "◎", "✚", "⛤", "☰", "📌", "💡" }, Text = note.EdgeTabIcon }; Children.Add(icon);
        Children.Add(new TextBlock { Text = "Top label (up to 8 characters)", FontSize = 11, Margin = new Thickness(0, 8, 0, 4) });
        topLabel = new TextBox { Text = note.EdgeTabTopLabel, MaxLength = 8, Padding = new Thickness(5) }; Children.Add(topLabel);
        Children.Add(new TextBlock { Text = "Bottom label (up to 8 characters)", FontSize = 11, Margin = new Thickness(0, 8, 0, 4) });
        label = new TextBox { Text = note.EdgeTabLabel, MaxLength = 8, Padding = new Thickness(5) }; Children.Add(label);
        Slider OpacitySlider(string text, double value)
        {
            var caption = new TextBlock { Text = text + $" · {value * 100:0}%", Margin = new Thickness(0, 8, 0, 4) }; Children.Add(caption);
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = value * 100, TickFrequency = 1, IsSnapToTickEnabled = true }; Children.Add(slider);
            slider.ValueChanged += (_, _) => caption.Text = text + $" · {slider.Value:0}%";
            return slider;
        }
        var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var pickIcon = new Button { Content = "Custom icon…", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Use your own image (PNG, JPG…) as this tab's icon" };
        pickIcon.Click += (_, _) => PickIcon();
        var clearIcon = new Button { Content = "Remove", Margin = new Thickness(0), ToolTip = "Go back to the symbol above" };
        clearIcon.Click += (_, _) => { try { File.Delete(CustomIconPath); } catch (IOException) { } UpdatePreview(); };
        iconRow.Children.Add(pickIcon); iconRow.Children.Add(clearIcon); iconRow.Children.Add(iconStatus); Children.Add(iconRow);
        var sizeCaption = new TextBlock { Text = $"Tab size · {Math.Clamp(note.EdgeTabScale, .6, 1.6) * 100:0}%", Margin = new Thickness(0, 8, 0, 4) }; Children.Add(sizeCaption);
        size = new Slider { Minimum = 60, Maximum = 160, Value = Math.Clamp(note.EdgeTabScale, .6, 1.6) * 100, TickFrequency = 5, IsSnapToTickEnabled = true }; Children.Add(size);
        size.ValueChanged += (_, _) => { sizeCaption.Text = $"Tab size · {size.Value:0}%"; UpdatePreview(); };
        opacity = OpacitySlider("Whole tab opacity", note.EdgeTabOpacity);
        iconOpacity = OpacitySlider("Icon opacity", note.EdgeTabIconOpacity);
        Children.Add(new TextBlock { Text = "Live preview.", FontSize = 11, Margin = new Thickness(0, 8, 0, 0), Foreground = AppTheme.Muted });
        Children.Add(preview); Children.Add(warning);
        style.SelectionChanged += (_, _) => UpdatePreview();
        opacity.ValueChanged += (_, _) => UpdatePreview(); iconOpacity.ValueChanged += (_, _) => UpdatePreview(); topLabel.TextChanged += (_, _) => UpdatePreview();
        color.TextChanged += (_, _) => UpdatePreview(); label.TextChanged += (_, _) => UpdatePreview();
        icon.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        icon.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdatePreview));
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        preview.Child = EdgeTabAppearance.Create(color.Text.Trim(), icon.Text, label.Text, topLabel.Text, opacity.Value / 100, iconOpacity.Value / 100, leftEdge, iconFile, SelectedLook, size.Value / 100);
        iconStatus.Text = File.Exists(CustomIconPath) ? "Custom image in use" : "";
        iconStatus.Foreground = AppTheme.Muted;
        warning.Text = IsValid ? "" : "Use a color like #ACD4EF.";
        Changed?.Invoke();
    }
    private string CustomIconPath => Path.Combine(App.CurrentApp.Store.Root, iconFile);
    private void PickIcon()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a tab icon", Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.ico|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var source = new BitmapImage(); source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.UriSource = new Uri(dialog.FileName); source.DecodePixelWidth = 96; source.EndInit();
            Directory.CreateDirectory(Path.GetDirectoryName(CustomIconPath)!);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(CustomIconPath)) encoder.Save(stream);
            UpdatePreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException or System.Runtime.InteropServices.COMException)
        { warning.Text = "That image could not be used: " + ex.Message; }
    }
    public void Apply(NoteData note)
    {
        note.EdgeTabTopLabel = topLabel.Text.Trim(); note.EdgeTabOpacity = opacity.Value / 100; note.EdgeTabIconOpacity = iconOpacity.Value / 100;
        note.EdgeTabScale = size.Value / 100; note.TabStyle = Looks[Math.Max(0, style.SelectedIndex)];
        note.EdgeTabColor = color.Text.Trim(); note.EdgeTabIcon = icon.Text.Trim(); note.EdgeTabLabel = label.Text.Trim();
    }
}

