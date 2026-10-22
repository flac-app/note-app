using System;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace StickyNotes;
internal static class ReminderSounds
{
    public const string DefaultSound = "Gentle Chime.wav";
    public static readonly string[] Extensions = { ".wav", ".mp3", ".wma", ".m4a", ".aac" };
    public const string FileFilter = "Sounds (*.wav;*.mp3;*.wma;*.m4a;*.aac)|*.wav;*.mp3;*.wma;*.m4a;*.aac";
    private static readonly MediaPlayer player = new();
    public static bool Looping { get; private set; }
    static ReminderSounds() => player.MediaEnded += (_, _) => { if (Looping) { player.Position = TimeSpan.Zero; player.Play(); } };
    public static string Folder => Path.Combine(App.CurrentApp.Store.Root, "sounds");
    private static string? checkedFolder;
    public static void InstallDefaults()
    {
        if (checkedFolder == Folder) return;
        var preferences = App.CurrentApp.Store.Preferences;
        try
        {
            Directory.CreateDirectory(Folder);
            var installed = preferences.InstalledSounds;
#pragma warning disable CS0612
            bool refresh = preferences.DefaultSoundsInstalled && installed.Count == 0;
            preferences.DefaultSoundsInstalled = false;
#pragma warning restore CS0612
            var assembly = typeof(ReminderSounds).Assembly;
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("sounds/", StringComparison.Ordinal)))
            {
                var name = resource["sounds/".Length..];
                var target = Path.Combine(Folder, name);
                byte[] bundled;
                using (var source = assembly.GetManifestResourceStream(resource)!) { bundled = new byte[source.Length]; source.ReadExactly(bundled); }
                var hash = Hash(bundled);
                bool write = installed.TryGetValue(name, out var offered)
                    ? offered != hash && File.Exists(target) && Hash(File.ReadAllBytes(target)) == offered
                    : refresh || !File.Exists(target);
                if (write) NoteStore.AtomicWrite(target, path => File.WriteAllBytes(path, bundled));
                installed[name] = hash;
            }
            checkedFolder = Folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private static string Hash(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
    public static string[] Available()
    {
        InstallDefaults();
        try
        {
            return Directory.Exists(Folder)
                ? Directory.EnumerateFiles(Folder).Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Select(Path.GetFileName).OfType<string>().Order(StringComparer.CurrentCultureIgnoreCase).ToArray()
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
    public static string Label(string file) => Path.GetFileNameWithoutExtension(file);
    public static string For(NoteData note) => note.ReminderSound ?? App.CurrentApp.Store.Preferences.ReminderSound;
    public static bool LoopsFor(NoteData note) => note.ReminderSoundLoops ?? App.CurrentApp.Store.Preferences.ReminderSoundLoops;
    public static void Play(string? file, bool loop = false)
    {
        Looping = false;
        if (string.IsNullOrEmpty(file)) return;
        InstallDefaults();
        var path = Path.Combine(Folder, Path.GetFileName(file));
        if (!File.Exists(path)) return;
        player.Stop(); Looping = loop; ApplyVolume(); player.Open(new Uri(path)); player.Play();
    }
    public static void ApplyVolume() => player.Volume = Math.Clamp(App.CurrentApp.Store.Preferences.ReminderVolume, 0, 100) / 100.0;
    public static double Volume => player.Volume;
    public static void Stop() { Looping = false; player.Stop(); }
    public static void StopIfAnswered() { if (Looping && !App.CurrentApp.Store.Notes.Any(n => n.ReminderActive)) Stop(); }
    public static string Add(string source)
    {
        Directory.CreateDirectory(Folder);
        if (NoteStore.SameFolder(Path.GetDirectoryName(Path.GetFullPath(source))!, Folder)) return Path.GetFileName(source);
        string name = Path.GetFileNameWithoutExtension(source), extension = Path.GetExtension(source).ToLowerInvariant();
        var target = Path.Combine(Folder, name + extension);
        for (int copy = 2; File.Exists(target); copy++) target = Path.Combine(Folder, $"{name} ({copy}){extension}");
        File.Copy(source, target);
        return Path.GetFileName(target);
    }
}
