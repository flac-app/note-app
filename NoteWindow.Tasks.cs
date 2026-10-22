using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace StickyNotes;

public sealed partial class NoteWindow
{
    internal const char OpenBox = '☐', DoneBox = '☑';
    private static readonly FontFamily BoxFont = new("Segoe UI Symbol");
    private readonly StackPanel reminderPanel = new() { Margin = new Thickness(12, 6, 12, 2) };
    private readonly TextBlock reminderText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly Button reminderSnooze = new() { Content = "Snooze 10 min", Padding = new Thickness(5, 2, 5, 2) };
    private readonly Button reminderDismiss = new() { Content = "Dismiss", Padding = new Thickness(5, 2, 5, 2) };

    private void ConfigureTasks(DockPanel body)
    {
        DockPanel.SetDock(reminderPanel, System.Windows.Controls.Dock.Top); body.Children.Add(reminderPanel);
        reminderPanel.Children.Add(reminderText);
        var reminderActions = new WrapPanel(); reminderPanel.Children.Add(reminderActions);
        reminderActions.Children.Add(reminderSnooze); reminderActions.Children.Add(reminderDismiss);
        reminderSnooze.Click += (_, _) => Snooze(TimeSpan.FromMinutes(10));
        reminderDismiss.Click += (_, _) => { if (Note.ReminderActive) DismissReminder(); else SetReminder(null); };
        editor.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (editor.IsReadOnly || (Keyboard.Modifiers & ModifierKeys.Control) != 0 || BoxAt(e.GetPosition(editor)) is not { } paragraph) return;
            ToggleItemDone(paragraph); e.Handled = true;
        };
        editor.PreviewMouseMove += (_, _) =>
        {
            if (BoxAt(Mouse.GetPosition(editor)) != null) editor.Cursor = Cursors.Hand; else editor.ClearValue(CursorProperty);
        };
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && !editor.IsReadOnly && editor.Selection.IsEmpty && ContinueChecklist()) e.Handled = true;
        };
        RefreshReminder();
    }

    private static TextPointer? BoxStart(Paragraph? paragraph)
    {
        if (paragraph == null) return null;
        var start = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        if (start.Paragraph != paragraph) return null;
        var text = start.GetTextInRun(LogicalDirection.Forward);
        return text.Length > 0 && text[0] is OpenBox or DoneBox ? start : null;
    }
    private static bool IsItem(Paragraph? paragraph) => BoxStart(paragraph) != null;
    private static bool IsDone(Paragraph paragraph) => BoxStart(paragraph)?.GetTextInRun(LogicalDirection.Forward)[0] == DoneBox;
    private static TextRange ItemText(Paragraph paragraph)
    {
        var start = BoxStart(paragraph)!.GetPositionAtOffset(1)!;
        if (start.GetNextInsertionPosition(LogicalDirection.Forward) is { } next && new TextRange(start, next).Text == " ") start = next;
        return new TextRange(start, paragraph.ContentEnd);
    }
    private static void StyleBox(Paragraph paragraph)
    {
        if (BoxStart(paragraph) is not { } box) return;
        var glyph = new TextRange(box, box.GetPositionAtOffset(1)!);
        if (!Equals(glyph.GetPropertyValue(TextElement.FontFamilyProperty), BoxFont)) glyph.ApplyPropertyValue(TextElement.FontFamilyProperty, BoxFont);
    }
    private void StyleBoxes()
    {
        var paragraphs = new List<Paragraph>();
        for (var p = editor.Document.ContentStart; p != null && p.CompareTo(editor.Document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is Paragraph paragraph && IsItem(paragraph)) paragraphs.Add(paragraph);
        foreach (var paragraph in paragraphs) StyleBox(paragraph);
    }
    private Paragraph? BoxAt(Point point)
    {
        if (!contentReadable || editor.GetPositionFromPoint(point, false) is not { } at || BoxStart(at.Paragraph) is not { } box) return null;
        var left = box.GetCharacterRect(LogicalDirection.Forward); var right = box.GetPositionAtOffset(1)!.GetCharacterRect(LogicalDirection.Backward);
        var area = new Rect(new Point(left.Left - 2, left.Top), new Point(right.Right + 2, left.Bottom));
        return area.Contains(point) ? at.Paragraph : null;
    }
    private void ToggleItemDone(Paragraph paragraph)
    {
        bool done = !IsDone(paragraph);
        editor.BeginChange();
        try
        {
            var box = BoxStart(paragraph)!;
            new TextRange(box, box.GetPositionAtOffset(1)!).Text = done ? DoneBox.ToString() : OpenBox.ToString();
            StyleBox(paragraph);
        }
        finally { editor.EndChange(); }
        dirty = true; QueueSave(); UpdateFormatStates();
    }
    private List<Paragraph> SelectedParagraphs()
    {
        var paragraphs = new List<Paragraph>();
        if (editor.Selection.Start.Paragraph is { } first) paragraphs.Add(first);
        for (var p = editor.Selection.Start; p != null && p.CompareTo(editor.Selection.End) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is Paragraph paragraph && !paragraphs.Contains(paragraph)) paragraphs.Add(paragraph);
        return paragraphs;
    }
    private void ToggleChecklist()
    {
        if (editor.IsReadOnly) return;
        editor.Focus();
        editor.BeginChange();
        try
        {
            SplitSelectedLines();
            foreach (var paragraph in SelectedParagraphs()) foreach (var lineBreak in LineBreaks(paragraph)) SplitAt(lineBreak);
            var paragraphs = SelectedParagraphs();
            bool remove = paragraphs.Count > 0 && paragraphs.All(IsItem);
            foreach (var paragraph in paragraphs)
                if (remove) MakePlain(paragraph);
                else if (!IsItem(paragraph)) MakeItem(paragraph, false);
        }
        finally { editor.EndChange(); }
        dirty = true; QueueSave(); UpdateFormatStates();
    }
    private static void MakeItem(Paragraph paragraph, bool done)
    {
        var start = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        start.InsertTextInRun((done ? DoneBox : OpenBox) + " ");
        StyleBox(paragraph);
        paragraph.Margin = new Thickness(0);
    }
    private static void MakePlain(Paragraph paragraph)
    {
        var box = BoxStart(paragraph)!;
        new TextRange(box, ItemText(paragraph).Start).Text = "";
        paragraph.ClearValue(Block.MarginProperty);
    }
    private bool ContinueChecklist()
    {
        if (editor.CaretPosition.Paragraph is not { } paragraph || !IsItem(paragraph)) return false;
        if (editor.CaretPosition.CompareTo(ItemText(paragraph).Start) < 0) return false;
        editor.BeginChange();
        try
        {
            if (ItemText(paragraph).Text.Trim().Length == 0) { MakePlain(paragraph); return true; }
            EditingCommands.EnterParagraphBreak.Execute(null, editor);
            if (editor.CaretPosition.Paragraph is not { } next) return true;
            var box = new Run(OpenBox + " ", next.ContentStart.GetInsertionPosition(LogicalDirection.Forward));
            var typing = box.ContentEnd;
            StyleBox(next);
            editor.CaretPosition = typing;
        }
        finally { editor.EndChange(); }
        dirty = true; QueueSave(); UpdateFormatStates();
        return true;
    }
    private void MigrateChecklist()
    {
#pragma warning disable CS0612
        if (Note.Checklist.Count == 0 || !contentReadable) return;
        var first = editor.Document.Blocks.FirstBlock;
        foreach (var item in Note.Checklist)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            paragraph.Inlines.Add(new Run((item.Done ? DoneBox : OpenBox).ToString()) { FontFamily = BoxFont });
            paragraph.Inlines.Add(new Run(" "));
            paragraph.Inlines.Add(new Run(item.Text));
            if (first != null) editor.Document.Blocks.InsertBefore(first, paragraph); else editor.Document.Blocks.Add(paragraph);
        }
        Note.Checklist.Clear(); Note.ShowChecklist = false;
#pragma warning restore CS0612
        editor.Document.Foreground = Foreground;
        dirty = true; QueueSave();
    }

    private void RefreshTaskAppearance()
    {
        reminderText.Foreground = Foreground;
        foreach (var button in new[] { reminderSnooze, reminderDismiss })
        {
            button.Background = headerBand.Background; button.Foreground = Foreground; button.BorderBrush = frame.BorderBrush;
        }
    }

    internal void SetReminder(DateTimeOffset? at)
    {
        Note.ReminderAt = at; Note.ReminderAnchor = at; Note.ReminderActive = false;
        if (at == null) Note.ReminderRepeat = ReminderRepeat.Once;
        RefreshReminder(); ApplyAppearance(); App.CurrentApp.Save(); ReminderSounds.StopIfAnswered(); ReminderPopup.CloseAnswered();
    }
    internal void Snooze(TimeSpan delay)
    {
        Note.ReminderAt = DateTimeOffset.Now + delay; Note.ReminderActive = false;
        RefreshReminder(); ApplyAppearance(); App.CurrentApp.Save(); ReminderSounds.StopIfAnswered(); ReminderPopup.CloseAnswered();
    }
    internal void DismissReminder()
    {
        Note.ReminderActive = false;
        RefreshReminder(); ApplyAppearance(); App.CurrentApp.Save(); ReminderSounds.StopIfAnswered(); ReminderPopup.CloseAnswered();
    }

    internal void RefreshReminder()
    {
        reminderPanel.Visibility = Note.ReminderActive || Note.ReminderAt.HasValue ? Visibility.Visible : Visibility.Collapsed;
        var repeats = Note.ReminderRepeat == ReminderRepeat.Once ? "" : " · " + ReminderSchedule.Label(Note.ReminderRepeat).ToLowerInvariant();
        reminderText.Text = Note.ReminderActive ? "Reminder · " + Note.Title + (Note.ReminderAt is { } next ? " · next " + next.ToLocalTime().ToString("g") : "")
            : "Reminder · " + Note.ReminderAt?.ToLocalTime().ToString("g") + repeats;
        reminderSnooze.Visibility = Note.ReminderActive ? Visibility.Visible : Visibility.Collapsed;
        reminderDismiss.Content = Note.ReminderActive ? "Dismiss" : "Cancel reminder";
    }

    internal void ShowReminder() { RefreshReminder(); Reveal(); }
}
