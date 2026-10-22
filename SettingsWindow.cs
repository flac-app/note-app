using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StickyNotes;
public sealed class SettingsWindow : Window
{
    private readonly StackPanel content = new() { Margin = new Thickness(24, 20, 24, 20) };
    private readonly DispatcherTimer saveDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    public SettingsWindow(NoteWindow owner)
    {
        Title = "Note settings"; Width = 420; Height = 640; MinHeight = 420; MinWidth = 360; ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AppTheme.Apply(this); FontFamily = new FontFamily("Segoe UI");
        var note = owner.Note;
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        content.Children.Add(new TextBlock { Text = "Note settings", FontSize = 20, FontWeight = FontWeights.SemiBold });
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); owner.Flush(); };
        void Changed(Action apply) { apply(); saveDelay.Stop(); saveDelay.Start(); }
        Closed += (_, _) => { if (saveDelay.IsEnabled) { saveDelay.Stop(); owner.Flush(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };

        Section("Paper");
        var colors = new WrapPanel();
        var customPaper = new TextBox { Text = note.Color, MaxLength = 7, Width = 84, Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(2, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Any paper color: #RRGGBB" };
        void MarkSelected()
        {
            foreach (var swatch in colors.Children.OfType<Button>())
                if (string.Equals((string)swatch.Tag, note.Color, StringComparison.OrdinalIgnoreCase)) swatch.SetResourceReference(BorderBrushProperty, "Ink");
                else swatch.BorderBrush = Brushes.Transparent;
        }
        foreach (var (color, name) in new[] { ("#000000", "Black"), ("#20242C", "Charcoal"), ("#264653", "Ocean"), ("#264D36", "Forest"), ("#503450", "Plum"), ("#7C2E37", "Wine"), ("#FFFFFF", "White"), ("#F7F5EF", "Paper"), ("#FFF1BA", "Butter"), ("#DCEBDC", "Sage"), ("#DDEAF5", "Sky"), ("#F1DFE8", "Rose"), ("#A89070", "Parchment"), ("#3B3026", "Leather") })
        {
            var swatch = new Button { Style = (Style)FindResource("Swatch"), Background = (Brush)new BrushConverter().ConvertFromString(color)!, ToolTip = name, Tag = color };
            swatch.Click += (_, _) => customPaper.Text = color; colors.Children.Add(swatch);
        }
        colors.Children.Add(customPaper); content.Children.Add(colors); MarkSelected();
        var paperStyle = new ComboBox { ItemsSource = new[] { "App default", "Clean", "Notebook" }, SelectedIndex = note.Paper switch { "Clean" => 1, "Notebook" => 2, _ => 0 }, Margin = new Thickness(0, 4, 0, 0), ToolTip = "This note's look; the default is set in the library" };
        paperStyle.SelectionChanged += (_, _) => Changed(() => owner.SetPaper(paperStyle.SelectedIndex switch { 1 => "Clean", 2 => "Notebook", _ => null }));
        Row("Style", paperStyle);
        var paperHint = Hint("Use a color like #ACD4EF."); paperHint.Visibility = Visibility.Collapsed; paperHint.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        customPaper.TextChanged += (_, _) =>
        {
            var value = customPaper.Text.Trim();
            bool valid = EdgeTabAppearance.IsValidColor(value);
            paperHint.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            if (valid && !string.Equals(value, note.Color, StringComparison.OrdinalIgnoreCase)) Changed(() => { note.Color = value; owner.ApplyAppearance(); });
            MarkSelected();
        };

        Section("Opacity");
        var opacity = PercentSlider("Resting", note.Opacity);
        opacity.ValueChanged += (_, _) => Changed(() => { note.Opacity = opacity.Value / 100; owner.ApplyAppearance(); });
        var hoverEnabled = new CheckBox { Content = "Different opacity on hover", IsChecked = note.HoverOpacityEnabled, Margin = new Thickness(0, 4, 0, 6) }; content.Children.Add(hoverEnabled);
        var hoverOpacity = PercentSlider("On hover", note.HoverOpacity);
        hoverOpacity.ValueChanged += (_, _) => Changed(() => { note.HoverOpacity = hoverOpacity.Value / 100; owner.ApplyAppearance(); });
        var hoverRow = (FrameworkElement)hoverOpacity.Parent;
        void HoverRow() { hoverOpacity.IsEnabled = hoverEnabled.IsChecked == true; hoverRow.Visibility = hoverOpacity.IsEnabled ? Visibility.Visible : Visibility.Collapsed; }
        Toggled(hoverEnabled, () => { HoverRow(); Changed(() => { note.HoverOpacityEnabled = hoverEnabled.IsChecked == true; owner.ApplyAppearance(); }); });
        HoverRow();

        Section("Compact · experimental");
        var compact = new CheckBox { Content = "Shrink to a title card unless hovered", IsChecked = note.ShrinkUnlessHovered, Name = "CompactToggle", ToolTip = "Hover to expand. Docked edge previews are unchanged." }; content.Children.Add(compact);
        var compactDetails = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; content.Children.Add(compactDetails);
        var compactHold = ValueSlider("Stay open after a click", 0, 60, Math.Clamp(note.CompactClickHoldSeconds, 0, 60), v => $"{v:0} s", compactDetails);
        compactHold.Name = "CompactHold";
        compactHold.ValueChanged += (_, _) => Changed(() => note.CompactClickHoldSeconds = (int)compactHold.Value);
        var compactSpeed = ValueSlider("Animation", 150, 1500, Math.Clamp(note.CompactAnimationMilliseconds, 150, 1500), v => $"{v / 1000:0.00} s", compactDetails, 50);
        compactSpeed.Name = "CompactSpeed";
        compactSpeed.ValueChanged += (_, _) => Changed(() => note.CompactAnimationMilliseconds = (int)compactSpeed.Value);
        void CompactRow() { compactHold.IsEnabled = compact.IsChecked == true; compactDetails.Visibility = compactHold.IsEnabled ? Visibility.Visible : Visibility.Collapsed; }
        Toggled(compact, () => { CompactRow(); Changed(() => { note.ShrinkUnlessHovered = compact.IsChecked == true; owner.RefreshCompactMode(); }); });
        CompactRow();

        Section("Behavior");
        var openOnStartup = new CheckBox { Content = "Open whenever note. starts", IsChecked = note.OpenOnStartup, Margin = new Thickness(0, 0, 0, 8) }; content.Children.Add(openOnStartup);
        Toggled(openOnStartup, () => Changed(() => note.OpenOnStartup = openOnStartup.IsChecked == true));
        var hideAltTab = new CheckBox { Content = "Hide from Alt+Tab and taskbar", IsChecked = note.HideFromAltTab, Margin = new Thickness(0, 0, 0, 8), Name = "HideAltTab" }; content.Children.Add(hideAltTab);
        Toggled(hideAltTab, () => Changed(() => { note.HideFromAltTab = hideAltTab.IsChecked == true; owner.ApplyAppearance(); }));
        content.Children.Add(new TextBlock { Text = "Show when these apps start", Margin = new Thickness(0, 12, 0, 0) });
        var apps = new TextBox { Text = note.Apps, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 4, 0, 0), ToolTip = "Executable names separated by commas. Checked every 3 seconds while note. runs." };
        content.Children.Add(apps);
        apps.TextChanged += (_, _) => Changed(() => { note.Apps = apps.Text; owner.ApplyAppearance(); });
        Hint("e.g. notepad, chrome, Code");

        Section("Screen edge");
        var edge = new ComboBox { ItemsSource = new[] { "Left", "Right" }, SelectedItem = note.Edge };
        Row("Collapse to", edge);
        edge.SelectionChanged += (_, _) => Changed(() => { note.Edge = (string?)edge.SelectedItem ?? "Right"; owner.ApplyAppearance(); });
        var autoHide = new CheckBox { Content = "Hide by itself", IsChecked = owner.AutoHideDelay != null, Margin = new Thickness(0, 4, 0, 8), Name = "AutoHide", ToolTip = "Off: an edge preview or slid-out page stays out until you click elsewhere or its tab" };
        content.Children.Add(autoHide);
        var autoHideDetails = new StackPanel(); content.Children.Add(autoHideDetails);
        int[] stops = { 0, 250, 500, 1000, 2000, 3000, 5000, 10000, 15000, 30000, 60000, 120000, 300000 };
        int StopFor(int? milliseconds) => milliseconds is int ms ? Enumerable.Range(1, stops.Length - 1).MinBy(i => Math.Abs(stops[i] - ms)) : 0;
        static string Duration(int ms) => ms < 1000 ? $"{ms / 1000.0:0.##} s" : ms < 60000 ? $"{ms / 1000} s" : ms % 60000 == 0 ? $"{ms / 60000} min" : $"{ms / 60000} min {ms % 60000 / 1000} s";
        string AppDefault()
        {
            var preferences = App.CurrentApp.Store.Preferences;
            if (NotebookStyle.Pull) return preferences.PullAutoCloseSeconds > 0 ? Duration(preferences.PullAutoCloseSeconds * 1000) : "never";
            return Duration(Math.Clamp(preferences.HoverCloseDelayMilliseconds, 0, 1500));
        }
        var autoHideDelay = ValueSlider("Hide after", 0, stops.Length - 1, StopFor(note.AutoHideMilliseconds), v => v == 0 ? "App default · " + AppDefault() : Duration(stops[(int)v]), autoHideDetails);
        autoHideDelay.Name = "AutoHideDelay";
        autoHideDelay.ValueChanged += (_, _) => Changed(() => note.AutoHideMilliseconds = autoHideDelay.Value == 0 ? null : stops[(int)autoHideDelay.Value]);
        var autoHideHint = new TextBlock { Text = "A preview hides this long after the pointer leaves it; a slid-out page once it has sat untouched this long.", FontSize = 11, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        autoHideHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); autoHideDetails.Children.Add(autoHideHint);
        void AutoHideRow() => autoHideDetails.Visibility = autoHide.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Toggled(autoHide, () => { Changed(() => owner.SetAutoHide(autoHide.IsChecked == true)); autoHideDelay.Value = StopFor(note.AutoHideMilliseconds); AutoHideRow(); });
        AutoHideRow();
        var edgeTabEditor = new EdgeTabEditor(note) { Visibility = Visibility.Collapsed };
        edgeTabEditor.Changed += () => { if (edgeTabEditor.IsValid) Changed(() => { edgeTabEditor.Apply(note); owner.ApplyAppearance(); App.CurrentApp.ArrangeTabs(owner.EdgeTab); }); };
        var customizeTab = new Button { Content = "Customize edge tab", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        customizeTab.Click += (_, _) => { bool show = edgeTabEditor.Visibility != Visibility.Visible; edgeTabEditor.Visibility = show ? Visibility.Visible : Visibility.Collapsed; customizeTab.Content = show ? "Hide edge tab options" : "Customize edge tab"; };
        content.Children.Add(customizeTab); content.Children.Add(edgeTabEditor);

        var divider = new Border { Height = 1, Margin = new Thickness(0, 24, 0, 12) }; divider.SetResourceReference(Border.BackgroundProperty, "Line"); content.Children.Add(divider);
        var delete = new Button { Content = "Delete note", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0) }; delete.SetResourceReference(ForegroundProperty, "Danger");
        delete.Click += (_, _) => { Close(); App.CurrentApp.Delete(owner); };
        content.Children.Add(delete);
    }
    private static void Toggled(CheckBox box, Action changed) { box.Checked += (_, _) => changed(); box.Unchecked += (_, _) => changed(); }
    private void Section(string text)
    {
        var heading = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(heading);
    }
    private TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(hint); return hint;
    }
    private void Row(string label, FrameworkElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var caption = new TextBlock { Text = label, Width = 96, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption); row.Children.Add(control); content.Children.Add(row);
    }
    private Slider ValueSlider(string label, double minimum, double maximum, double value, Func<double, string> format, Panel? host = null, double step = 1)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var top = new DockPanel();
        var shown = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right }; shown.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(shown, Dock.Right); top.Children.Add(shown); top.Children.Add(new TextBlock { Text = label });
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0) };
        slider.ValueChanged += (_, _) => shown.Text = format(slider.Value); shown.Text = format(slider.Value);
        block.Children.Add(top); block.Children.Add(slider); (host ?? content).Children.Add(block);
        return slider;
    }
    private Slider PercentSlider(string label, double fraction) => ValueSlider(label, 0, 100, Math.Clamp(fraction, 0, 1) * 100, v => $"{v:0}%");
}
internal sealed class LinkDialog : Window
{
    private readonly TextBox text = new() { MinWidth = 340, Margin = new Thickness(0, 8, 0, 14), Padding = new Thickness(6) };
    private readonly TextBox input = new() { MinWidth = 340, Margin = new Thickness(0, 8, 0, 12), Padding = new Thickness(6) };
    private LinkDialog(string? shown, string address, bool canRename)
    {
        AppTheme.Apply(this);
        bool editing = shown != null;
        Title = editing ? "Edit link" : "Add a link"; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        if (editing)
        {
            text.Text = shown; text.IsEnabled = canRename;
            if (!canRename) text.ToolTip = "This link wraps an image, so only its address can change";
            panel.Children.Add(new TextBlock { Text = "Text to show", FontWeight = FontWeights.SemiBold }); panel.Children.Add(text);
        }
        input.Text = address;
        panel.Children.Add(new TextBlock { Text = "Website, file, folder, note or app URL", FontWeight = FontWeights.SemiBold }); panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = @"https://example.com · C:\Documents · obsidian://open", FontSize = 11, Foreground = AppTheme.Muted });
        var actions = new DockPanel { Margin = new Thickness(0, 14, 0, 0) }; panel.Children.Add(actions);
        var browse = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(browse, Dock.Left); actions.Children.Add(browse);
        var file = new Button { Content = "File…", ToolTip = "Link to a file on this PC", Margin = new Thickness(0, 0, 6, 0) };
        file.Click += (_, _) => { var pick = new Microsoft.Win32.OpenFileDialog { Title = "Link a file" }; if (pick.ShowDialog(this) == true) input.Text = pick.FileName; };
        var folder = new Button { Content = "Folder…", ToolTip = "Link to a folder on this PC", Margin = new Thickness(0) };
        folder.Click += (_, _) => { var pick = new Microsoft.Win32.OpenFolderDialog { Title = "Link a folder" }; if (pick.ShowDialog(this) == true) input.Text = pick.FolderName; };
        browse.Children.Add(file); browse.Children.Add(folder);
        var button = new Button { Content = editing ? "Save" : "Add link", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0) }; button.Click += (_, _) => DialogResult = true; actions.Children.Add(button);
        Loaded += (_, _) => { var first = editing && canRename ? text : input; first.Focus(); first.SelectAll(); };
    }
    public static string? Ask(Window owner) { var dialog = new LinkDialog(null, "", false) { Owner = owner }; return dialog.ShowDialog() == true ? dialog.input.Text.Trim() : null; }
    public static (string Text, string Address)? Edit(Window owner, string text, string address, bool canRename)
    {
        var dialog = new LinkDialog(text, address, canRename) { Owner = owner };
        return dialog.ShowDialog() == true ? (dialog.text.Text.Trim(), dialog.input.Text.Trim()) : null;
    }
}
