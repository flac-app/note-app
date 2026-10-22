using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace StickyNotes;
public sealed class NoteData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled note";
    public DateTimeOffset? Created { get; set; }
    public DateTimeOffset? LastOpened { get; set; }
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 430;
    public bool Pinned { get; set; }
    public double Opacity { get; set; } = 1;
    public bool HoverOpacityEnabled { get; set; }
    public double HoverOpacity { get; set; } = 1;
    public bool ShrinkUnlessHovered { get; set; }
    public int CompactClickHoldSeconds { get; set; } = 5;
    public int CompactAnimationMilliseconds { get; set; } = 200;
    public bool HideFromAltTab { get; set; } = true;
    [Obsolete]
    public bool ShowChecklist { get; set; }
    [Obsolete]
    public List<ChecklistItem> Checklist { get; set; } = new();
    public DateTimeOffset? ReminderAt { get; set; }
    public bool ReminderActive { get; set; }
    public string? ReminderSound { get; set; }
    public ReminderRepeat ReminderRepeat { get; set; }
    public bool? ReminderSoundLoops { get; set; }
    public DateTimeOffset? ReminderAnchor { get; set; }
    public string ReminderMessage { get; set; } = "";
    public bool? ReminderPopup { get; set; }
    public string Color { get; set; } = "#000000";
    public string Font { get; set; } = AppFonts.Sans;
    public double FontSize { get; set; } = 16;
    public string Apps { get; set; } = "";
    public bool OpenOnStartup { get; set; }
    public bool Hidden { get; set; }
    public bool Docked { get; set; }
    public string EdgeTabColor { get; set; } = "#000000";
    public string EdgeTabIcon { get; set; } = "▤";
    public double EdgeTabOpacity { get; set; } = 1;
    public double EdgeTabIconOpacity { get; set; } = 1;
    public string EdgeTabTopLabel { get; set; } = "";
    public string EdgeTabLabel { get; set; } = "";
    public double? EdgeTabY { get; set; }
    public string? EdgeTabScreen { get; set; }
    public string Edge { get; set; } = "Right";
    public string? Paper { get; set; }
    public string? TabStyle { get; set; }
    public double EdgeTabScale { get; set; } = 1;
    public bool AutoHide { get; set; } = true;
    public int? AutoHideMilliseconds { get; set; }
}
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ReminderRepeat { Once, Daily, Weekdays, Weekly, Monthly }
public sealed class ChecklistItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}
public sealed class AppPreferences
{
    public bool WornTabs { get; set; }
    public bool SmoothHover { get; set; } = true;
    public int HoverCloseDelayMilliseconds { get; set; } = 350;
    public bool DarkMode { get; set; } = true;
    public bool DarkDefaultsApplied { get; set; }
    public List<ShortcutBinding> Shortcuts { get; set; } = ShortcutBinding.Defaults();
    public List<CommandShortcut> Commands { get; set; } = CommandShortcut.Defaults();
    public bool NotebookStyle { get; set; }
    public bool? NotebookTabs { get; set; } = true;
    public bool SmoothSeparators { get; set; } = true;
    public bool NotebookPull { get; set; } = true;
    public int PullAutoCloseSeconds { get; set; }
    public int CornerRadius { get; set; } = 6;
    public bool LibraryGrid { get; set; } = true;
    public int DoublePressMilliseconds { get; set; } = 300;
    public int HoldMilliseconds { get; set; } = 500;
    public string ReminderSound { get; set; } = StickyNotes.ReminderSounds.DefaultSound;
    public bool ReminderSoundLoops { get; set; }
    public int ReminderVolume { get; set; } = 80;
    public bool ReminderPopup { get; set; } = true;
    public Dictionary<string, string> InstalledSounds { get; set; } = new();
    [Obsolete]
    public bool DefaultSoundsInstalled { get; set; }
}
public sealed class NoteStore
{
    public static string DefaultRoot { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "flac.app", "StickyNotes");
    private static string LegacyRoot { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleLeaf", "StickyNotes");
    private static string LocationFile => Path.Combine(Path.GetDirectoryName(DefaultRoot)!, "StickyNotes-location.txt");
    private static string LegacyLocationFile => Path.Combine(Path.GetDirectoryName(LegacyRoot)!, "StickyNotes-location.txt");
    private const string BackupStamp = "yyyyMMdd-HHmm";
    public NoteStore()
    {
        var overridden = Environment.GetEnvironmentVariable("STICKYNOTES_DATA_DIR");
        LocationLocked = overridden != null;
        if (overridden == null) MigrateLegacyFolder();
        var saved = overridden == null ? SavedLocation() : null;
        if (saved != null && !Directory.Exists(saved)) { UnavailableLocation = saved; saved = null; }
        Root = overridden ?? saved ?? DefaultRoot;
    }
    public string Root { get; private set; }
    public bool LocationLocked { get; }
    public string? UnavailableLocation { get; }
    public AppPreferences Preferences { get; private set; } = new();
    public List<NoteData> Notes { get; private set; } = new();
    public string ContentPath(NoteData note) => Path.Combine(Root, note.Id + ".xamlpkg");
    public void Load()
    {
        Directory.CreateDirectory(Root);
        var preferencesPath = Path.Combine(Root, "preferences.json");
        if (File.Exists(preferencesPath))
        {
            try { Preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(preferencesPath)) ?? new(); }
            catch (JsonException) { File.Copy(preferencesPath, preferencesPath + ".invalid-" + DateTime.Now.Ticks); }
        }
        if (!Preferences.DarkDefaultsApplied) { Preferences.DarkMode = true; Preferences.DarkDefaultsApplied = true; }
        Preferences.Commands = CommandShortcut.Normalize(Preferences.Commands);
        Preferences.NotebookTabs ??= Preferences.NotebookStyle;
        var path = Path.Combine(Root, "notes.json");
        if (!File.Exists(path)) return;
        try
        {
            Notes = JsonSerializer.Deserialize<List<NoteData>>(File.ReadAllText(path)) ?? new();
            foreach (var note in Notes.Where(n => n.Created == null))
            {
                var content = ContentPath(note);
                note.Created = File.Exists(content) ? new DateTimeOffset(File.GetCreationTime(content)) : DateTimeOffset.Now;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            System.Windows.MessageBox.Show("The notes index could not be read. Existing files will be preserved.\n\n" + ex.Message, "note.");
            File.Copy(path, Path.Combine(Root, "notes-unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".json"));
        }
    }
    public void ResetPreferences(AppPreferences? restore = null)
    {
        Preferences = restore ?? new AppPreferences { DarkDefaultsApplied = true, InstalledSounds = Preferences.InstalledSounds };
        Preferences.NotebookTabs ??= Preferences.NotebookStyle;
        Preferences.Commands = CommandShortcut.Normalize(Preferences.Commands);
    }
    public string PreferencesJson() => JsonSerializer.Serialize(Preferences);
    public static AppPreferences? ParsePreferences(string json) => JsonSerializer.Deserialize<AppPreferences>(json);
    public void Save()
    {
        Directory.CreateDirectory(Root);
        AtomicWrite(Path.Combine(Root, "preferences.json"), p => File.WriteAllText(p, JsonSerializer.Serialize(Preferences)));
        AtomicWrite(Path.Combine(Root, "notes.json"), p => File.WriteAllText(p, JsonSerializer.Serialize(Notes, new JsonSerializerOptions { WriteIndented = true })));
    }
    public static void AtomicWrite(string path, Action<string> write)
    {
        var temp = path + ".tmp"; write(temp);
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
    }
    private static void MigrateLegacyFolder()
    {
        try
        {
            if (HasData(DefaultRoot) || File.Exists(LocationFile)) return;
            if (File.Exists(LegacyLocationFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LocationFile)!);
                File.Copy(LegacyLocationFile, LocationFile);
            }
            if (!HasData(LegacyRoot)) return;
            foreach (var file in Directory.GetFiles(LegacyRoot, "*", SearchOption.AllDirectories).OrderBy(f => Path.GetFileName(f).Equals("notes.json", StringComparison.OrdinalIgnoreCase)))
            {
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(DefaultRoot, Path.GetRelativePath(LegacyRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private static string? SavedLocation()
    {
        try
        {
            var path = File.Exists(LocationFile) ? File.ReadAllText(LocationFile).Trim() : "";
            return Path.IsPathFullyQualified(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    private IEnumerable<string> DataFiles() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && !string.Equals(f, LocationFile, StringComparison.OrdinalIgnoreCase));
    public static bool HasData(string folder) => File.Exists(Path.Combine(folder, "notes.json"));
    public static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    public static string BackupName() => "note-backup-" + DateTime.Now.ToString(BackupStamp) + ".zip";
    public void Backup(string zipPath)
    {
        var files = DataFiles().Where(f => !SameFolder(f, zipPath)).ToArray();
        var temp = zipPath + ".tmp";
        using (var archive = System.IO.Compression.ZipFile.Open(temp, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(Path.GetRelativePath(Root, file).Replace('\\', '/'));
                entry.LastWriteTime = File.GetLastWriteTime(file);
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var target = entry.Open();
                source.CopyTo(target);
            }
        File.Move(temp, zipPath, overwrite: true);
    }
    public void MoveTo(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (SameFolder(folder, Root)) return;
        if (folder.StartsWith(Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a folder outside the current notes folder.");
        Directory.CreateDirectory(folder);
        foreach (var file in DataFiles().ToArray())
        {
            var target = Path.Combine(folder, Path.GetRelativePath(Root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(LocationFile)!);
        if (SameFolder(folder, DefaultRoot)) File.Delete(LocationFile);
        else AtomicWrite(LocationFile, p => File.WriteAllText(p, folder));
        Root = folder;
    }
    public void DeleteContent(NoteData note)
    {
        foreach (var path in new[] { ContentPath(note), ContentPath(note) + ".bak" })
            try { File.Delete(path); } catch (IOException) { }
    }
}
