using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
namespace StickyNotes;
public partial class App : Application
{
    private readonly List<NoteWindow> windows = new();
    private Forms.NotifyIcon? tray;
    private Mutex? instance;
    private LibraryWindow? library;
    private DispatcherTimer? watcher;
    private DispatcherTimer? reminderWatcher;
    private HashSet<string> running = new(StringComparer.OrdinalIgnoreCase);
    private GlobalHotkeys? hotkeys;
    public GlobalHotkeys Hotkeys => hotkeys ??= new GlobalHotkeys(RunShortcut);
    public static App CurrentApp => (App)Current;
    public bool Exiting { get; private set; }
    public NoteStore Store { get; } = new();
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mutexName = "Local\\LittleLeafStickyNotes" + (e.Args.Contains("--smoke-test") ? "-Smoke-" + Environment.ProcessId : "");
        instance = new Mutex(true, mutexName, out bool first);
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, mutexName + "-Show");
        if (!first)
        {
            AllowSetForegroundWindow(-1);
            showSignal.Set(); showSignal.Dispose(); Shutdown(); return;
        }
        ListenForShowRequests(showSignal);
        Store.Load();
        ReminderSounds.InstallDefaults();
        Hotkeys.Apply(Store.Preferences);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Notes library", null, (_, _) => Dispatcher.Invoke(ShowLibrary));
        menu.Items.Add("New note", null, (_, _) => Dispatcher.Invoke(() => NewNote()));
        menu.Items.Add("Show all notes", null, (_, _) => Dispatcher.Invoke(ShowAll));
        menu.Items.Add("Hide all notes", null, (_, _) => Dispatcher.Invoke(HideAll));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open notes folder", null, (_, _) => Process.Start(new ProcessStartInfo(Store.Root) { UseShellExecute = true }));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        tray = new Forms.NotifyIcon { Text = "note.", Icon = TrayIcon(), ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowLibrary);
        if (Store.Notes.Count == 0) Store.Notes.Add(new NoteData { Title = "A little space for your thoughts", Created = DateTimeOffset.Now });
        RestoreWindows();
        if (Store.UnavailableLocation != null)
            MessageBox.Show("Your notes folder could not be found:\n" + Store.UnavailableLocation + "\n\nnote. is using its default folder until that folder is available again, for example when its drive is reconnected.", "note.");
        watcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        watcher.Tick += (_, _) => CheckTriggers(); watcher.Start(); CheckTriggers();
        reminderWatcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        reminderWatcher.Tick += (_, _) => CheckReminders(DateTimeOffset.Now); reminderWatcher.Start(); CheckReminders(DateTimeOffset.Now);
        if (e.Args.Contains("--smoke-test"))
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { timer.Stop(); ExitApp(); }; timer.Start();
        }
    }
    private const int TrayMarkSize = 24;
    private System.Drawing.Icon TrayIcon(int? size = null)
    {
        int pixels = size ?? Forms.SystemInformation.SmallIconSize.Width;
        using var stream = GetResourceStream(new Uri("pack://application:,,,/note.app;component/NoteappFinal.ico")).Stream;
        if (pixels <= TrayMarkSize) return new System.Drawing.Icon(stream, pixels, pixels);
        using var mark = new System.Drawing.Icon(stream, TrayMarkSize, TrayMarkSize);
        using var source = mark.ToBitmap();
        using var scaled = new System.Drawing.Bitmap(pixels, pixels);
        using (var graphics = System.Drawing.Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, pixels, pixels);
        }
        using var png = new MemoryStream();
        scaled.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0); writer.Write((short)1); writer.Write((short)1);
            writer.Write((byte)pixels); writer.Write((byte)pixels); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((short)1); writer.Write((short)32); writer.Write((int)png.Length); writer.Write(22);
            png.WriteTo(writer.BaseStream);
        }
        ico.Position = 0;
        return new System.Drawing.Icon(ico, pixels, pixels);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    private void ListenForShowRequests(EventWaitHandle signal)
    {
        var listener = new Thread(() =>
        {
            while (signal.WaitOne()) { if (Exiting) return; Dispatcher.BeginInvoke(ShowLibrary); }
        }) { IsBackground = true, Name = "note. show requests" };
        listener.Start();
    }
    private void RestoreWindows()
    {
        ShowLibrary();
        foreach (var note in Store.Notes.OrderBy(n => n.OpenOnStartup).ToArray())
            RestoreNote(CreateWindow(note));
    }
    private void RestoreNote(NoteWindow window)
    {
        if (window.Note.OpenOnStartup || window.Note.ReminderActive) window.Reveal();
        else if (!window.Note.Hidden) { window.Show(); if (window.Note.Docked) window.Dock(); }
    }
    private NoteWindow CreateWindow(NoteData note) { var w = new NoteWindow(note); windows.Add(w); return w; }
    public void NewNote()
    {
        var note = new NoteData { X = 100 + windows.Count % 6 * 32, Y = 100 + windows.Count % 6 * 32, Created = DateTimeOffset.Now, LastOpened = DateTimeOffset.Now };
        Store.Notes.Add(note); CreateWindow(note).Reveal();
    }
    public void ShowLibrary()
    {
        library ??= new LibraryWindow(); library.Refresh(); library.Show();
        library.WindowState = WindowState.Normal; library.Activate();
    }
    public bool OpenNote(Guid id)
    {
        var note = Store.Notes.FirstOrDefault(n => n.Id == id);
        if (note == null) return false;
        var window = windows.FirstOrDefault(w => w.Note.Id == id) ?? CreateWindow(note);
        window.Reveal(); return true;
    }
    public void ShowAll() { foreach (var w in windows) w.Reveal(); }
    public void HideAll() => windows.ForEach(w => w.HideNote());
    private void RunShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.NewNote: NewNote(); break;
            case ShortcutAction.ShowLibrary: ShowLibrary(); break;
            case ShortcutAction.ShowAllNotes: ShowAll(); break;
            case ShortcutAction.HideAllNotes: HideAll(); break;
        }
    }
    public void ApplyShortcuts()
    {
        Hotkeys.Apply(Store.Preferences);
        foreach (var window in windows) window.RefreshShortcutHints();
        Save();
    }
    internal void ArrangeTabs(EdgeTabWindow? anchor = null) => EdgeTabWindow.Arrange(windows, anchor);
    public void HidePreviewsExcept(NoteWindow keep, Func<NoteWindow, bool>? only = null) { foreach (var window in windows) if (window != keep && (only == null || only(window))) window.HideEdgePreview(); }
    public void RaiseTabs() { foreach (var window in windows) window.EdgeTab?.BringToFront(); }
    public void Delete(NoteWindow window) => DeleteNote(window.Note, window);
    public void DeleteNote(NoteData note, Window owner)
    {
        var name = string.IsNullOrWhiteSpace(note.Title) ? "Untitled note" : note.Title;
        if (!ConfirmDialog.Ask(owner, "Delete note?", "“" + name + "” and its contents will be permanently deleted.", "Delete", danger: true)) return;
        try { RemoveNote(note); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ConfirmDialog.Inform(owner, "Note not fully deleted", "The note could not be fully deleted.\n" + ex.Message); }
    }
    public void DeleteNotes(IReadOnlyList<NoteData> notes, Window owner)
    {
        if (notes.Count == 1) { DeleteNote(notes[0], owner); return; }
        if (notes.Count == 0) return;
        var names = notes.Select(n => "“" + (string.IsNullOrWhiteSpace(n.Title) ? "Untitled note" : n.Title) + "”").ToList();
        var named = names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + $" and {names.Count - 3} more";
        if (!ConfirmDialog.Ask(owner, $"Delete {notes.Count} notes?", named + " and their contents will be permanently deleted.", "Delete", danger: true)) return;
        var failed = new List<string>();
        foreach (var note in notes)
            try { RemoveNote(note); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(note.Title + ": " + ex.Message); }
        if (failed.Count > 0) ConfirmDialog.Inform(owner, "Some notes not fully deleted", string.Join("\n", failed));
    }
    private void RemoveNote(NoteData note)
    {
        int index = Store.Notes.IndexOf(note);
        if (index < 0) return;
        Store.Notes.RemoveAt(index);
        try { Store.Save(); }
        catch { Store.Notes.Insert(index, note); throw; }
        foreach (var window in windows.Where(w => w.Note.Id == note.Id).ToArray())
        { windows.Remove(window); window.AllowClose = true; window.Close(); }
        Store.DeleteContent(note); library?.Refresh();
        ReminderSounds.StopIfAnswered(); ReminderPopup.CloseAnswered();
    }
    public void Save()
    {
        try { Store.Save(); library?.Refresh(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { tray?.ShowBalloonTip(5000, "Notes could not be saved", ex.Message, Forms.ToolTipIcon.Warning); }
    }
    private void CheckTriggers()
    {
        var now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process) { try { now.Add(process.ProcessName); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        }
        foreach (var window in windows)
            if (window.Note.Apps.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => NormalizeAppName(s)).Any(s => now.Contains(s) && !running.Contains(s))) window.Reveal();
        running = now;
    }
    private static string NormalizeAppName(string value)
    {
        var name = Path.GetFileName(value.Trim().Trim('"'));
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
    private void CheckReminders(DateTimeOffset now)
    {
        var due = Store.Notes.Where(n => n.ReminderAt.HasValue && n.ReminderAt.Value <= now).ToArray();
        if (due.Length > 0) ReminderSounds.Play(ReminderSounds.For(due[0]), due.Any(ReminderSounds.LoopsFor));
        foreach (var note in due)
        {
            var at = note.ReminderAt!.Value;
            note.ReminderAt = note.ReminderRepeat == ReminderRepeat.Once ? null : ReminderSchedule.Next(note.ReminderAnchor ?? at, note.ReminderRepeat, now);
            note.ReminderActive = true;
            var window = windows.FirstOrDefault(w => w.Note.Id == note.Id) ?? CreateWindow(note);
            window.ShowReminder();
            if (ReminderPopup.ShowsFor(note)) ReminderPopup.Ring(window);
        }
    }
    public bool SaveEverything() { foreach (var w in windows) if (!w.Flush()) return false; Store.Save(); return true; }
    public void ExitApp() { foreach (var w in windows) if (!w.Flush()) return; Save(); Exiting = true; Shutdown(); }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        foreach (var window in windows) window.Flush();
        Exiting = true; base.OnSessionEnding(e);
    }
    protected override void OnExit(ExitEventArgs e) { watcher?.Stop(); reminderWatcher?.Stop(); hotkeys?.Dispose(); var icon = tray?.Icon; tray?.Dispose(); icon?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}


