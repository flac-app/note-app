using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
namespace StickyNotes;
public static class NoteLinks
{
    public static Uri For(Guid id) => new Uri("stickynote://note/" + id.ToString("D"));
    public static bool TryGetId(Uri uri, out Guid id)
    {
        id = Guid.Empty;
        return uri.IsAbsoluteUri && uri.Scheme == "stickynote" && uri.Host == "note" && Guid.TryParse(uri.AbsolutePath.Trim('/'), out id);
    }
}
public sealed partial class LibraryWindow : Window
{
    private const string CreditUrl = "https://github.com/flac-app";
    private const double CreditHeight = 22;
    private readonly Border creditDrawer = new() { Height = 0, ClipToBounds = true, Margin = new Thickness(2, -12, 0, 12) };
    private readonly Border creditHost = new() { VerticalAlignment = VerticalAlignment.Top, Opacity = 0, RenderTransform = new TranslateTransform(0, -CreditHeight) };
    private readonly TextBlock creditLink = new() { Text = "flac.app studios", FontSize = 12, Cursor = Cursors.Hand, ToolTip = "github.com/flac-app" };
    private bool creditOpen;
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly TextBox search = new() { Padding = new Thickness(30, 8, 8, 8), BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StackPanel rows = new();
    private readonly ScrollViewer notesView = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button themeToggle = new();
    private readonly StackPanel settingsPanel = new();
    private Window? preferencesWindow;
    private readonly StackPanel hotkeysPanel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 16), RenderTransform = new TranslateTransform() };
    private readonly StackPanel globalShortcuts = new();
    private readonly StackPanel shortcutTiming = new();
    private readonly StackPanel appShortcuts = new();
    private readonly Grid fixedShortcuts = new();
    private readonly TextBlock recordHint = new() { FontSize = 12, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer hintTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private IKeyCombo? recording;
    private string? undoPreferences;
    private readonly TextBlock count = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 5) };
    private readonly TextBlock startupHint = new() { FontSize = 11, Margin = new Thickness(0, -6, 0, 12), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly CheckBox systemStartup = new() { Content = "Open on system startup", Margin = new Thickness(0, 0, 0, 12), ToolTip = "Start note. when you sign in to Windows. If you move the EXE, turn this off and on again from its new location." };
    private TextBlock dataFolder = new(), dataStatus = new();
    private readonly DispatcherTimer saveDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private Brush Muted => (Brush)Resources["Muted"];
    public LibraryWindow()
    {
        AppTheme.ApplyIcon(this);
        Title = "note."; Width = 560; Height = 560; MinWidth = 400; MinHeight = 340;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/note.app;component/LibraryTheme.xaml", UriKind.Relative) });
        SourceInitialized += (_, _) => ApplyNativeTheme();
        FontFamily = new FontFamily("Segoe UI");
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); App.CurrentApp.Save(); };
        var layout = new DockPanel { Margin = new Thickness(28, 24, 28, 20) }; Content = layout;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(tools, Dock.Right); header.Children.Add(tools);
        tools.Children.Add(IconButton("\uE710", "New note", () => App.CurrentApp.NewNote()));
        BuildViewToggle(); tools.Children.Add(viewToggle);
        themeToggle.Style = (Style)FindResource("IconButton");
        themeToggle.Click += (_, _) => { App.CurrentApp.Store.Preferences.DarkMode = !App.CurrentApp.Store.Preferences.DarkMode; ApplyTheme(); App.CurrentApp.Save(); };
        tools.Children.Add(themeToggle);
        tools.Children.Add(IconButton("\uE765", "Keyboard shortcuts", ToggleHotkeys));
        tools.Children.Add(IconButton("\uE713", "Preferences", ToggleSettings));
        var brand = new TextBlock { Text = "note.", FontSize = 26, FontFamily = AppFonts.Brand, Cursor = Cursors.Hand };
        brand.MouseLeftButtonUp += (_, _) => BrandClicked();
        header.Children.Add(brand);
        header.Children.Add(count);
        BuildCredit(); DockPanel.SetDock(creditDrawer, Dock.Top); layout.Children.Add(creditDrawer);
        systemStartup.Click += (_, _) =>
        {
            try { StartupSettings.SetEnabled(systemStartup.IsChecked == true); }
            catch (Exception ex) { MessageBox.Show(this, "Windows startup setting could not be changed.\n" + ex.Message, "note."); }
            RefreshSystemStartup();
        };
        RefreshSystemStartup();
        BuildSettingsPanel();
        hintTimer.Tick += (_, _) => { hintTimer.Stop(); if (recording == null) recordHint.Visibility = Visibility.Collapsed; };
        DockPanel.SetDock(hotkeysPanel, Dock.Top); layout.Children.Add(hotkeysPanel); AddHotkeys(hotkeysPanel);
        var searchBox = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        searchBox.SetResourceReference(Border.BackgroundProperty, "Surface"); searchBox.SetResourceReference(Border.BorderBrushProperty, "Line");
        var searchGrid = new Grid(); searchBox.Child = searchGrid;
        var searchIcon = new TextBlock { Text = "\uE721", FontFamily = new FontFamily(IconFont), FontSize = 12, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var placeholder = new TextBlock { Text = "Search", Margin = new Thickness(32, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        searchIcon.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        searchGrid.Children.Add(searchIcon); searchGrid.Children.Add(placeholder); searchGrid.Children.Add(search);
        search.ToolTip = "Search notes by title";
        search.TextChanged += (_, _) => { placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden; Refresh(); };
        DockPanel.SetDock(searchBox, Dock.Top); layout.Children.Add(searchBox);
        BuildSelectionBar(); DockPanel.SetDock(selectionBar, Dock.Top); layout.Children.Add(selectionBar);
        layout.Children.Add(notesView);
        ApplyTheme();
        Closing += (_, e) => { if (!App.CurrentApp.Exiting) { e.Cancel = true; Hide(); } };
        IsVisibleChanged += (_, _) => { if (!IsVisible) preferencesWindow?.Hide(); };
        Activated += (_, _) => { Refresh(); RefreshSystemStartup(); }; Refresh();
        Deactivated += (_, _) => { if (recording != null) StopRecording(); };
        PreviewKeyDown += (_, e) =>
        {
            if (recording != null) { e.Handled = true; RecordKey(e); return; }
            var command = CommandShortcut.Match(App.CurrentApp.Store.Preferences, Keyboard.Modifiers, e.Key == Key.System ? e.SystemKey : e.Key);
            if (command == AppCommand.NewNote) { App.CurrentApp.NewNote(); e.Handled = true; }
            else if (command == AppCommand.Search) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && selectedNotes.Count > 0) { ClearSelection(); e.Handled = true; }
            else if (e.Key == Key.Escape && search.Text.Length > 0) { search.Clear(); e.Handled = true; }
        };
    }
    private static Duration Fade(int milliseconds) => TimeSpan.FromMilliseconds(App.CurrentApp.Store.Preferences.SmoothHover ? milliseconds : 0);
    private static void Animate(IAnimatable target, DependencyProperty property, double to, int milliseconds, EventHandler? completed = null)
    {
        var animation = new DoubleAnimation(to, Fade(milliseconds)) { EasingFunction = Ease };
        if (completed != null) animation.Completed += completed;
        target.BeginAnimation(property, animation);
    }
    private void BuildCredit()
    {
        creditLink.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        creditLink.MouseEnter += (_, _) => creditLink.TextDecorations = TextDecorations.Underline;
        creditLink.MouseLeave += (_, _) => creditLink.TextDecorations = null;
        creditLink.MouseLeftButtonUp += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(CreditUrl) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        };
        creditHost.Child = creditLink; creditDrawer.Child = creditHost;
    }
    private void BrandClicked()
    {
        bool handled = false; EasterEggClick(ref handled);
        if (!handled) SetCreditOpen(!creditOpen);
    }
    partial void EasterEggClick(ref bool handled);
    private void SetCreditOpen(bool open, Action? closed = null)
    {
        creditOpen = open;
        var duration = TimeSpan.FromMilliseconds(open ? 260 : 200);
        var height = new DoubleAnimation(open ? CreditHeight : 0, duration) { EasingFunction = Ease };
        if (closed != null) height.Completed += (_, _) => { if (!creditOpen) closed(); };
        creditDrawer.BeginAnimation(HeightProperty, height);
        creditHost.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(open ? 0 : -CreditHeight, duration) { EasingFunction = Ease });
        creditHost.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, duration) { EasingFunction = Ease });
    }
    private void ToggleSettings()
    {
        if (preferencesWindow is { IsVisible: true }) { preferencesWindow.Hide(); return; }
        preferencesWindow ??= CreatePreferencesWindow();
        ApplyPreferencesTheme();
        var area = SystemParameters.WorkArea;
        preferencesWindow.Height = Math.Min(760, area.Height - 40);
        double left = Left + ActualWidth + 12 + preferencesWindow.Width <= area.Right ? Left + ActualWidth + 12 : Math.Max(area.Left, Left + 40);
        preferencesWindow.Left = Math.Min(left, area.Right - preferencesWindow.Width);
        preferencesWindow.Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - preferencesWindow.Height));
        preferencesWindow.Show(); preferencesWindow.Activate();
    }
    private Window CreatePreferencesWindow()
    {
        var window = new Window { Title = "note. preferences", Width = 520, MinWidth = 420, MinHeight = 260, Owner = this, ShowInTaskbar = false, FontFamily = FontFamily, WindowStartupLocation = WindowStartupLocation.Manual };
        AppTheme.Apply(window);
        var heading = new TextBlock { Text = "Preferences", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        var page = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        page.Children.Add(heading); page.Children.Add(settingsPanel);
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetResourceReference(BackgroundProperty, "Page");
        window.Content = scroll;
        window.Closing += (_, e) => { if (!App.CurrentApp.Exiting) { e.Cancel = true; window.Hide(); } };
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && recording == null) { window.Hide(); e.Handled = true; } };
        return window;
    }
    private void ApplyPreferencesTheme()
    {
        if (preferencesWindow == null) return;
        AppTheme.ApplyBrushes(preferencesWindow);
        preferencesWindow.Background = (Brush)preferencesWindow.Resources["Page"]; preferencesWindow.Foreground = (Brush)preferencesWindow.Resources["Ink"];
        AppTheme.ApplyNativeTheme(preferencesWindow);
    }
    private void ToggleHotkeys() => SetPanelOpen(hotkeysPanel, hotkeysPanel.Visibility != Visibility.Visible);
    private static void SetPanelOpen(StackPanel panel, bool open)
    {
        var slide = (TranslateTransform)panel.RenderTransform;
        if (open)
        {
            panel.Visibility = Visibility.Visible;
            panel.BeginAnimation(OpacityProperty, null); slide.BeginAnimation(TranslateTransform.YProperty, null);
            panel.Opacity = 0; slide.Y = -8;
            Animate(panel, OpacityProperty, 1, 200); Animate(slide, TranslateTransform.YProperty, 0, 200);
        }
        else
        {
            Animate(slide, TranslateTransform.YProperty, -8, 150);
            Animate(panel, OpacityProperty, 0, 150, (_, _) => { if (panel.Opacity == 0) panel.Visibility = Visibility.Collapsed; });
        }
    }
    private void AddHotkeys(StackPanel panel)
    {
        var inner = new StackPanel();
        panel.Children.Add(new ScrollViewer { Content = inner, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) });
        recordHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); inner.Children.Add(recordHint);
        inner.Children.Add(SectionHeading("Global · works anywhere", 0));
        inner.Children.Add(globalShortcuts);
        var add = new Button { Content = "+ Add shortcut", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
        add.Click += (_, _) => { var binding = new ShortcutBinding { Action = ShortcutAction.ShowLibrary }; App.CurrentApp.Store.Preferences.Shortcuts.Add(binding); StartRecording(binding); };
        inner.Children.Add(add); inner.Children.Add(shortcutTiming);
        inner.Children.Add(SectionHeading("In note. windows", 16));
        inner.Children.Add(appShortcuts);
        inner.Children.Add(SectionHeading("Fixed", 14));
        fixedShortcuts.ColumnDefinitions.Add(new ColumnDefinition()); fixedShortcuts.ColumnDefinitions.Add(new ColumnDefinition());
        var fixedKeys = new[] { ("Esc", "Clear search"), ("Ctrl + Click", "Open a link"), ("Ctrl + Z", "Undo"), ("Ctrl + Y", "Redo"), ("Alt + F4", "Hide note in tray") };
        for (int i = 0; i < fixedKeys.Length; i++)
        {
            if (i % 2 == 0) fixedShortcuts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            item.Children.Add(KeyChip(fixedKeys[i].Item1)); item.Children.Add(new TextBlock { Text = fixedKeys[i].Item2, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(item, i / 2); Grid.SetColumn(item, i % 2); fixedShortcuts.Children.Add(item);
        }
        inner.Children.Add(fixedShortcuts);
        var reset = new Button { Content = "Reset shortcuts", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), ToolTip = "Restore the default key bindings" };
        reset.Click += (_, _) =>
        {
            var preferences = App.CurrentApp.Store.Preferences;
            preferences.Shortcuts = ShortcutBinding.Defaults(); preferences.Commands = CommandShortcut.Defaults();
            recording = null; App.CurrentApp.ApplyShortcuts(); RebuildShortcuts(); Flash("Shortcuts restored to defaults");
        };
        inner.Children.Add(reset);
        RebuildShortcuts();
        var divider = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 0) }; divider.SetResourceReference(Border.BackgroundProperty, "Line");
        panel.Children.Add(divider);
    }
    private static Border KeyChip(string keys)
    {
        var chip = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(6, 0, 6, 1), Child = new TextBlock { Text = keys, FontSize = 11 } };
        chip.SetResourceReference(Border.BackgroundProperty, "Surface"); chip.SetResourceReference(Border.BorderBrushProperty, "Line");
        return chip;
    }
    private static TextBlock SectionHeading(string text, double top)
    {
        var heading = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 8) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return heading;
    }
    private Button KeysButton(IKeyCombo combo, bool blocked)
    {
        var keys = new Button { Content = combo == recording ? "Press keys…" : ShortcutBinding.Format(combo.Modifiers, combo.Key), Margin = new Thickness(0), Padding = new Thickness(8, 5, 8, 5),
            ToolTip = blocked ? "Windows or another app already uses this combination. Click to choose another." : "Click, then press a new key combination" };
        if (blocked) keys.SetResourceReference(Control.ForegroundProperty, "Danger");
        else if (combo.Key == Key.None && combo != recording) keys.SetResourceReference(Control.ForegroundProperty, "Muted");
        keys.Click += (_, _) => StartRecording(combo);
        return keys;
    }
    private void RebuildShortcuts()
    {
        var preferences = App.CurrentApp.Store.Preferences; var failed = App.CurrentApp.Hotkeys.Failed;
        void Later(Action action) => Dispatcher.BeginInvoke(action);
        Button Remove(string tooltip, Action remove)
        {
            var button = IconButton("\uE711", tooltip, remove);
            button.Width = button.Height = 28; button.FontSize = 11; DockPanel.SetDock(button, Dock.Right); return button;
        }
        globalShortcuts.Children.Clear();
        foreach (var binding in preferences.Shortcuts.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(Remove("Remove shortcut", () => { preferences.Shortcuts.Remove(binding); if (recording == binding) recording = null; App.CurrentApp.ApplyShortcuts(); RebuildShortcuts(); }));
            var trigger = new ComboBox { ItemsSource = Enum.GetValues<ShortcutTrigger>().Select(ShortcutBinding.Label).ToArray(), SelectedIndex = (int)binding.Trigger, Width = 112, Margin = new Thickness(6, 0, 0, 0), ToolTip = "How the keys must be pressed" };
            trigger.SelectionChanged += (_, _) => { binding.Trigger = (ShortcutTrigger)trigger.SelectedIndex; Later(() => { App.CurrentApp.ApplyShortcuts(); RebuildShortcuts(); }); };
            DockPanel.SetDock(trigger, Dock.Right); row.Children.Add(trigger);
            var action = new ComboBox { ItemsSource = Enum.GetValues<ShortcutAction>().Select(ShortcutBinding.Label).ToArray(), SelectedIndex = (int)binding.Action, Width = 128, Margin = new Thickness(0, 0, 6, 0) };
            action.SelectionChanged += (_, _) => { binding.Action = (ShortcutAction)action.SelectedIndex; App.CurrentApp.Save(); };
            DockPanel.SetDock(action, Dock.Left); row.Children.Add(action);
            row.Children.Add(KeysButton(binding, failed.Contains(binding)));
            globalShortcuts.Children.Add(row);
        }
        appShortcuts.Children.Clear();
        foreach (var command in preferences.Commands)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var remove = Remove("Remove this shortcut", () => { command.Key = Key.None; command.Modifiers = ModifierKeys.None; if (recording == command) recording = null; App.CurrentApp.ApplyShortcuts(); RebuildShortcuts(); });
            remove.IsEnabled = command.Key != Key.None; row.Children.Add(remove);
            var label = new TextBlock { Text = command.Name, Width = 134, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left); row.Children.Add(label);
            row.Children.Add(KeysButton(command, false));
            appShortcuts.Children.Add(row);
        }
        shortcutTiming.Children.Clear();
        Slider Timing(string label, int minimum, int maximum, int value, Action<int> set)
        {
            var caption = new TextBlock { Text = $"{label} · {value} ms", Margin = new Thickness(0, 10, 0, 0) }; shortcutTiming.Children.Add(caption);
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = 25, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0) };
            slider.ValueChanged += (_, _) => { set((int)slider.Value); caption.Text = $"{label} · {(int)slider.Value} ms"; saveDelay.Stop(); saveDelay.Start(); };
            shortcutTiming.Children.Add(slider); return slider;
        }
        if (preferences.Shortcuts.Any(b => b.Trigger == ShortcutTrigger.DoublePress))
            Timing("Double press within", 150, 800, Math.Clamp(preferences.DoublePressMilliseconds, 150, 800), v => preferences.DoublePressMilliseconds = v);
        if (preferences.Shortcuts.Any(b => b.Trigger == ShortcutTrigger.Hold))
            Timing("Hold for", 200, 1500, Math.Clamp(preferences.HoldMilliseconds, 200, 1500), v => preferences.HoldMilliseconds = v);
    }
    private void Flash(string message)
    {
        recordHint.Text = message; recordHint.Visibility = Visibility.Visible;
        hintTimer.Stop(); hintTimer.Start();
    }
    private void StartRecording(IKeyCombo combo)
    {
        recording = combo; App.CurrentApp.Hotkeys.Suspend(); hintTimer.Stop();
        recordHint.Text = "Press a key combination · Esc to cancel"; recordHint.Visibility = Visibility.Visible;
        RebuildShortcuts();
    }
    private void StopRecording(string? message = null)
    {
        var preferences = App.CurrentApp.Store.Preferences;
        if (recording is ShortcutBinding { Key: Key.None } unfinished) preferences.Shortcuts.Remove(unfinished);
        recording = null;
        if (message != null) Flash(message); else recordHint.Visibility = Visibility.Collapsed;
        App.CurrentApp.ApplyShortcuts(); RebuildShortcuts();
    }
    private void RecordKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (recording == null || key == Key.None || ShortcutBinding.IsModifier(key)) return;
        if (key == Key.Escape && modifiers == ModifierKeys.None) { StopRecording(); return; }
        if (modifiers == ModifierKeys.None && !ShortcutBinding.IsFunctionKey(key)) { recordHint.Text = "Add Ctrl, Alt, Shift or Win, or use an F-key"; return; }
        var preferences = App.CurrentApp.Store.Preferences;
        var previous = preferences.Shortcuts.Cast<IKeyCombo>().Concat(preferences.Commands)
            .Where(c => c != recording && c.Key == key && c.Modifiers == modifiers).ToArray();
        foreach (var combo in previous)
        {
            if (combo is ShortcutBinding binding) preferences.Shortcuts.Remove(binding);
            else { combo.Key = Key.None; combo.Modifiers = ModifierKeys.None; }
        }
        recording.Key = key; recording.Modifiers = modifiers;
        StopRecording(previous.Length > 0 ? $"Moved from “{previous[0].Name}”" : null);
    }
    private Button IconButton(string glyph, string tooltip, Action click)
    {
        var button = new Button { Content = glyph, ToolTip = tooltip, Style = (Style)FindResource("IconButton") };
        button.Click += (_, _) => click(); return button;
    }
    private void RefreshSystemStartup()
    {
        try
        {
            systemStartup.IsChecked = StartupSettings.IsEnabled(); systemStartup.IsEnabled = true;
            var other = systemStartup.IsChecked == true ? null : StartupSettings.RegisteredPath();
            startupHint.Text = other == null ? "" : "Windows currently starts another copy: " + other + ". Turn this on to start this copy instead, or off to start neither.";
            startupHint.Visibility = other == null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception) { systemStartup.IsChecked = false; systemStartup.IsEnabled = false; startupHint.Visibility = Visibility.Collapsed; }
    }
    private void BuildSettingsPanel()
    {
        settingsPanel.Children.Clear();
        settingsPanel.Children.Add(systemStartup);
        startupHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); settingsPanel.Children.Add(startupHint);
        AddDataControls(settingsPanel);
        AddSoundControls(settingsPanel);
        AddAppearanceControls(settingsPanel);
    }
    private void AddDataControls(StackPanel panel)
    {
        var store = App.CurrentApp.Store;
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; panel.Children.Add(section);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var caption = new TextBlock { Text = "Notes folder", Width = 150, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption);
        dataFolder = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        dataFolder.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        row.Children.Add(dataFolder); section.Children.Add(row); RefreshDataFolder();
        var buttons = new WrapPanel();
        Button Add(string text, string tooltip, Action click)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), ToolTip = tooltip };
            button.Click += (_, _) => click(); buttons.Children.Add(button); return button;
        }
        Add("Back up…", "Save every note, image and setting into one .zip file", BackUp);
        var move = Add("Change location…", "Keep your notes in another folder, such as a synced or external drive", ChangeLocation);
        if (store.LocationLocked) { move.IsEnabled = false; move.ToolTip = "Set by STICKYNOTES_DATA_DIR"; ToolTipService.SetShowOnDisabled(move, true); }
        Add("Open folder", "Show the notes folder in File Explorer", () => Process.Start(new ProcessStartInfo(store.Root) { UseShellExecute = true }));
        section.Children.Add(buttons);
        dataStatus = new TextBlock { FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        dataStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); section.Children.Add(dataStatus);
    }
    private void AddSoundControls(StackPanel panel)
    {
        var preferences = App.CurrentApp.Store.Preferences;
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; panel.Children.Add(section);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var caption = new TextBlock { Text = "Reminder sound", Width = 150, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption);
        var preview = new Button { Content = "▶", ToolTip = "Play this sound", MinWidth = 0, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(preview, Dock.Right); row.Children.Add(preview);
        var choice = new ComboBox { MinWidth = 160 }; row.Children.Add(choice); section.Children.Add(row);
        string[] values = Array.Empty<string>();
        void Fill()
        {
            var files = ReminderSounds.Available();
            values = new[] { "" }.Concat(files).ToArray();
            choice.ItemsSource = new[] { "None" }.Concat(files.Select(ReminderSounds.Label)).ToArray();
            int index = Array.FindIndex(values, v => string.Equals(v, preferences.ReminderSound, StringComparison.OrdinalIgnoreCase));
            choice.SelectedIndex = Math.Max(0, index);
        }
        Fill();
        choice.DropDownOpened += (_, _) => Fill();
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedIndex < 0 || choice.SelectedIndex >= values.Length || values[choice.SelectedIndex] == preferences.ReminderSound) return;
            preferences.ReminderSound = values[choice.SelectedIndex]; App.CurrentApp.Save();
        };
        preview.Click += (_, _) => ReminderSounds.Play(preferences.ReminderSound);
        var buttons = new WrapPanel();
        var add = new Button { Content = "Add sound…", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Copy a sound file into the sounds folder and use it for reminders" };
        add.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Add a reminder sound", Filter = ReminderSounds.FileFilter };
            if (dialog.ShowDialog(this) != true) return;
            try { preferences.ReminderSound = ReminderSounds.Add(dialog.FileName); App.CurrentApp.Save(); Fill(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ConfirmDialog.Inform(this, "Sound not added", ex.Message); }
        };
        var open = new Button { Content = "Open folder", Margin = new Thickness(0), ToolTip = "Show the sounds folder; sound files you put there appear in the list" };
        open.Click += (_, _) => { ReminderSounds.InstallDefaults(); Directory.CreateDirectory(ReminderSounds.Folder); Process.Start(new ProcessStartInfo(ReminderSounds.Folder) { UseShellExecute = true }); };
        var volumeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var volumeCaption = new TextBlock { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(volumeCaption, Dock.Left); volumeRow.Children.Add(volumeCaption);
        var volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Math.Clamp(preferences.ReminderVolume, 0, 100), VerticalAlignment = VerticalAlignment.Center, ToolTip = "How loud reminder sounds play; ▶ previews it" };
        void VolumeCaption() => volumeCaption.Text = "Volume · " + (volume.Value == 0 ? "muted" : $"{volume.Value:0}%");
        volume.ValueChanged += (_, _) => { preferences.ReminderVolume = (int)volume.Value; ReminderSounds.ApplyVolume(); VolumeCaption(); saveDelay.Stop(); saveDelay.Start(); };
        VolumeCaption(); volumeRow.Children.Add(volume); section.Children.Add(volumeRow);
        var loops = new CheckBox { Content = "Repeat the sound until the reminder is dismissed", IsChecked = preferences.ReminderSoundLoops, Margin = new Thickness(0, 0, 0, 8) };
        loops.Click += (_, _) => { preferences.ReminderSoundLoops = loops.IsChecked == true; App.CurrentApp.Save(); };
        section.Children.Add(loops);
        var popupRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var previewPopup = new Button { Content = "Preview", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Show a sample reminder pop-up" };
        previewPopup.Click += (_, _) => ReminderPopup.Preview();
        DockPanel.SetDock(previewPopup, Dock.Right); popupRow.Children.Add(previewPopup);
        var popup = new CheckBox { Content = "Show a pop-up when a reminder rings", IsChecked = preferences.ReminderPopup, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Off: the sound still plays and the note still opens. Each reminder can choose for itself." };
        popup.Click += (_, _) => { preferences.ReminderPopup = popup.IsChecked == true; App.CurrentApp.Save(); };
        popupRow.Children.Add(popup); section.Children.Add(popupRow);
        buttons.Children.Add(add); buttons.Children.Add(open); section.Children.Add(buttons);
        var hint = new TextBlock { Text = "Sounds live in the sounds folder inside your notes folder, so backups include them. Each reminder can pick its own sound.", FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); section.Children.Add(hint);
    }
    private void RefreshDataFolder() { dataFolder.Text = App.CurrentApp.Store.Root; dataFolder.ToolTip = App.CurrentApp.Store.Root; }
    private void ShowDataStatus(string message) { dataStatus.Text = message; dataStatus.Visibility = Visibility.Visible; }
    private void BackUp()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Back up notes", FileName = NoteStore.BackupName(), DefaultExt = ".zip", Filter = "Zip archive (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!App.CurrentApp.SaveEverything()) { MessageBox.Show(this, "A note could not be saved, so no backup was made. Its window shows why.", "note."); return; }
        try { App.CurrentApp.Store.Backup(dialog.FileName); ShowDataStatus("Backup saved to " + dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { MessageBox.Show(this, "The backup could not be saved.\n" + ex.Message, "note."); }
    }
    private void ChangeLocation()
    {
        var store = App.CurrentApp.Store;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder for your notes", InitialDirectory = store.Root };
        if (dialog.ShowDialog(this) != true || NoteStore.SameFolder(dialog.FolderName, store.Root)) return;
        var folder = dialog.FolderName;
        if (NoteStore.HasData(folder) && !ConfirmDialog.Ask(this, "Replace notes in that folder?", "That folder already holds note. data. It will be replaced by your current notes.", "Replace", danger: true)) return;
        if (!App.CurrentApp.SaveEverything()) { MessageBox.Show(this, "A note could not be saved, so your notes were not moved. Its window shows why.", "note."); return; }
        var previous = store.Root;
        try { store.MoveTo(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { MessageBox.Show(this, "Your notes could not be moved and are still saved in\n" + previous + "\n\n" + ex.Message, "note."); return; }
        RefreshDataFolder();
        ShowDataStatus("Notes are now saved here. A copy stays in " + previous + " until you delete it.");
    }
    private static void ApplyToNotes()
    {
        foreach (var note in Application.Current.Windows.OfType<NoteWindow>()) { note.ApplyAppearance(); note.RefreshInteractionPreferences(); }
        App.CurrentApp.ArrangeTabs();
    }
    private void AddAppearanceControls(StackPanel panel)
    {
        var preferences = App.CurrentApp.Store.Preferences;
        void Apply() { ApplyToNotes(); App.CurrentApp.Save(); }
        ComboBox Choice(string label, string[] options, int selected)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var caption = new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption);
            var choice = new ComboBox { ItemsSource = options, SelectedIndex = selected, MinWidth = 180 };
            row.Children.Add(choice); panel.Children.Add(row); return choice;
        }
        Slider Range(Panel host, int minimum, int maximum, int step, int value, string tooltip, Func<int, string> caption, Action<int> set)
        {
            var label = new TextBlock { Text = caption(value), Margin = new Thickness(0, 10, 0, 0) };
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 6, 0, 0), ToolTip = tooltip };
            slider.ValueChanged += (_, _) => { set((int)slider.Value); label.Text = caption((int)slider.Value); saveDelay.Stop(); saveDelay.Start(); };
            host.Children.Add(label); host.Children.Add(slider); return slider;
        }
        var paper = Choice("Note style", new[] { "Clean", "Notebook" }, preferences.NotebookStyle ? 1 : 0);
        paper.ToolTip = "Default paper for notes. Each note can override it from its ⋯ menu.";
        paper.SelectionChanged += (_, _) => { preferences.NotebookStyle = paper.SelectedIndex == 1; Apply(); };
        int tabLook = preferences.NotebookTabs == true ? (preferences.SmoothSeparators ? 3 : 2) : preferences.WornTabs ? 1 : 0;
        var tabs = Choice("Tab style", NotebookStyle.TabLooks.Select(l => l.Name).ToArray(), tabLook);
        tabs.ToolTip = "Default edge tab look, separate from the paper. Each note can override it from its ⋯ menu.";
        tabs.SelectionChanged += (_, _) =>
        {
            preferences.WornTabs = tabs.SelectedIndex == 1; preferences.NotebookTabs = tabs.SelectedIndex >= 2; preferences.SmoothSeparators = tabs.SelectedIndex == 3;
            Apply();
        };
        var hover = Choice("Hover out mode", new[] { "Instant", "Smooth slide", "Notebook style" }, !preferences.SmoothHover ? 0 : preferences.NotebookPull ? 2 : 1);
        hover.ToolTip = "Notebook style: edge tabs stack like notebook separators; click one to slide its page out as an editable note";
        var autoCloseRow = new StackPanel();
        panel.Children.Add(autoCloseRow);
        static string AutoCloseCaption(int seconds) => "Slide back after · " + (seconds == 0 ? "never" : seconds < 60 ? $"{seconds} s" : seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds / 60} min {seconds % 60} s");
        Range(autoCloseRow, 0, 300, 5, Math.Clamp(preferences.PullAutoCloseSeconds, 0, 300), "How long a slid-out page may sit untouched before it slides back; 0 keeps it out",
            AutoCloseCaption, v => preferences.PullAutoCloseSeconds = v);
        void AutoCloseRow() => autoCloseRow.Visibility = preferences.NotebookPull && preferences.SmoothHover ? Visibility.Visible : Visibility.Collapsed;
        AutoCloseRow();
        hover.SelectionChanged += (_, _) =>
        {
            preferences.SmoothHover = hover.SelectedIndex != 0; preferences.NotebookPull = hover.SelectedIndex == 2;
            AutoCloseRow(); Apply();
        };
        Range(panel, 0, 1500, 50, Math.Clamp(preferences.HoverCloseDelayMilliseconds, 0, 1500), "Delay after leaving the tab or preview; 0 closes immediately",
            v => $"Close preview after · {v} ms", v => preferences.HoverCloseDelayMilliseconds = v);
        Range(panel, 0, 20, 1, Math.Clamp(preferences.CornerRadius, 0, 20), "Notebook pages use a third of this for a squarer page",
            v => $"Corner rounding · {v} px", v => { preferences.CornerRadius = v; ApplyToNotes(); });
        var resetRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var reset = new Button { Content = "Reset all settings", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Restore default app preferences and shortcuts. Notes are not changed." };
        reset.SetResourceReference(Control.ForegroundProperty, "Danger");
        reset.Click += (_, _) => ResetAllSettings();
        var undo = new Button { Content = "Undo reset", Margin = new Thickness(0), Visibility = undoPreferences == null ? Visibility.Collapsed : Visibility.Visible };
        undo.Click += (_, _) => UndoReset();
        resetRow.Children.Add(reset); resetRow.Children.Add(undo); panel.Children.Add(resetRow);
    }
    private void ResetAllSettings()
    {
        if (!ConfirmDialog.Ask(this, "Reset all settings?", "App preferences, style and shortcuts return to their defaults. Your notes and their contents are not changed. You can undo this until note. closes.", "Reset", danger: true)) return;
        undoPreferences = App.CurrentApp.Store.PreferencesJson();
        App.CurrentApp.Store.ResetPreferences();
        AfterPreferencesReplaced();
    }
    private void UndoReset()
    {
        if (undoPreferences == null) return;
        App.CurrentApp.Store.ResetPreferences(NoteStore.ParsePreferences(undoPreferences));
        undoPreferences = null;
        AfterPreferencesReplaced();
    }
    private void AfterPreferencesReplaced()
    {
        recording = null;
        ApplyTheme();
        Dispatcher.BeginInvoke(new Action(BuildSettingsPanel));
        ApplyToNotes();
        App.CurrentApp.ApplyShortcuts(); RebuildShortcuts(); Refresh();
    }
    private void ApplyTheme()
    {
        bool dark = App.CurrentApp.Store.Preferences.DarkMode;
        AppTheme.ApplyBrushes(this);
        Background = (Brush)Resources["Page"]; Foreground = (Brush)Resources["Ink"];
        count.Foreground = Muted;
        themeToggle.Content = dark ? "\uE706" : "\uE708";
        themeToggle.ToolTip = dark ? "Light mode" : "Dark mode";
        ApplyNativeTheme(); ApplyPreferencesTheme();
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void ApplyNativeTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = App.CurrentApp.Store.Preferences.DarkMode ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    private static string Status(NoteData note) =>
        (note.Hidden ? "Hidden" : note.Docked ? "At screen edge" : "On desktop")
        + (note.LastOpened is { } opened ? " · Opened " + Ago(opened) : "")
        + (note.Created is { } created ? " · Created " + Day(created) : "")
        + (note.OpenOnStartup ? " · Opens at startup" : "");
    public static string Ago(DateTimeOffset time, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.Now; var elapsed = current - time;
        var day = time.LocalDateTime.Date; var today = current.LocalDateTime.Date;
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min ago";
        if (day == today) return "today " + time.LocalDateTime.ToString("t", CultureInfo.CurrentCulture);
        if (day == today.AddDays(-1)) return "yesterday";
        if ((today - day).Days < 7) return $"{(today - day).Days} days ago";
        return Day(time, current);
    }
    public static string Day(DateTimeOffset time, DateTimeOffset? now = null)
    {
        var local = time.LocalDateTime;
        return local.ToString(local.Year == (now ?? DateTimeOffset.Now).LocalDateTime.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.CurrentCulture);
    }
    public void Refresh()
    {
        rows.Children.Clear(); cards.Children.Clear(); cardMarks.Clear();
        var notes = App.CurrentApp.Store.Notes;
        var matches = notes.Where(n => (n.Title ?? "").Contains(search.Text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.LastOpened ?? n.Created ?? DateTimeOffset.MinValue).ToArray();
        count.Text = notes.Count == 1 ? "1 note" : $"{notes.Count} notes";
        RefreshViewToggle();
        bool grid = App.CurrentApp.Store.Preferences.LibraryGrid && matches.Length > 0;
        notesView.Content = grid ? cards : rows;
        foreach (var note in matches)
            if (grid) cards.Children.Add(NoteCard(note)); else rows.Children.Add(NoteRow(note));
        if (matches.Length == 0)
            rows.Children.Add(new TextBlock { Text = search.Text.Length > 0 ? "No notes found." : "No notes yet. Press + to create one.", Margin = new Thickness(12, 20, 12, 20), Foreground = Muted });
        selectedNotes.IntersectWith(grid ? matches.Select(n => n.Id) : Enumerable.Empty<Guid>());
        RefreshSelectionBar();
    }
    private Grid NoteRow(NoteData note)
    {
        var row = new DockPanel();
        var status = new TextBlock { Text = Status(note), Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var actions = NoteActions(note, () => status.Text = Status(note));
        DockPanel.SetDock(actions, Dock.Right); row.Children.Add(actions);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(note.Title) ? "Untitled note" : note.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(status);
        row.Children.Add(text);
        var highlight = new Border { CornerRadius = new CornerRadius(8), Opacity = 0 };
        highlight.SetResourceReference(Border.BackgroundProperty, "Hover");
        var card = new Grid();
        card.Children.Add(highlight);
        card.Children.Add(new Border { Child = row, Padding = new Thickness(12, 10, 8, 10) });
        WireCard(card, highlight, actions, note);
        return card;
    }
    private StackPanel NoteActions(NoteData note, Action startupChanged)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
        var startup = IconButton("\uE7E8", "", () => { });
        void StartupLook() { startup.ToolTip = note.OpenOnStartup ? "Don't open on app startup" : "Open on app startup"; startup.Opacity = note.OpenOnStartup ? 1 : 0.5; }
        startup.Click += (_, _) => { note.OpenOnStartup = !note.OpenOnStartup; StartupLook(); startupChanged(); App.CurrentApp.Save(); };
        StartupLook(); actions.Children.Add(startup);
        actions.Children.Add(IconButton("\uE71B", "Copy note link", () => Clipboard.SetText(NoteLinks.For(note.Id).AbsoluteUri)));
        var delete = IconButton("\uE74D", "Delete note", () => App.CurrentApp.DeleteNote(note, this));
        delete.MouseEnter += (_, _) => delete.SetResourceReference(Control.ForegroundProperty, "Danger");
        delete.MouseLeave += (_, _) => delete.ClearValue(Control.ForegroundProperty);
        actions.Children.Add(delete);
        return actions;
    }
    private static void WireCard(Grid card, Border highlight, UIElement actions, NoteData note, Func<bool>? click = null)
    {
        card.Background = Brushes.Transparent; card.Cursor = Cursors.Hand; card.ToolTip = "Open note";
        card.MouseEnter += (_, _) => { Animate(highlight, OpacityProperty, 1, 140); Animate(actions, OpacityProperty, 1, 140); actions.IsHitTestVisible = true; };
        card.MouseLeave += (_, _) => { Animate(highlight, OpacityProperty, 0, 220); Animate(actions, OpacityProperty, 0, 220); actions.IsHitTestVisible = false; };
        card.MouseLeftButtonUp += (_, e) => { if (!e.Handled && click?.Invoke() != true) App.CurrentApp.OpenNote(note.Id); };
    }
}
internal sealed class NotePicker : Window
{
    private readonly ListBox list = new() { DisplayMemberPath = "Title", MinHeight = 200 };
    private NotePicker(Guid source)
    {
        AppTheme.Apply(this);
        Title = "Link another note"; Width = 420; Height = 370; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(20) }; Content = panel;
        var search = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 12), ToolTip = "Search notes by title" }; DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        footer.Children.Add(new TextBlock { Text = "Links keep working after a note is renamed.", Foreground = AppTheme.Muted, Margin = new Thickness(0, 10, 0, 5) });
        var insert = new Button { Content = "Insert note link", IsDefault = true, IsEnabled = false }; footer.Children.Add(insert);
        panel.Children.Add(list);
        void Refresh() => list.ItemsSource = App.CurrentApp.Store.Notes.Where(n => n.Id != source && (n.Title ?? "").Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        search.TextChanged += (_, _) => Refresh(); Refresh();
        list.SelectionChanged += (_, _) => insert.IsEnabled = list.SelectedItem != null;
        insert.Click += (_, _) => { if (list.SelectedItem != null) DialogResult = true; };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem != null) DialogResult = true; };
    }
    public static NoteData? Pick(Window owner, Guid source) { var dialog = new NotePicker(source) { Owner = owner }; return dialog.ShowDialog() == true ? dialog.list.SelectedItem as NoteData : null; }
}
