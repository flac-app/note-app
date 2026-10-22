using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace StickyNotes;

internal static class ReminderSchedule
{
    public static readonly ReminderRepeat[] Choices = { ReminderRepeat.Once, ReminderRepeat.Daily, ReminderRepeat.Weekdays, ReminderRepeat.Weekly, ReminderRepeat.Monthly };
    public static string Label(ReminderRepeat repeat) => repeat switch
    {
        ReminderRepeat.Daily => "Every day", ReminderRepeat.Weekdays => "Weekdays (Mon–Fri)", ReminderRepeat.Weekly => "Every week", ReminderRepeat.Monthly => "Every month", _ => "Only once"
    };
    public static DateTimeOffset? Next(DateTimeOffset anchor, ReminderRepeat repeat, DateTimeOffset after)
    {
        if (repeat == ReminderRepeat.Once) return null;
        var start = anchor.LocalDateTime;
        for (int step = 1; step < 4000; step++)
        {
            var local = repeat switch
            {
                ReminderRepeat.Weekly => start.AddDays(7 * step),
                ReminderRepeat.Monthly => start.AddMonths(step),
                _ => start.AddDays(step)
            };
            if (repeat == ReminderRepeat.Weekdays && local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
            var at = new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
            if (at > after) return at;
        }
        return null;
    }
}
public sealed class ReminderWindow : Window
{
    public ReminderWindow(NoteWindow note)
    {
        Title = "Remind me"; Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AppTheme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Remind me about this note", FontSize = 18, Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(new TextBlock { Text = "Message · optional", Margin = new Thickness(0, 0, 0, 6) });
        var message = new TextBox { Text = note.Note.ReminderMessage, Padding = new Thickness(6), MaxLength = 280, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 48, MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 14), ToolTip = "Shown in the reminder pop-up under the note's title" };
        panel.Children.Add(message);
        Closed += (_, _) => { var text = message.Text.Trim(); if (text != note.Note.ReminderMessage) { note.Note.ReminderMessage = text; App.CurrentApp.Save(); } };
        var quick = new WrapPanel(); panel.Children.Add(quick);
        foreach (int minutes in new[] { 5, 15, 60 })
        {
            var button = new Button { Content = minutes == 60 ? "In 1 hour" : $"In {minutes} min" };
            button.Click += (_, _) => { note.SetReminder(DateTimeOffset.Now.AddMinutes(minutes)); Close(); }; quick.Children.Add(button);
        }
        var initial = (note.Note.ReminderAt ?? DateTimeOffset.Now.AddHours(1)).LocalDateTime;
        panel.Children.Add(new TextBlock { Text = "Or choose a local date and time", Margin = new Thickness(0, 14, 0, 6) });
        var date = new DatePicker { SelectedDate = initial.Date }; panel.Children.Add(date);
        var time = new TextBox { Text = initial.ToString("HH:mm"), Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), ToolTip = "24-hour time · HH:mm" }; panel.Children.Add(time);
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }; panel.Children.Add(error);
        panel.Children.Add(new TextBlock { Text = "Repeat", Margin = new Thickness(0, 14, 0, 6) });
        var repeat = new ComboBox { ItemsSource = ReminderSchedule.Choices.Select(ReminderSchedule.Label).ToArray(), SelectedIndex = Array.IndexOf(ReminderSchedule.Choices, note.Note.ReminderRepeat), ToolTip = "Repeats count from the time you set" };
        repeat.SelectionChanged += (_, _) => { if (repeat.SelectedIndex >= 0) { note.Note.ReminderRepeat = ReminderSchedule.Choices[repeat.SelectedIndex]; note.RefreshReminder(); App.CurrentApp.Save(); } };
        panel.Children.Add(repeat);
        panel.Children.Add(new TextBlock { Text = "Sound", Margin = new Thickness(0, 14, 0, 6) });
        panel.Children.Add(SoundPicker(note.Note));
        bool appLoops = App.CurrentApp.Store.Preferences.ReminderSoundLoops;
        bool?[] loopValues = { null, false, true };
        var loops = new ComboBox { ItemsSource = new[] { "App setting · " + (appLoops ? "repeats until dismissed" : "plays once"), "Plays once", "Repeats until dismissed" }, SelectedIndex = Array.IndexOf(loopValues, note.Note.ReminderSoundLoops), Margin = new Thickness(0, 6, 0, 0), ToolTip = "Repeat the sound until you dismiss or snooze the reminder" };
        loops.SelectionChanged += (_, _) => { if (loops.SelectedIndex >= 0) { note.Note.ReminderSoundLoops = loopValues[loops.SelectedIndex]; App.CurrentApp.Save(); } };
        panel.Children.Add(loops);
        panel.Children.Add(new TextBlock { Text = "Pop-up", Margin = new Thickness(0, 14, 0, 6) });
        bool appPopup = App.CurrentApp.Store.Preferences.ReminderPopup;
        bool?[] popupValues = { null, true, false };
        var popupRow = new DockPanel(); panel.Children.Add(popupRow);
        var previewPopup = new Button { Content = "Preview", ToolTip = "Show this reminder's pop-up", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        previewPopup.Click += (_, _) => ReminderPopup.Preview(note.Note.Title, message.Text);
        DockPanel.SetDock(previewPopup, Dock.Right); popupRow.Children.Add(previewPopup);
        var popup = new ComboBox { ItemsSource = new[] { "App setting · " + (appPopup ? "shows a pop-up" : "sound only"), "Shows a pop-up", "Sound only, no pop-up" }, SelectedIndex = Array.IndexOf(popupValues, note.Note.ReminderPopup), ToolTip = "Without the pop-up, the sound still plays and the note still opens" };
        popup.SelectionChanged += (_, _) => { if (popup.SelectedIndex >= 0) { note.Note.ReminderPopup = popupValues[popup.SelectedIndex]; App.CurrentApp.Save(); } };
        popupRow.Children.Add(popup);
        panel.Children.Add(new TextBlock { Text = "Keep note. running in the tray for reminders. Missed reminders appear the next time you open the app.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = AppTheme.Muted, Margin = new Thickness(0, 10, 0, 10) });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(actions);
        var cancelReminder = new Button { Content = "Clear reminder", IsEnabled = note.Note.ReminderAt.HasValue || note.Note.ReminderActive }; actions.Children.Add(cancelReminder);
        cancelReminder.Click += (_, _) => { note.SetReminder(null); Close(); };
        var save = new Button { Content = "Set reminder", IsDefault = true }; actions.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (!TryGetReminder(date.SelectedDate, time.Text, DateTimeOffset.Now, out var at)) { error.Text = "Choose a future date and a valid time such as 14:30."; return; }
            note.SetReminder(at); Close();
        };
    }
    private static FrameworkElement SoundPicker(NoteData note)
    {
        var row = new DockPanel();
        var preview = new Button { Content = "▶", ToolTip = "Play this sound", MinWidth = 0, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(preview, Dock.Right); row.Children.Add(preview);
        var choice = new ComboBox { MinWidth = 180 }; row.Children.Add(choice);
        string?[] values = Array.Empty<string?>();
        void Fill()
        {
            var appSound = App.CurrentApp.Store.Preferences.ReminderSound;
            var files = ReminderSounds.Available();
            values = new string?[] { null, "" }.Concat(files).ToArray();
            choice.ItemsSource = new[] { "App sound · " + (appSound.Length == 0 ? "None" : ReminderSounds.Label(appSound)), "None" }.Concat(files.Select(ReminderSounds.Label)).ToArray();
            int index = Array.FindIndex(values, v => v == note.ReminderSound);
            choice.SelectedIndex = index >= 0 ? index : 0;
        }
        Fill();
        choice.DropDownOpened += (_, _) => { var keep = note.ReminderSound; Fill(); note.ReminderSound = keep; };
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedIndex < 0 || choice.SelectedIndex >= values.Length || values[choice.SelectedIndex] == note.ReminderSound) return;
            note.ReminderSound = values[choice.SelectedIndex]; App.CurrentApp.Save();
        };
        preview.Click += (_, _) => ReminderSounds.Play(ReminderSounds.For(note));
        return row;
    }
    internal static bool TryGetReminder(DateTime? date, string time, DateTimeOffset now, out DateTimeOffset at)
    {
        at = default;
        if (!date.HasValue || !TimeSpan.TryParseExact(time.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var clock) || clock.TotalHours >= 24) return false;
        var local = DateTime.SpecifyKind(date.Value.Date + clock, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) return false;
        at = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return at > now;
    }
}
