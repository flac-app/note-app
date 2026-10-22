using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace StickyNotes;
public sealed partial class NoteWindow
{
    private static readonly (TextAlignment Alignment, string Label, string Keys)[] Alignments =
    {
        (TextAlignment.Left, "Align left", "Ctrl + L"), (TextAlignment.Center, "Center", "Ctrl + E"),
        (TextAlignment.Right, "Align right", "Ctrl + R"), (TextAlignment.Justify, "Justify", "Ctrl + J")
    };
    private void ConfigureAlignment()
    {
        var button = formatButtons[Key.L];
        button.ToolTip = "Text alignment · Ctrl + L / E / R / J";
        editor.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, e) =>
        {
            TextAlignment? alignment = e.Command == EditingCommands.AlignLeft ? TextAlignment.Left : e.Command == EditingCommands.AlignCenter ? TextAlignment.Center
                : e.Command == EditingCommands.AlignRight ? TextAlignment.Right : e.Command == EditingCommands.AlignJustify ? TextAlignment.Justify : null;
            if (alignment is { } chosen) { e.Handled = true; SetAlignment(chosen); }
        }), true);
    }
    private TextAlignment? SelectionAlignment() => editor.Selection.GetPropertyValue(Block.TextAlignmentProperty) is TextAlignment alignment ? alignment : null;
    private void ShowAlignmentMenu()
    {
        var button = formatButtons[Key.L];
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Top }; AppTheme.ApplyResources(menu);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var current = SelectionAlignment();
        foreach (var (alignment, label, keys) in Alignments)
        {
            var option = new ToggleButton { Content = AlignmentIcon(alignment), Focusable = false, Style = (Style)FindResource("FormatButton"), IsChecked = current == alignment, ToolTip = label + " · " + keys };
            option.SetResourceReference(ForegroundProperty, "Ink");
            option.Click += (_, _) => { menu.IsOpen = false; SetAlignment(alignment); };
            row.Children.Add(option);
        }
        menu.Items.Add(new MenuItem { Header = row, StaysOpenOnClick = true });
        menu.IsOpen = true;
    }
    private void SetAlignment(TextAlignment alignment)
    {
        if (editor.IsReadOnly) return;
        editor.Focus();
        editor.BeginChange();
        try
        {
            SplitSelectedLines();
            editor.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, alignment);
        }
        finally { editor.EndChange(); }
        AlignmentApplied();
    }
    private void SplitSelectedLines()
    {
        var selection = editor.Selection;
        bool empty = selection.IsEmpty;
        static bool Touching(TextPointer from, TextPointer to) => new TextRange(from, to).Text.Length == 0;
        if (selection.End.Paragraph is { } last)
        {
            var after = LineBreaks(last).FirstOrDefault(b => b.ElementStart.CompareTo(selection.End) >= 0 || !empty && b.ElementEnd.CompareTo(selection.End) <= 0 && Touching(b.ElementEnd, selection.End) && b.ElementStart.CompareTo(selection.Start) > 0);
            if (after != null) SplitAt(after);
        }
        if (selection.Start.Paragraph is { } first)
        {
            var before = LineBreaks(first).LastOrDefault(b => b.ElementEnd.CompareTo(selection.Start) <= 0 || !empty && b.ElementStart.CompareTo(selection.Start) >= 0 && Touching(selection.Start, b.ElementStart) && b.ElementEnd.CompareTo(selection.End) < 0);
            if (before != null) SplitAt(before);
        }
    }
    private static List<LineBreak> LineBreaks(Paragraph paragraph)
    {
        var breaks = new List<LineBreak>();
        for (var p = paragraph.ContentStart; p != null && p.CompareTo(paragraph.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak lineBreak) breaks.Add(lineBreak);
        return breaks;
    }
    private static void SplitAt(LineBreak lineBreak)
    {
        var at = lineBreak.ElementStart;
        lineBreak.SiblingInlines.Remove(lineBreak);
        if (at.InsertParagraphBreak().Paragraph is not { } lower) return;
        lower.Margin = new Thickness(lower.Margin.Left, 0, lower.Margin.Right, lower.Margin.Bottom);
        if (lower.PreviousBlock is Paragraph upper) upper.Margin = new Thickness(upper.Margin.Left, upper.Margin.Top, upper.Margin.Right, 0);
    }
    private void AlignmentApplied()
    {
        for (var p = editor.Selection.Start; p != null; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetAdjacentElement(LogicalDirection.Forward) is BlockUIContainer { Child: Image image } block)
                image.HorizontalAlignment = block.TextAlignment switch { TextAlignment.Center => HorizontalAlignment.Center, TextAlignment.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Left };
            if (p.CompareTo(editor.Selection.End) >= 0) break;
        }
        imageAdorner?.InvalidateArrange();
        dirty = true; QueueSave(); UpdateFormatStates();
    }
    private void UpdateAlignmentButton(ToggleButton button)
    {
        var alignment = SelectionAlignment() ?? TextAlignment.Left;
        if (button.Tag is not TextAlignment shown || shown != alignment) { button.Content = AlignmentIcon(alignment); button.Tag = alignment; }
        button.IsChecked = false;
    }
    private static FrameworkElement AlignmentIcon(TextAlignment alignment)
    {
        (double Start, double End)[] lines = alignment switch
        {
            TextAlignment.Center => new[] { (0d, 12d), (2.5, 9.5), (0d, 12d), (3.5, 8.5) },
            TextAlignment.Right => new[] { (0d, 12d), (4.5, 12d), (0d, 12d), (5.5, 12d) },
            TextAlignment.Justify => new[] { (0d, 12d), (0d, 12d), (0d, 12d), (0d, 6.5) },
            _ => new[] { (0d, 12d), (0d, 7.5), (0d, 12d), (0d, 6.5) }
        };
        var geometry = new GeometryGroup();
        for (int i = 0; i < lines.Length; i++) geometry.Children.Add(new RectangleGeometry(new Rect(lines[i].Start, i * 3, lines[i].End - lines[i].Start, 1.5)));
        var path = new Path { Data = geometry, Width = 12, Height = 10.5, IsHitTestVisible = false, SnapsToDevicePixels = true };
        path.SetBinding(Shape.FillProperty, new Binding(nameof(Control.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        return path;
    }
}
