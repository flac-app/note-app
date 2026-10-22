using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Windows.Interop;
using Microsoft.Win32;

namespace StickyNotes;
public sealed partial class NoteWindow : Window
{
    public NoteData Note { get; }
    public bool AllowClose { get; set; }
    private readonly RichTextBox editor = new();
    private readonly TextBox title = new();
    private readonly TextBlock status = new();
    private readonly Grid full = new();
    private readonly Border frame = new();
    private readonly Border headerBand = new();
    private readonly Border sourceChip = new();
    private readonly Border paperShade = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private FrameworkElement headerControls = null!, footerBar = null!;
    private bool chromeShown = true;
    internal bool IsDocked => docked;
    internal EdgeTabWindow? EdgeTab => edgeWindow;
    private readonly TextBlock sourceText = new();
    private readonly ContextMenu noteMenu = new();
    private readonly Dictionary<Key, ToggleButton> formatButtons = new();
    private Button pinButton = null!;
    private MenuItem newNoteItem = null!, addLinkItem = null!;
    private EdgeTabWindow? edgeWindow;
    private bool peeking;
    private int edgeTransition;
    private double previewLeft;
    private readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private bool ready, docked, changing, dirty;
    private bool contentReadable = true;
    private static readonly Regex UrlPattern = new(@"(?:https?://|stickynote://note/|www\.)[^\s<>""\u201c\u201d]+", RegexOptions.IgnoreCase);
    public NoteWindow(NoteData note)
    {
        AppTheme.ApplyIcon(this);
        Note = note;
        Title = note.Title;
        Width = Math.Clamp(note.Width, 120, 1600); Height = Math.Clamp(note.Height, 100, 1400);
        Left = double.IsFinite(note.X) ? note.X : 100; Top = double.IsFinite(note.Y) ? note.Y : 100;
        MinWidth = 120; MinHeight = 100;
        WindowStyle = WindowStyle.None; AllowsTransparency = true;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = !Note.HideFromAltTab;

        FontFamily = new FontFamily("Segoe UI"); Foreground = new SolidColorBrush(Color.FromRgb(49, 48, 40));
        frame.BorderBrush = new SolidColorBrush(Color.FromRgb(205, 188, 139)); frame.BorderThickness = new Thickness(1); frame.CornerRadius = new CornerRadius(9); frame.Padding = new Thickness(0);
        Content = new Viewbox { Child = frame, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        var paperLayers = new Grid(); paperLayers.Children.Add(paperShade); paperLayers.Children.Add(full); frame.Child = paperLayers;
        ConfigureCompactView();
        Content = null; peekRoot.Children.Add(compactLayers); peekRoot.Children.Add(peekTab); Content = peekRoot;
        peekRoot.RenderTransform = peekSlide;
        UpdateScale(Width, Height);
        full.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        full.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        full.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        full.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(6, 2, 5, 2), LastChildFill = true };
        var drag = new TextBlock { Text = "≡", FontSize = 17, Padding = new Thickness(5, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll, ToolTip = "Drag to move this note" };
        drag.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (docked)
            {
                if (!PageOut) return;
                Note.X = Note.Edge == "Left" ? Left : Left + peekExtra; Note.Y = Top; Reveal();
            }
            double startX = Note.X, startY = Note.Y; var startCursor = System.Windows.Forms.Cursor.Position;
            DragMove();
            var cursor = System.Windows.Forms.Cursor.Position;
            if (cursor == startCursor) return;
            var screen = System.Windows.Forms.Screen.FromPoint(cursor);
            bool left = cursor.X <= screen.Bounds.Left, right = cursor.X >= screen.Bounds.Right - 1;
            if (!left && !right) return;
            var dips = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Note.X = startX; Note.Y = startY;
            Note.Edge = left ? "Left" : "Right"; Note.EdgeTabScreen = screen.DeviceName;
            Note.EdgeTabY = dips.Transform(new Point(cursor.X, cursor.Y)).Y - 30;
            Dock();
        };
        DockPanel.SetDock(drag, System.Windows.Controls.Dock.Left); header.Children.Add(drag);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var more = Button("⋯", "Note menu", (_, _) => { });
        var menu = noteMenu; AppTheme.ApplyResources(menu);
        var paperSwatches = new WrapPanel();
        foreach (var hex in new[] { "#000000", "#222222", "#7C2E37", "#264D36", "#264653", "#503450", "#FFF1BA", "#FFFFFF" })
        {
            var swatch = new Button { Style = (Style)menu.FindResource("Swatch"), Width = 22, Height = 22, Margin = new Thickness(3, 3, 5, 3), Background = (Brush)new BrushConverter().ConvertFromString(hex)!, ToolTip = hex };
            swatch.Click += (_, _) => { Note.Color = hex; ApplyAppearance(); QueueSave(); menu.IsOpen = false; }; paperSwatches.Children.Add(swatch);
        }
        menu.Items.Add(new MenuItem { Header = paperSwatches, StaysOpenOnClick = true });
        var opacityPanel = new StackPanel { MinWidth = 200 };
        var opacityLabel = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 5) };
        var quickOpacity = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true, Value = Note.Opacity * 100, ToolTip = "Sets resting and hover opacity together" };
        opacityPanel.Children.Add(opacityLabel); opacityPanel.Children.Add(quickOpacity);
        menu.Items.Add(new MenuItem { Header = opacityPanel, StaysOpenOnClick = true });
        var paperText = new TextBlock();
        var paperItem = new MenuItem { Header = paperText, StaysOpenOnClick = true, ToolTip = "Change only this note's look; the app default is set in the library" };
        void PaperLabel() => paperText.Text = "Paper · " + (NotebookStyle.PaperFor(Note) ? "Notebook" : "Clean") + (Note.Paper == null ? " (default)" : "");
        paperItem.Click += (_, _) => { SetPaper(NotebookStyle.PaperFor(Note) ? "Clean" : "Notebook"); PaperLabel(); };
        menu.Opened += (_, _) => PaperLabel();
        menu.Items.Add(paperItem);
        var tabText = new TextBlock();
        var tabItem = new MenuItem { Header = tabText, StaysOpenOnClick = true, ToolTip = "Change only this note's edge tab; click to cycle through the tab styles" };
        void TabLabel() => tabText.Text = "Edge tab · " + NotebookStyle.TabLookName(NotebookStyle.TabLookFor(Note)) + (Note.TabStyle == null ? " (default)" : "");
        tabItem.Click += (_, _) =>
        {
            var looks = NotebookStyle.TabLooks;
            int current = Array.FindIndex(looks, l => l.Key == NotebookStyle.TabLookFor(Note));
            SetTabStyle(looks[(current + 1) % looks.Length].Key); TabLabel();
        };
        menu.Opened += (_, _) => TabLabel();
        menu.Items.Add(tabItem);
        var autoHideText = new TextBlock();
        var autoHideItem = new MenuItem { Header = autoHideText, StaysOpenOnClick = true, ToolTip = "Whether this note's edge preview or slid-out page hides again by itself; the delay is in Settings" };
        void AutoHideLabel() => autoHideText.Text = "Toggle auto-hide · " + (AutoHideDelay != null ? "On" : "Off");
        autoHideItem.Click += (_, _) => { ToggleAutoHide(); AutoHideLabel(); };
        menu.Opened += (_, _) => AutoHideLabel();
        menu.Items.Add(autoHideItem);
        bool syncingOpacity = false;
        void SyncOpacity()
        {
            syncingOpacity = true;
            quickOpacity.Value = Note.Opacity * 100;
            opacityLabel.Text = Note.HoverOpacityEnabled && Math.Abs(Note.Opacity - Note.HoverOpacity) > .001
                ? $"Opacity · {Note.Opacity * 100:0}% resting / {Note.HoverOpacity * 100:0}% hover"
                : $"Opacity · {Note.Opacity * 100:0}%";
            syncingOpacity = false;
        }
        quickOpacity.ValueChanged += (_, _) =>
        {
            if (syncingOpacity) return;
            Note.Opacity = Note.HoverOpacity = quickOpacity.Value / 100;
            opacityLabel.Text = $"Opacity · {quickOpacity.Value:0}% · both states";
            UpdateOpacity(PointerInside(), false); QueueSave();
        };
        menu.Opened += (_, _) => SyncOpacity(); SyncOpacity();
        MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); return item; }
        Item("Hide in tray", HideNote);
        Item("Delete note", () => App.CurrentApp.Delete(this)).Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 120));
        Item("Copy note link", () => Clipboard.SetText(NoteLinks.For(Note.Id).AbsoluteUri));
        menu.Items.Add(new Separator());
        Item("Notes library", () => App.CurrentApp.ShowLibrary());
        newNoteItem = Item("New note", () => App.CurrentApp.NewNote());
        menu.Items.Add(new Separator());
        addLinkItem = Item("Add a link…", AddLink);
        Item("Link another note…", AddNoteLink);
        Item("Insert image…", PickImage);
        Item("Checklist", ToggleChecklist).ToolTip = "Turn the selected lines into checklist items, or back";
        Item("Set reminder…", () => new ReminderWindow(this) { Owner = this }.ShowDialog());
        menu.Items.Add(new Separator());
        Item("Settings…", OpenSettings);
        more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
        controls.Children.Add(more);
        pinButton = Button("\uE718", "Always on top", (_, _) => { Note.Pinned = !Note.Pinned; ApplyAppearance(); QueueSave(); });
        pinButton.FontFamily = new FontFamily("Segoe MDL2 Assets"); controls.Children.Add(pinButton);
        controls.Children.Add(Button("−", "Collapse to screen edge", (_, _) => Dock()));
        controls.Children.Add(Button("×", "Hide note in tray", (_, _) => HideNote()));
        DockPanel.SetDock(controls, System.Windows.Controls.Dock.Right); header.Children.Add(controls); headerControls = controls;
        title.Text = note.Title; title.Background = Brushes.Transparent; title.BorderThickness = new Thickness(0); title.FontWeight = FontWeights.Normal; title.FontSize = 12; title.Opacity = .8; title.VerticalContentAlignment = VerticalAlignment.Center;
        title.TextChanged += (_, _) => { Note.Title = title.Text; Title = title.Text; QueueSave(); };
        header.Children.Add(title);
        headerBand.Child = header; headerBand.CornerRadius = new CornerRadius(8, 8, 0, 0); full.Children.Add(headerBand);
        sourceChip.Child = sourceText; sourceChip.CornerRadius = new CornerRadius(8); sourceChip.Padding = new Thickness(8, 3, 8, 3); sourceChip.Margin = new Thickness(12, 8, 12, 0); sourceChip.HorizontalAlignment = HorizontalAlignment.Left;
        sourceText.FontSize = 10; sourceText.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(sourceChip, 1); full.Children.Add(sourceChip);
        editor.Background = Brushes.Transparent; editor.BorderThickness = new Thickness(0); editor.Padding = new Thickness(14, 12, 14, 10); editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; editor.AcceptsTab = true; editor.IsDocumentEnabled = true;
        editor.Template = (ControlTemplate)FindResource("NoteEditorTemplate");
        editor.Document.PagePadding = new Thickness(0); editor.Document.Blocks.Clear();
        editor.Language = System.Windows.Markup.XmlLanguage.GetLanguage(InputLanguageManager.Current.CurrentInputLanguage.IetfLanguageTag);
        editor.Document.Language = editor.Language;
        editor.Document.Blocks.Add(new Paragraph());
        var imageLayerHost = new AdornerDecorator { Child = editor };
        var body = new DockPanel();
        ConfigureTasks(body);
        body.Children.Add(imageLayerHost);
        Grid.SetRow(body, 2); full.Children.Add(body);
        var footer = new DockPanel { Margin = new Thickness(8, 3, 12, 7) }; footerBar = footer;
        var formatting = new StackPanel { Orientation = Orientation.Horizontal };
        void Format(Key key, string label, FontWeight? weight = null, FontStyle? style = null, TextDecorationCollection? decorations = null, string? font = null)
        {
            var text = new TextBlock { Text = label, FontWeight = weight ?? FontWeights.Normal, FontStyle = style ?? FontStyles.Normal, TextDecorations = decorations, IsHitTestVisible = false };
            if (font != null) text.FontFamily = new FontFamily(font);
            var button = new ToggleButton { Content = text, Focusable = false, Style = (Style)FindResource("FormatButton") };
            button.Click += (_, _) => ApplyFormat(key); formatButtons[key] = button; formatting.Children.Add(button);
        }
        Format(Key.F, "Aa");
        Format(Key.B, "B", weight: FontWeights.Bold);
        Format(Key.I, "I", style: FontStyles.Italic);
        Format(Key.U, "U", decorations: TextDecorations.Underline);
        Format(Key.S, "S", decorations: TextDecorations.Strikethrough);
        Format(Key.P, "☷");
        Format(Key.T, "☑", font: "Segoe UI Symbol");
        formatButtons[Key.T].ToolTip = "Checklist · click a box to tick it";
        Format(Key.L, "");
        Format(Key.K, "", font: "Segoe MDL2 Assets");
        ConfigureAlignment(); ConfigureFonts();
        editor.SelectionChanged += (_, _) => UpdateFormatStates();
        DockPanel.SetDock(formatting, System.Windows.Controls.Dock.Left); footer.Children.Add(formatting);
        status.Text = "Saved on this device"; status.FontSize = 9; status.Opacity = .45; status.TextTrimming = TextTrimming.CharacterEllipsis; status.HorizontalAlignment = HorizontalAlignment.Right; status.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(status); Grid.SetRow(footer, 3); full.Children.Add(footer);
        LoadContent(); MigrateChecklist(); StyleBoxes(); ApplyAppearance(); ConfigureImageEditing(); ConfigureLinkMenu(); RefreshShortcutHints();
        editor.TextChanged += (_, _) => { if (ready && !changing) { dirty = true; QueueSave(); } };
        editor.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HandleFormatShortcut(key, Keyboard.Modifiers)) { e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control && key is Key.B or Key.I or Key.U) e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape && selectedImage != null) { SelectImage(null); Flush(); e.Handled = true; return; }
            if (PageOut && key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { HideEdgePreview(); e.Handled = true; return; }
            if (CommandShortcut.Match(App.CurrentApp.Store.Preferences, Keyboard.Modifiers, key) == AppCommand.NewNote) { App.CurrentApp.NewNote(); e.Handled = true; }
        };
        editor.KeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter or Key.Tab) AutoLink(); };
        DataObject.AddPastingHandler(editor, OnPaste);
        editor.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler((_, e) => { OpenTarget(e.Uri); e.Handled = true; }));
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Flush(); };
        LocationChanged += (_, _) => SaveBounds(); SizeChanged += (_, e) => { UpdateScale(e.NewSize.Width, e.NewSize.Height); SaveBounds(); };
        Closing += (_, e) => { if (!AllowClose && !App.CurrentApp.Exiting) { e.Cancel = true; HideNote(); } };
        Closed += (_, _) => { saveTimer.Stop(); hoverTimer.Stop(); edgeWindow?.Close(); };
        Activated += (_, _) => Note.LastOpened = DateTimeOffset.Now;
        MouseEnter += (_, _) => UpdateOpacity(true);
        MouseLeave += (_, _) => UpdateOpacity(false);
        hoverTimer.Tick += (_, _) =>
        {
            if (!docked && IsVisible) { bool inside = PointerInside(); UpdateOpacity(inside); UpdateCompactState(inside, Environment.TickCount64); }
            else if (PageOut) { UpdateChrome(PointerInside()); CheckPageAutoClose(Environment.TickCount64); }
        };
        hoverTimer.Start();
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!docked || !peeking) return;
            if (!PageOut) { DetachFromEdge(); return; }
            if (e.OriginalSource is Visual source && peekTab.IsAncestorOf(source)) { HideEdgePreview(); e.Handled = true; }
        };
        SourceInitialized += (_, _) => { EnsureVisible(); ApplyAltTabVisibility(); HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ResizeHitTest); };
        ready = true;
    }
    private IntPtr ResizeHitTest(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0084) return IntPtr.Zero;
        long packed = lParam.ToInt64();
        var point = PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
        if (compactAnimating && !new Rect(new Point(), VisibleNoteSize()).Contains(point)) { handled = true; return new IntPtr(-1); }
        if (compact || compactAnimating) { handled = true; return new IntPtr(1); }
        if (ResizeMode != ResizeMode.CanResize) return IntPtr.Zero;
        bool left = point.X < 6, right = point.X >= ActualWidth - 6;
        bool top = point.Y < 6, bottom = point.Y >= ActualHeight - 6;
        int edge = top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 0;
        handled = edge != 0; return new IntPtr(edge);
    }
    private void ApplyAltTabVisibility()
    {
        if (ShowInTaskbar == Note.HideFromAltTab) ShowInTaskbar = !Note.HideFromAltTab;
        WindowVisibility.SetHiddenFromSwitcher(this, Note.HideFromAltTab);
    }
    private Button Button(string text, string tooltip, RoutedEventHandler click)
    {
        var button = new Button { Foreground = Foreground, Content = text, ToolTip = tooltip, Padding = new Thickness(2), Margin = new Thickness(1), Width = 24, Height = 24, FontSize = 14, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false }; button.Click += click; return button;
    }
    private void LoadContent()
    {
        var path = App.CurrentApp.Store.ContentPath(Note);
        if (!File.Exists(path)) return;
        try { using var stream = File.OpenRead(path); new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Load(stream, DataFormats.XamlPackage); AppFonts.RestoreBundled(editor.Document); WireLinks(); }
        catch (Exception ex) when (ex is IOException or System.Xaml.XamlException or ArgumentException or System.Xml.XmlException)
        { contentReadable = false; editor.IsReadOnly = true; status.Text = "Unable to load · original preserved"; MessageBox.Show("This note could not be loaded. Its original file has been preserved.\n" + ex.Message, "note."); }
    }
    public bool Flush()
    {
        saveTimer.Stop();
        try
        {
            if (dirty && contentReadable)
            {
                NoteStore.AtomicWrite(App.CurrentApp.Store.ContentPath(Note), path => { using var stream = File.Create(path); new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Save(stream, DataFormats.XamlPackage); });
                dirty = false;
            }
            App.CurrentApp.Store.Save(); if (contentReadable) status.Text = "Saved on this device"; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { status.Text = "Save failed · " + ex.Message; status.ToolTip = ex.Message; return false; }
    }
    private void QueueSave() { if (!ready) return; status.Text = "Saving…"; saveTimer.Stop(); saveTimer.Start(); }
    private void UpdateScale(double width, double height)
    {
        if (compactAnimating || compact) return;
        width -= peekExtra;
        double scale = Math.Min(1, Math.Min(width / 290, height / 240));
        if (scale <= 0) return;
        frame.Width = width / scale; frame.Height = height / scale;
    }
    private void SaveBounds()
    {
        if (!ready || docked || changing || compactAnimating || WindowState != WindowState.Normal) return;
        Note.X = Left; Note.Y = Top;
        if (!compact) { Note.Width = Width; Note.Height = Height; }
        QueueSave();
    }
    private Rect WorkingArea()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(screen.Left, screen.Top)), transform.Transform(new Point(screen.Right, screen.Bottom)));
    }
    private void EnsureVisible()
    {
        var area = WorkingArea();
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }
    public void ApplyAppearance()
    {
        try { frame.Background = (Brush)new BrushConverter().ConvertFromString(Note.Color)!; } catch (FormatException) { frame.Background = Brushes.Black; }
        Background = Brushes.Transparent;
        var paper = (frame.Background as SolidColorBrush)?.Color ?? Colors.Black;
        bool darkPaper = paper.R * .299 + paper.G * .587 + paper.B * .114 < 145;
        Resources["ScrollInk"] = new SolidColorBrush(darkPaper ? Color.FromRgb(175, 181, 190) : Color.FromRgb(82, 79, 72));
        Foreground = darkPaper ? Brushes.White : Brushes.Black;
        headerBand.Background = darkPaper ? new SolidColorBrush(Color.FromRgb(24, 24, 24)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        frame.BorderBrush = darkPaper ? new SolidColorBrush(Color.FromRgb(55, 55, 55)) : new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        sourceChip.Visibility = string.IsNullOrWhiteSpace(Note.Apps) ? Visibility.Collapsed : Visibility.Visible;
        sourceChip.Background = darkPaper ? new SolidColorBrush(Color.FromRgb(32, 32, 32)) : new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
        sourceText.Text = "Opens with: " + Note.Apps;
        foreach (var child in full.Children.OfType<FrameworkElement>()) UpdateControlInk(child, Foreground);
        title.Foreground = Foreground; title.CaretBrush = Foreground;
        bool notebook = NotebookStyle.PaperFor(Note);
        if (notebook)
        {
            frame.Background = NotebookStyle.Paper(paper);
            paperShade.Background = NotebookStyle.Vignette; paperShade.Visibility = Visibility.Visible;
            headerBand.Background = new SolidColorBrush(Color.FromArgb(darkPaper ? (byte)150 : (byte)200, 22, 17, 12));
            headerBand.BorderBrush = new SolidColorBrush(Color.FromArgb(150, 232, 224, 207)); headerBand.BorderThickness = new Thickness(0, 0, 0, 2);
            frame.BorderBrush = new SolidColorBrush(Color.FromArgb(130, 12, 8, 4));
            var headerInk = new SolidColorBrush(Color.FromRgb(242, 236, 224));
            TextElement.SetForeground(headerBand, headerInk); UpdateControlInk(headerBand, headerInk);
            title.Foreground = headerInk; title.CaretBrush = headerInk; title.FontSize = 14; title.FontWeight = FontWeights.SemiBold; title.Opacity = 1;
            sourceChip.Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        }
        else
        {
            paperShade.Visibility = Visibility.Collapsed; headerBand.BorderThickness = new Thickness(0); headerBand.ClearValue(TextElement.ForegroundProperty);
            title.FontSize = 12; title.FontWeight = FontWeights.Normal; title.Opacity = .8;
        }
        double corner = NotebookStyle.Corner(notebook), inner = Math.Max(0, corner - 1);
        frame.CornerRadius = new CornerRadius(corner); paperShade.CornerRadius = new CornerRadius(inner);
        headerBand.CornerRadius = new CornerRadius(inner, inner, 0, 0);
        pinButton.Foreground = Note.Pinned ? Brushes.DeepSkyBlue : notebook ? title.Foreground : Foreground;
        editor.Foreground = Foreground; editor.CaretBrush = Foreground; editor.Document.Foreground = Foreground;
        new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).ApplyPropertyValue(TextElement.ForegroundProperty, Foreground);
        void InkLists(BlockCollection blocks)
        {
            foreach (var block in blocks)
                if (block is System.Windows.Documents.List list) { list.Foreground = Foreground; foreach (var item in list.ListItems) { item.Foreground = Foreground; InkLists(item.Blocks); } }
                else if (block is Section section) InkLists(section.Blocks);
        }
        InkLists(editor.Document.Blocks);
        WireLinks();
        edgeWindow?.RefreshAppearance(); RefreshCarriedTab();
        Topmost = docked || Note.Pinned || Note.ReminderActive; UpdateOpacity(PointerInside(), false);
        editor.FontFamily = AppFonts.Get(Note.Font); editor.FontSize = Math.Clamp(Note.FontSize, 10, 48);
        editor.Document.FontFamily = editor.FontFamily; editor.Document.FontSize = editor.FontSize;
        UpdateFormatStates();
        RefreshCompactAppearance();
        UpdateChrome(PointerInside(), false);
        ApplyAltTabVisibility();
        RefreshTaskAppearance();
    }
    private static readonly Dictionary<AppCommand, Key> FormatCommands = new()
    {
        [AppCommand.Bold] = Key.B, [AppCommand.Italic] = Key.I, [AppCommand.Underline] = Key.U, [AppCommand.Strikethrough] = Key.S, [AppCommand.Bullets] = Key.P, [AppCommand.AddLink] = Key.K
    };
    private bool HandleFormatShortcut(Key key, ModifierKeys modifiers)
    {
        if (CommandShortcut.Match(App.CurrentApp.Store.Preferences, modifiers, key) is not { } command || !FormatCommands.TryGetValue(command, out var format)) return false;
        ApplyFormat(format); return true;
    }
    internal void RefreshShortcutHints()
    {
        var preferences = App.CurrentApp.Store.Preferences;
        string Hint(AppCommand command) { var keys = CommandShortcut.KeysFor(preferences, command); return keys == "Not set" ? "" : "    " + keys; }
        foreach (var (command, format) in FormatCommands)
            if (formatButtons.TryGetValue(format, out var button)) button.ToolTip = CommandShortcut.Label(command) + (Hint(command) is { Length: > 0 } keys ? " ·" + keys.TrimStart() : "");
        newNoteItem.Header = "New note" + Hint(AppCommand.NewNote);
        addLinkItem.Header = "Add a link…" + Hint(AppCommand.AddLink);
    }
    private void ApplyFormat(Key key)
    {
        if (editor.IsReadOnly) return;
        editor.Focus();
        if (editor.Selection.IsEmpty && !editor.CaretPosition.IsAtInsertionPosition)
            editor.CaretPosition = editor.CaretPosition.GetInsertionPosition(LogicalDirection.Backward);
        switch (key)
        {
            case Key.B: EditingCommands.ToggleBold.Execute(null, editor); break;
            case Key.I: EditingCommands.ToggleItalic.Execute(null, editor); break;
            case Key.U: ToggleDecoration(TextDecorationLocation.Underline); break;
            case Key.S: ToggleDecoration(TextDecorationLocation.Strikethrough); break;
            case Key.P: EditingCommands.ToggleBullets.Execute(null, editor); break;
            case Key.K: AddLink(); break;
            case Key.L: ShowAlignmentMenu(); break;
            case Key.T: ToggleChecklist(); break;
            case Key.F: ShowFontPicker(); break;
        }
        UpdateFormatStates();
    }
    private IEnumerable<TextRange> SelectedTextRanges()
    {
        if (editor.Selection.IsEmpty) { yield return editor.Selection; yield break; }
        for (var p = editor.Selection.Start; p != null && p.CompareTo(editor.Selection.End) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            var end = p.GetNextContextPosition(LogicalDirection.Forward);
            if (end == null) break;
            yield return new TextRange(p, end.CompareTo(editor.Selection.End) > 0 ? editor.Selection.End : end);
        }
    }
    private bool HasDecoration(TextDecorationLocation location)
    {
        if (editor.Selection.IsEmpty) return editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection caretDecorations && caretDecorations.Any(t => t.Location == location);
        var ranges = SelectedTextRanges().ToArray();
        return ranges.Length > 0 && ranges.All(r => r.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection d && d.Any(t => t.Location == location));
    }
    private void ToggleDecoration(TextDecorationLocation location)
    {
        bool remove = HasDecoration(location);
        if (editor.Selection.IsEmpty)
        {
            var pending = (editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection)?.Clone() ?? new TextDecorationCollection();
            foreach (var decoration in pending.Where(d => d.Location == location).ToArray()) pending.Remove(decoration);
            if (!remove) pending.Add(new TextDecoration { Location = location });
            editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, pending);
            return;
        }
        var ranges = SelectedTextRanges().ToArray();
        editor.BeginChange();
        try
        {
            foreach (var range in ranges)
            {
                var decorations = (range.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection)?.Clone() ?? new TextDecorationCollection();
                foreach (var decoration in decorations.Where(d => d.Location == location).ToArray()) decorations.Remove(decoration);
                if (!remove) decorations.Add(new TextDecoration { Location = location });
                range.ApplyPropertyValue(Inline.TextDecorationsProperty, decorations);
            }
        }
        finally { editor.EndChange(); }
    }
    private void UpdateFormatStates()
    {
        var paragraphs = new HashSet<Paragraph>();
        if (editor.Selection.Start.Paragraph is Paragraph first) paragraphs.Add(first);
        if (editor.Selection.End.Paragraph is Paragraph last) paragraphs.Add(last);
        for (var p = editor.Selection.Start; p != null && p.CompareTo(editor.Selection.End) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is Paragraph paragraph) paragraphs.Add(paragraph);
        bool Bulleted(Paragraph? paragraph)
        {
            if (paragraph == null) return false;
            for (DependencyObject? p = paragraph.Parent; p is FrameworkContentElement element; p = element.Parent)
                if (p is System.Windows.Documents.List list) return list.MarkerStyle is TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square or TextMarkerStyle.Box;
            return false;
        }
        foreach (var pair in formatButtons)
        {
            pair.Value.IsEnabled = !editor.IsReadOnly;
            if (pair.Key == Key.L) { UpdateAlignmentButton(pair.Value); continue; }
            pair.Value.IsChecked = pair.Key switch
            {
                Key.B => Equals(editor.Selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold),
                Key.I => Equals(editor.Selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic),
                Key.U => HasDecoration(TextDecorationLocation.Underline),
                Key.S => HasDecoration(TextDecorationLocation.Strikethrough),
                Key.P => paragraphs.Count > 0 && paragraphs.All(Bulleted),
                Key.K => LinkAt(editor.Selection.Start) != null,
                Key.T => paragraphs.Count > 0 && paragraphs.All(IsItem),
                _ => false
            };
        }
    }
    private static void UpdateControlInk(DependencyObject element, Brush ink)
    {
        if (element is ButtonBase button) button.Foreground = ink;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) UpdateControlInk(VisualTreeHelper.GetChild(element, i), ink);
    }
    private void UpdateChrome(bool hovered, bool animate = true)
    {
        bool show = (!peeking || PageOut) && (!NotebookStyle.PaperFor(Note) || hovered || noteMenu.IsOpen || editor.IsKeyboardFocusWithin && IsActive);
        if (show == chromeShown && animate) return;
        chromeShown = show;
        foreach (var part in new[] { headerControls, footerBar })
        {
            part.IsHitTestVisible = show;
            double from = part.Opacity;
            part.BeginAnimation(OpacityProperty, null); part.Opacity = show ? 1 : 0;
            if (animate && App.CurrentApp.Store.Preferences.SmoothHover)
                part.BeginAnimation(OpacityProperty, new DoubleAnimation(from, part.Opacity, TimeSpan.FromMilliseconds(show ? 140 : 260)) { FillBehavior = FillBehavior.Stop });
        }
    }
    public void SetDocumentFont()
    {
        var range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd);
        range.ApplyPropertyValue(TextElement.FontFamilyProperty, AppFonts.Get(Note.Font)); range.ApplyPropertyValue(TextElement.FontSizeProperty, Note.FontSize);
        StyleBoxes();
    }
    private bool PointerInside()
    {
        if (!IsVisible) return false;
        var cursor = System.Windows.Forms.Cursor.Position;
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        var size = VisibleNoteSize();
        return point.X >= 0 && point.Y >= 0 && point.X <= size.Width && point.Y <= size.Height;
    }
    private double opacityTarget = -1;
    internal void RefreshInteractionPreferences()
    {
        edgeTransition++;
        BeginAnimation(LeftProperty, null);
        if (docked)
        {
            if (peeking) Left = previewLeft;
            else Hide();
        }
        UpdateOpacity(PointerInside(), false);
        edgeWindow?.RefreshAppearance(); RefreshCarriedTab();
        RefreshCompactMode();
    }
    private void UpdateOpacity(bool hovered, bool animate = true)
    {
        UpdateChrome(hovered);
        animate &= App.CurrentApp.Store.Preferences.SmoothHover;
        double target = Math.Clamp((hovered || peeking) && Note.HoverOpacityEnabled ? Note.HoverOpacity : Note.Opacity, 0, 1);
        target = Math.Max(.001, target);
        if (Math.Abs(opacityTarget - target) < .0001 && animate) return;
        opacityTarget = target;
        double current = Opacity;
        BeginAnimation(OpacityProperty, null); Opacity = target;
        if (animate) BeginAnimation(OpacityProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
    }
    public void Dock()
    {
        if (docked) { if (peeking) HideEdgePreview(); return; }
        Flush(); changing = true; docked = true; peeking = false; Note.Docked = true; Note.Hidden = false;
        ResetCompactView();
        Hide();
        edgeWindow ??= new EdgeTabWindow(this);
        edgeWindow.Place();
        edgeWindow.RefreshAppearance(); edgeWindow.Show(); changing = false; App.CurrentApp.ArrangeTabs(edgeWindow); App.CurrentApp.Save();
    }
    internal void ShowEdgePreview()
    {
        if (!docked || Note.Hidden || peeking) return;
        bool pull = NotebookStyle.Pull && edgeWindow != null;
        if (pull) App.CurrentApp.HidePreviewsExcept(this);
        else App.CurrentApp.HidePreviewsExcept(this, other => other.AutoHideDelay == null);
        bool wasSliding = pull && IsVisible;
        edgeTransition++; peeking = true; changing = true;
        UpdateChrome(false, false);
        var area = edgeWindow?.WorkArea() ?? WorkingArea();
        if (pull)
        {
            ShowPulledPage(area, wasSliding);
            changing = false; return;
        }
        double tabWidth = edgeWindow?.Width ?? 44;
        Width = Math.Min(Math.Clamp(Note.Width, 120, 1600), Math.Max(120, area.Width - tabWidth));
        Height = Math.Min(Math.Clamp(Note.Height, 100, 1400), Math.Max(100, area.Height));
        previewLeft = Note.Edge == "Left" ? area.Left + tabWidth : area.Right - tabWidth - Width;
        BeginAnimation(LeftProperty, null); Left = previewLeft;
        Top = Math.Clamp(edgeWindow?.Top ?? Note.Y, area.Top, Math.Max(area.Top, area.Bottom - Height));
        ResizeMode = ResizeMode.NoResize; ShowActivated = false; Topmost = true; Show(); UpdateOpacity(true, false);
        if (App.CurrentApp.Store.Preferences.SmoothHover)
            BeginAnimation(LeftProperty, new DoubleAnimation(previewLeft + (Note.Edge == "Left" ? -36 : 36), previewLeft, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        changing = false;
    }
    private void ShowPulledPage(Rect area, bool wasSliding)
    {
        var tab = edgeWindow!;
        LayoutPulledPage(area);
        int version = edgeTransition;
        ResizeMode = ResizeMode.NoResize; ShowActivated = false; Topmost = true;
        if (wasSliding)
        {
            tab.SetCovered(true); Left = previewLeft; UpdateOpacity(true, false); FocusPage();
            SlidePage(true, version); return;
        }
        ShowPulledPageFresh(tab, version);
    }
    private void LayoutPulledPage(Rect area)
    {
        var tab = edgeWindow!;
        bool right = Note.Edge != "Left";
        double tabWidth = tab.Width, tabTop = tab.Top;
        pageWidth = Math.Min(Math.Clamp(Note.Width, 120, 1600), Math.Max(120, area.Width - tabWidth));
        double pageHeight = Math.Min(Math.Clamp(Note.Height, 100, 1400), Math.Max(100, area.Height));
        peekExtra = tabWidth;
        Width = pageWidth + tabWidth;
        previewLeft = right ? area.Right - Width : area.Left;
        BeginAnimation(LeftProperty, null);
        Top = Math.Clamp(tabTop - 40, area.Top, Math.Max(area.Top, area.Bottom - pageHeight));
        double tabOffset = tabTop - Top;
        Height = Math.Max(pageHeight, tabOffset + tab.Height);
        compactLayers.Margin = right ? new Thickness(tabWidth, 0, 0, 0) : new Thickness(0, 0, tabWidth, 0);
        compactLayers.Height = pageHeight; compactLayers.VerticalAlignment = VerticalAlignment.Top;
        peekTab.Content = tab.CreateTabVisual();
        peekTab.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        peekTab.Margin = new Thickness(0, tabOffset, 0, 0); peekTab.Visibility = Visibility.Visible;
        UpdateScale(Width, pageHeight);
    }
    private void ShowPulledPageFresh(EdgeTabWindow tab, int version)
    {
        peekSlide.BeginAnimation(TranslateTransform.XProperty, null); peekSlide.X = PageHiddenOffset;
        Left = -32000; Show(); UpdateOpacity(true, false);
        App.CurrentApp.RaiseTabs();
        AfterFrames(2, () =>
        {
            if (version != edgeTransition || !peeking) return;
            Left = previewLeft;
            AfterFrames(1, () => { if (version != edgeTransition || !peeking) return; tab.SetCovered(true); SlidePage(true, version); FocusPage(); });
        });
    }
    internal bool PageOut => docked && peeking && peekExtra > 0;
    private long pageActivity;
    internal void ToggleEdgePage() { if (peeking) HideEdgePreview(); else ShowEdgePreview(); }
    private void FocusPage()
    {
        pageActivity = Environment.TickCount64;
        Activate(); editor.Focus();
    }
    internal void CheckPageAutoClose(long now)
    {
        if (!PageOut) return;
        bool busy = PointerInside() || IsActive && IsKeyboardFocusWithin || noteMenu.IsOpen || OwnedWindows.Cast<Window>().Any(w => w.IsVisible);
        if (busy) { pageActivity = now; return; }
        if (AutoHideDelay is int delay && now - pageActivity > delay) HideEdgePreview();
    }
    internal int? AutoHideDelay
    {
        get
        {
            if (!Note.AutoHide) return null;
            if (Note.AutoHideMilliseconds is int own) return Math.Max(0, own);
            var preferences = App.CurrentApp.Store.Preferences;
            if (NotebookStyle.Pull) return preferences.PullAutoCloseSeconds > 0 ? preferences.PullAutoCloseSeconds * 1000 : null;
            return Math.Clamp(preferences.HoverCloseDelayMilliseconds, 0, 1500);
        }
    }
    internal const int AutoHideFallbackMilliseconds = 30000;
    internal void ToggleAutoHide() => SetAutoHide(AutoHideDelay == null);
    internal void SetAutoHide(bool on)
    {
        Note.AutoHide = on;
        if (on && AutoHideDelay == null) Note.AutoHideMilliseconds = AutoHideFallbackMilliseconds;
        pageActivity = Environment.TickCount64; QueueSave();
    }
    private readonly TranslateTransform peekSlide = new();
    private readonly Grid peekRoot = new();
    private readonly ContentControl peekTab = new() { VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = true };
    private double peekExtra, pageWidth;
    private double PageHiddenOffset => Note.Edge == "Left" ? -pageWidth : pageWidth;
    private void SlidePage(bool open, int version, Action? done = null)
    {
        double to = open ? 0 : PageHiddenOffset;
        void Finish() { peekSlide.BeginAnimation(TranslateTransform.XProperty, null); peekSlide.X = to; done?.Invoke(); }
        if (!App.CurrentApp.Store.Preferences.SmoothHover) { Finish(); return; }
        double from = peekSlide.X;
        var duration = TimeSpan.FromMilliseconds(Math.Max(120, (open ? 300 : 240) * Math.Abs(to - from) / Math.Max(1, pageWidth)));
        var animation = new DoubleAnimation(from, to, duration) { EasingFunction = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn } };
        animation.Completed += (_, _) => { if (version == edgeTransition) Finish(); };
        peekSlide.BeginAnimation(TranslateTransform.XProperty, animation);
    }
    private static void AfterFrames(int count, Action action)
    {
        EventHandler? handler = null;
        handler = (_, _) => { if (--count > 0) return; CompositionTarget.Rendering -= handler; action(); };
        CompositionTarget.Rendering += handler;
    }
    private void ResetPeek()
    {
        peekSlide.BeginAnimation(TranslateTransform.XProperty, null); peekSlide.X = 0;
        peekExtra = 0; peekTab.Content = null; peekTab.Visibility = Visibility.Collapsed;
        compactLayers.Margin = new Thickness(0); compactLayers.ClearValue(HeightProperty); compactLayers.VerticalAlignment = VerticalAlignment.Stretch;
        edgeWindow?.SetCovered(false);
    }
    internal void HideEdgePreview()
    {
        if (!docked || !peeking) return;
        peeking = false; int version = ++edgeTransition;
        if (peekExtra > 0)
        {
            SlidePage(false, version, () =>
            {
                if (!docked || peeking || version != edgeTransition) return;
                edgeWindow?.SetCovered(false);
                AfterFrames(2, () => { if (!docked || peeking || version != edgeTransition) return; Hide(); ResetPeek(); Flush(); });
            });
            return;
        }
        if (!App.CurrentApp.Store.Preferences.SmoothHover)
        {
            BeginAnimation(LeftProperty, null); BeginAnimation(OpacityProperty, null); Hide(); return;
        }
        var animation = new DoubleAnimation(Left, previewLeft + (Note.Edge == "Left" ? -36 : 36), TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }, FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) => { if (docked && !peeking && version == edgeTransition) Hide(); };
        BeginAnimation(LeftProperty, animation);
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, .001, TimeSpan.FromMilliseconds(180)));
    }
    internal bool PointerOverPreview(Point screenPoint)
    {
        if (!IsVisible || !docked) return false;
        if (NotebookStyle.Pull && !peeking) return false;
        var point = PointFromScreen(screenPoint);
        return new Rect(-12, -12, ActualWidth + 24, ActualHeight + 24).Contains(point);
    }
    internal void DetachFromEdge() => Reveal();
    public void Reveal()
    {
        changing = true; edgeTransition++; peeking = false; docked = false; Note.Docked = false; Note.Hidden = false; Note.LastOpened = DateTimeOffset.Now;
        ResetCompactView();
        ResetPeek(); edgeWindow?.Hide(); BeginAnimation(LeftProperty, null); App.CurrentApp.ArrangeTabs();
        Width = Math.Clamp(Note.Width, 120, 1600); Height = Math.Clamp(Note.Height, 100, 1400); Left = Note.X; Top = Note.Y; ResizeMode = ResizeMode.CanResize;
        ShowActivated = true; Show(); EnsureVisible(); ApplyAppearance(); changing = false; Activate(); editor.Focus(); App.CurrentApp.Save();
        HoldCompactOpen(Environment.TickCount64);
    }
    public void HideNote() { Flush(); edgeTransition++; peeking = false; Note.Hidden = true; BeginAnimation(LeftProperty, null); ResetPeek(); edgeWindow?.Hide(); Hide(); ResetCompactView(); App.CurrentApp.ArrangeTabs(); App.CurrentApp.Save(); }
    internal void SetPaper(string? paper)
    {
        Note.Paper = paper;
        ApplyAppearance(); App.CurrentApp.ArrangeTabs(); QueueSave();
    }
    internal void SetTabStyle(string? look)
    {
        Note.TabStyle = look;
        ApplyAppearance(); App.CurrentApp.ArrangeTabs(edgeWindow); QueueSave();
    }
    internal void ShowNoteMenu(UIElement target)
    {
        noteMenu.PlacementTarget = target; noteMenu.Placement = PlacementMode.MousePoint; noteMenu.IsOpen = true;
    }
    private void RefreshCarriedTab()
    {
        if (!PageOut || edgeWindow == null) return;
        LayoutPulledPage(edgeWindow.WorkArea()); Left = previewLeft;
    }
    private void OpenSettings() { var dialog = new SettingsWindow(this) { Owner = this }; dialog.ShowDialog(); }
    private void PickImage()
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*" };
        if (dialog.ShowDialog(this) == true) try { InsertImage(ReadImage(dialog.FileName)); } catch (Exception ex) { MessageBox.Show(this, "Unable to open image: " + ex.Message); }
    }
    internal static BitmapImage ReadImage(string path)
    {
        using var stream = File.OpenRead(path); return DecodeImage(stream);
    }
    internal static BitmapImage DecodeImage(Stream stream)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1200; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    private void InsertImage(BitmapSource source)
    {
        editor.Selection.Text = "";
        var image = new Image { Source = source, Width = Math.Min(ImageWidthLimit(), Math.Min(290, source.Width)), Stretch = Stretch.Uniform, ToolTip = "Click to resize · right-click for image sizing" };
        var inline = new InlineUIContainer(image, editor.CaretPosition); editor.CaretPosition = inline.ElementEnd; editor.Focus(); dirty = true; QueueSave();
    }
    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (e.DataObject.GetDataPresent(DataFormats.FileDrop)) { e.CancelCommand(); ImportDroppedImages(e.DataObject, editor.CaretPosition); return; }
            if (!IsNoteContent(e.DataObject) && e.DataObject.GetDataPresent(DataFormats.Bitmap) && e.DataObject.GetData(DataFormats.Bitmap) is BitmapSource source) { e.CancelCommand(); InsertImage(source); return; }
        }
        catch (Exception ex) { status.Text = "Paste failed: " + ex.Message; }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { AppFonts.RestoreBundled(editor.Document); AutoLink(); WireLinks(); }));
    }
    private void ConfigureLinkMenu()
    {
        editor.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (!contentReadable || editor.GetPositionFromPoint(e.GetPosition(editor), false) is not { } at || LinkAt(at) is not { } link) return;
            var items = new List<(string, Action)> { ("Edit link…", () => EditLink(link)) };
            if (link.NavigateUri is { } uri)
            {
                items.Add(("Open link", () => OpenTarget(uri)));
                items.Add(("Copy link address", () => Clipboard.SetText(uri.OriginalString)));
            }
            ShowEditorMenu(editor, items.ToArray()); e.Handled = true;
        };
    }
    private void ShowEditorMenu(UIElement target, params (string Label, Action Action)[] items)
    {
        var menu = new ContextMenu { PlacementTarget = target }; AppTheme.ApplyResources(menu);
        foreach (var (label, action) in items) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        menu.IsOpen = true;
    }
    private static Hyperlink? LinkAt(TextPointer position)
    {
        for (var element = position.Parent; element is FrameworkContentElement content; element = content.Parent)
            if (element is Hyperlink link) return link;
        return null;
    }
    private void EditLink(Hyperlink link)
    {
        if (editor.IsReadOnly) return;
        bool renamable = LinkRuns(link) != null;
        if (LinkDialog.Edit(this, new TextRange(link.ContentStart, link.ContentEnd).Text, link.NavigateUri?.OriginalString ?? "", renamable) is not { } edit) return;
        if (!Uri.TryCreate(edit.Address, UriKind.Absolute, out var uri)) { MessageBox.Show(this, "Enter a full URL, file path, or application link."); return; }
        UpdateLink(link, renamable ? edit.Text : "", uri);
    }
    private static List<Run>? LinkRuns(Hyperlink link)
    {
        var runs = new List<Run>();
        for (var p = link.ContentStart; p != null && p.CompareTo(link.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            var context = p.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.EmbeddedElement) return null;
            if (context == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is Run run) runs.Add(run);
        }
        return runs.Count > 0 ? runs : null;
    }
    internal void UpdateLink(Hyperlink link, string text, Uri address)
    {
        editor.BeginChange();
        try
        {
            link.NavigateUri = address;
            if (text.Length > 0 && LinkRuns(link) is { } runs && text != new TextRange(link.ContentStart, link.ContentEnd).Text)
            {
                runs[0].Text = text;
                foreach (var extra in runs.Skip(1)) extra.SiblingInlines.Remove(extra);
            }
            ConfigureLink(link);
        }
        finally { editor.EndChange(); }
        editor.CaretPosition = link.ElementEnd; dirty = true; QueueSave(); UpdateFormatStates();
    }
    private void AddLink()
    {
        if (LinkAt(editor.Selection.Start) is { } existing) { EditLink(existing); return; }
        var address = LinkDialog.Ask(this);
        if (address == null) return;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) { MessageBox.Show(this, "Enter a full URL, file path, or application link."); return; }
        try
        {
            Hyperlink link;
            if (editor.Selection.IsEmpty) link = new Hyperlink(new Run(address), editor.CaretPosition);
            else link = new Hyperlink(editor.Selection.Start, editor.Selection.End);
            link.NavigateUri = uri; ConfigureLink(link); editor.CaretPosition = link.ElementEnd; dirty = true; QueueSave();
        }
        catch (InvalidOperationException) { MessageBox.Show(this, "Select text or an image within one paragraph to add a link."); }
    }
    private void AddNoteLink()
    {
        var target = NotePicker.Pick(this, Note.Id);
        if (target != null) InsertNoteLink(target);
    }
    internal void InsertNoteLink(NoteData target)
    {
        try
        {
            Hyperlink link = editor.Selection.IsEmpty
                ? new Hyperlink(new Run(target.Title), editor.CaretPosition)
                : new Hyperlink(editor.Selection.Start, editor.Selection.End);
            link.NavigateUri = NoteLinks.For(target.Id); ConfigureLink(link);
            editor.CaretPosition = link.ElementEnd; dirty = true; QueueSave();
        }
        catch (InvalidOperationException) { MessageBox.Show(this, "Select content within one paragraph to link a note."); }
    }
    private IEnumerable<Run> Runs()
    {
        for (var p = editor.Document.ContentStart; p != null && p.CompareTo(editor.Document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is Run run) yield return run;
    }
    private void AutoLink()
    {
        if (changing || !contentReadable) return;
        changing = true;
        try
        {
            foreach (var run in Runs().Where(r => !InsideLink(r)).ToArray())
            {
                foreach (Match match in UrlPattern.Matches(run.Text).Cast<Match>().Reverse())
                {
                    string text = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']');
                    if (!Uri.TryCreate(text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + text : text, UriKind.Absolute, out var uri)) continue;
                    var start = run.ContentStart.GetPositionAtOffset(match.Index); var end = start?.GetPositionAtOffset(text.Length);
                    if (start == null || end == null) continue;
                    var link = new Hyperlink(start, end) { NavigateUri = uri }; ConfigureLink(link); dirty = true;
                }
            }
        }
        finally { changing = false; }
        QueueSave();
    }
    private static bool InsideLink(FrameworkContentElement element)
    {
        for (var parent = element.Parent; parent is FrameworkContentElement p; parent = p.Parent) if (p is Hyperlink) return true;
        return false;
    }
    private void WireLinks()
    {
        for (var p = editor.Document.ContentStart; p != null && p.CompareTo(editor.Document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is Hyperlink link) ConfigureLink(link);
    }
    private void ConfigureLink(Hyperlink link)
    {
        link.Foreground = Foreground == Brushes.White ? Brushes.LightSkyBlue : new SolidColorBrush(Color.FromRgb(49, 99, 111));
        if (link.NavigateUri != null) link.ToolTip = NoteLinks.TryGetId(link.NavigateUri, out var id)
            ? "Open note: " + (App.CurrentApp.Store.Notes.FirstOrDefault(n => n.Id == id)?.Title ?? "Deleted note")
            : ImagePreview.Create(link.NavigateUri);
    }
    private void OpenTarget(Uri uri)
    {
        if (NoteLinks.TryGetId(uri, out var id))
        {
            if (!App.CurrentApp.OpenNote(id)) ConfirmDialog.Inform(this, "Note unavailable", "This note was deleted or is no longer available.");
            return;
        }
        if (uri.Scheme != "https" && uri.Scheme != "http" && uri.Scheme != "mailto")
            if (!ConfirmDialog.Ask(this, "Open link?", uri.OriginalString, "Open")) return;
        try { Process.Start(new ProcessStartInfo(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, "Windows could not open this link.\n" + ex.Message); }
    }
}


