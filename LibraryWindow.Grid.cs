using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
namespace StickyNotes;
public sealed partial class LibraryWindow
{
    private const double CardMinWidth = 160, CardHeight = 164, CardGap = 10;
    private readonly UniformGrid cards = new() { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, -CardGap, 0) };
    private readonly Button viewToggle = new();
    private static readonly Dictionary<Guid, (DateTime Written, Summary Summary)> previews = new();
    public readonly record struct Summary(string Text, int Done, int Items)
    {
        public IReadOnlyList<IReadOnlyList<(string Text, string? Font)>> Lines { get; init; } = Array.Empty<IReadOnlyList<(string, string?)>>();
    }
    private readonly HashSet<Guid> selectedNotes = new();
    private static readonly Brush SelectionBlue = new SolidColorBrush(Color.FromRgb(0x3D, 0x8B, 0xFF));
    private readonly Dictionary<Guid, Action<bool>> cardMarks = new();
    private readonly Border selectionBar = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 4, 4, 4), Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    private readonly TextBlock selectionCount = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private void BuildSelectionBar()
    {
        selectionBar.SetResourceReference(Border.BackgroundProperty, "Surface"); selectionBar.SetResourceReference(Border.BorderBrushProperty, "Line");
        var bar = new DockPanel(); selectionBar.Child = bar;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); bar.Children.Add(buttons);
        Button Action(string label, string tooltip, Action run) { var button = new Button { Content = label, ToolTip = tooltip, MinWidth = 0 }; button.Click += (_, _) => run(); buttons.Children.Add(button); return button; }
        Action("Select all", "Select every note shown", () => { foreach (var id in cardMarks.Keys) selectedNotes.Add(id); RefreshSelection(); });
        Action("Open", "Open the selected notes", () => { var ids = selectedNotes.ToArray(); ClearSelection(); foreach (var id in ids) App.CurrentApp.OpenNote(id); });
        var delete = Action("Delete", "Delete the selected notes", () => { App.CurrentApp.DeleteNotes(App.CurrentApp.Store.Notes.Where(n => selectedNotes.Contains(n.Id)).ToList(), this); Refresh(); });
        delete.SetResourceReference(Control.ForegroundProperty, "Danger");
        Action("Clear", "Clear the selection · Esc", ClearSelection);
        bar.Children.Add(selectionCount);
    }
    private void ToggleSelected(Guid id) { if (!selectedNotes.Remove(id)) selectedNotes.Add(id); RefreshSelection(); }
    private void ClearSelection() { selectedNotes.Clear(); RefreshSelection(); }
    private void RefreshSelection()
    {
        foreach (var (id, mark) in cardMarks) mark(selectedNotes.Contains(id));
        RefreshSelectionBar();
    }
    private void RefreshSelectionBar()
    {
        selectionBar.Visibility = selectedNotes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        selectionCount.Text = selectedNotes.Count == 1 ? "1 note selected" : $"{selectedNotes.Count} notes selected";
    }
    private void BuildViewToggle()
    {
        viewToggle.Style = (Style)FindResource("IconButton");
        viewToggle.Click += (_, _) => { App.CurrentApp.Store.Preferences.LibraryGrid = !App.CurrentApp.Store.Preferences.LibraryGrid; Refresh(); App.CurrentApp.Save(); };
        cards.SizeChanged += (_, e) => cards.Columns = Math.Max(1, (int)(e.NewSize.Width / (CardMinWidth + CardGap)));
    }
    private void RefreshViewToggle()
    {
        bool grid = App.CurrentApp.Store.Preferences.LibraryGrid;
        viewToggle.Content = grid ? "\uE8FD" : "\uF0E2";
        viewToggle.ToolTip = grid ? "List view" : "Grid view";
    }
    private Grid NoteCard(NoteData note)
    {
        Color paper;
        try { paper = (Color)ColorConverter.ConvertFromString(note.Color); } catch (FormatException) { paper = Colors.Black; }
        bool darkPaper = paper.R * .299 + paper.G * .587 + paper.B * .114 < 145, notebook = NotebookStyle.PaperFor(note);
        var ink = darkPaper ? Brushes.White : Brushes.Black;
        double corner = NotebookStyle.Corner(notebook), inner = Math.Max(0, corner - 1);
        var frame = new Border { CornerRadius = new CornerRadius(corner), BorderThickness = new Thickness(1), Background = notebook ? NotebookStyle.Paper(paper) : new SolidColorBrush(paper),
            BorderBrush = new SolidColorBrush(notebook ? Color.FromArgb(130, 12, 8, 4) : darkPaper ? Color.FromRgb(55, 55, 55) : Color.FromArgb(70, 0, 0, 0)) };
        var layers = new Grid(); frame.Child = layers;
        if (notebook) layers.Children.Add(new Border { CornerRadius = new CornerRadius(inner), Background = NotebookStyle.Vignette });
        var body = new DockPanel(); layers.Children.Add(body);

        var band = new Border { CornerRadius = new CornerRadius(inner, inner, 0, 0), Height = 32, Padding = new Thickness(0, 0, 4, 0) };
        Brush headerInk = ink;
        if (notebook)
        {
            band.Background = new SolidColorBrush(Color.FromArgb(darkPaper ? (byte)150 : (byte)200, 22, 17, 12));
            headerInk = new SolidColorBrush(Color.FromRgb(242, 236, 224));
        }
        else band.Background = new SolidColorBrush(darkPaper ? Color.FromRgb(24, 24, 24) : Color.FromArgb(30, 0, 0, 0));
        bool lightInk = notebook || darkPaper;
        band.Resources["Ink"] = headerInk;
        band.Resources["Muted"] = new SolidColorBrush(lightInk ? Color.FromArgb(190, 255, 255, 255) : Color.FromArgb(170, 0, 0, 0));
        band.Resources["Hover"] = new SolidColorBrush(lightInk ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        band.Resources["Danger"] = new SolidColorBrush(lightInk ? Color.FromRgb(255, 169, 169) : Color.FromRgb(173, 48, 48));
        DockPanel.SetDock(band, Dock.Top); body.Children.Add(band);
        var head = new DockPanel(); band.Child = head;
        var tab = EdgeTabChip(note, inner);
        DockPanel.SetDock(tab, Dock.Left); head.Children.Add(tab);
        var when = new TextBlock { Text = CardStatus(note), FontSize = 11.5, Foreground = ink, Opacity = .6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 10, 9) };
        var actions = NoteActions(note, () => when.Text = CardStatus(note));
        foreach (var button in actions.Children.OfType<Button>()) { button.Width = button.Height = 24; button.FontSize = 11; }
        actions.Visibility = Visibility.Collapsed; actions.Margin = new Thickness(4, 0, 0, 0);
        DockPanel.SetDock(actions, Dock.Right); head.Children.Add(actions);
        head.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(note.Title) ? "Untitled note" : note.Title, Foreground = headerInk, FontSize = 13.5, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(9, 0, 0, 0) });

        DockPanel.SetDock(when, Dock.Bottom); body.Children.Add(when);
        var preview = Preview(note); var text = preview.Text;
        var excerpt = new TextBlock { FontFamily = AppFonts.Get(note.Font), FontSize = 13, Foreground = ink, Opacity = text.Length > 0 ? .9 : .45,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.WordEllipsis, Margin = new Thickness(12, 9, 10, 6) };
        if (text.Length == 0) excerpt.Text = "Empty";
        else foreach (var (line, index) in preview.Lines.Select((l, i) => (l, i)))
            {
                if (index > 0) excerpt.Inlines.Add(new LineBreak());
                foreach (var (piece, font) in line) excerpt.Inlines.Add(font == null ? new Run(piece) : new Run(piece) { FontFamily = PreviewFont(font) });
            }
        body.Children.Add(excerpt);
        var highlight = new Border { CornerRadius = new CornerRadius(inner), Margin = new Thickness(1), Opacity = 0, IsHitTestVisible = false,
            Background = new SolidColorBrush(darkPaper ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(16, 0, 0, 0)) };
        var card = new Grid { Height = CardHeight, Margin = new Thickness(0, 0, CardGap, CardGap) };
        card.Children.Add(frame); card.Children.Add(highlight);
        var outline = new Border { CornerRadius = new CornerRadius(corner), BorderThickness = new Thickness(2.5), IsHitTestVisible = false };
        outline.BorderBrush = SelectionBlue;
        var check = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 7, 7), IsHitTestVisible = false,
            Child = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        check.Background = SelectionBlue; ((TextBlock)check.Child).Foreground = Brushes.White;
        card.Children.Add(outline); card.Children.Add(check);
        void Mark(bool selected) => outline.Visibility = check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        Mark(selectedNotes.Contains(note.Id)); cardMarks[note.Id] = Mark;
        card.MouseEnter += (_, _) => actions.Visibility = Visibility.Visible;
        card.MouseLeave += (_, _) => actions.Visibility = Visibility.Collapsed;
        WireCard(card, highlight, actions, note, () =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { ToggleSelected(note.Id); return true; }
            if (selectedNotes.Count > 0) ClearSelection();
            return false;
        });
        card.ToolTip = "Open note · Shift+click to select";
        return card;
    }
    private static FontFamily PreviewFont(string source) => source.EndsWith("#" + AppFonts.Sans, StringComparison.Ordinal) || source == AppFonts.Sans ? AppFonts.Get(AppFonts.Sans) : new FontFamily(source);
    private static Border EdgeTabChip(NoteData note, double corner)
    {
        var color = EdgeTabAppearance.IsValidColor(note.EdgeTabColor) ? (Color)ColorConverter.ConvertFromString(note.EdgeTabColor) : Color.FromRgb(255, 241, 186);
        bool dark = color.R * .299 + color.G * .587 + color.B * .114 < 145;
        double iconOpacity = Math.Clamp(note.EdgeTabIconOpacity, 0, 1);
        FrameworkElement icon = EdgeTabAppearance.IconImage(note) is { } image
            ? new Image { Source = image, Width = 18, Height = 18, Stretch = Stretch.Uniform, Opacity = iconOpacity }
            : new TextBlock { Text = string.IsNullOrWhiteSpace(note.EdgeTabIcon) ? "▤" : note.EdgeTabIcon, FontSize = 15, Foreground = dark ? Brushes.White : Brushes.Black, Opacity = iconOpacity };
        icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Width = 32, Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(corner, 0, 5, 0), Child = icon,
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), BorderThickness = new Thickness(0, 0, 1, 1), IsHitTestVisible = false };
    }
    private static string CardStatus(NoteData note)
    {
        var summary = Preview(note);
        var tasks = summary.Items > 0 ? $"{summary.Done}/{summary.Items} done · " : "";
        var when = note.LastOpened is { } opened ? "Opened " + Ago(opened) : note.Created is { } created ? "Created " + Day(created) : "";
        return (tasks + when).TrimEnd(' ', '·');
    }
    private static Summary Preview(NoteData note)
    {
        var path = App.CurrentApp.Store.ContentPath(note);
        try
        {
            if (!File.Exists(path)) return new("", 0, 0);
            var written = File.GetLastWriteTimeUtc(path);
            if (previews.TryGetValue(note.Id, out var cached) && cached.Written == written) return cached.Summary;
            var summary = ReadSummary(path);
            previews[note.Id] = (written, summary); return summary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException) { return new("", 0, 0); }
    }
    public static string ReadPreview(string path, int limit = 320) => ReadSummary(path, limit).Text;
    public static Summary ReadSummary(string path, int limit = 320)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);
        var part = package.GetEntry("Xaml/Document.xaml");
        if (part == null) return new("", 0, 0);
        using var reader = XmlReader.Create(part.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var lines = new List<string>(); var styled = new List<IReadOnlyList<(string, string?)>>();
        var line = new List<(string Text, string? Font)>(); int length = 0, done = 0, items = 0;
        var fonts = new Stack<string?>(); fonts.Push(null);
        void EndLine()
        {
            var text = string.Concat(line.Select(p => p.Text)).Trim();
            if (text.Length > 0 && text[0] is NoteWindow.OpenBox or NoteWindow.DoneBox) { items++; if (text[0] == NoteWindow.DoneBox) done++; }
            if (text.Length > 0 && length < limit)
            {
                lines.Add(text); length += text.Length;
                var pieces = line.Where(p => p.Text.Length > 0).ToList();
                pieces[0] = (pieces[0].Text.TrimStart(), pieces[0].Font); pieces[^1] = (pieces[^1].Text.TrimEnd(), pieces[^1].Font);
                styled.Add(pieces.Where(p => p.Text.Length > 0).ToArray());
            }
            line.Clear();
        }
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Run")
            {
                var font = reader.GetAttribute("FontFamily") ?? fonts.Peek();
                if (reader.IsEmptyElement) { line.Add((reader.GetAttribute("Text") ?? "", font)); reader.Read(); }
                else line.Add((reader.ReadElementContentAsString(), font));
                continue;
            }
            if ((reader.NodeType == XmlNodeType.Element && reader.LocalName == "LineBreak") || (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Paragraph")) EndLine();
            if (reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement) fonts.Push(reader.Depth == 0 ? null : reader.GetAttribute("FontFamily") ?? fonts.Peek());
            else if (reader.NodeType == XmlNodeType.EndElement && fonts.Count > 1) fonts.Pop();
            reader.Read();
        }
        EndLine();
        var preview = string.Join("\n", lines);
        return new(preview.Length > limit ? preview[..limit] : preview, done, items) { Lines = styled };
    }
}
