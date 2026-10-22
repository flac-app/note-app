using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
namespace StickyNotes;
internal static class StartupSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "note.app";
    private const string LegacyValueName = "LittleLeafStickyNotes";
    private const string ExeName = "note.app.exe";
    public static bool IsEnabled() => RegisteredPath() is { } path && SamePath(path, Environment.ProcessPath);
    public static string? RegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key == null) return null;
        foreach (var name in new[] { ValueName, LegacyValueName }.Concat(key.GetValueNames()))
            if (key.GetValue(name) is string command && ExecutableOf(command) is { } path && (name is ValueName or LegacyValueName || IsNote(path))) return path;
        return null;
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true);
        foreach (var name in key.GetValueNames())
            if (name is ValueName or LegacyValueName || key.GetValue(name) is string command && ExecutableOf(command) is { } path && IsNote(path))
            {
                key.DeleteValue(name, false);
                approved?.DeleteValue(name, false);
            }
        if (!enabled) return;
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The app's executable path is unavailable.");
        key.SetValue(ValueName, "\"" + executable + "\"");
    }
    private static bool IsNote(string path) => string.Equals(Path.GetFileName(path), ExeName, StringComparison.OrdinalIgnoreCase);
    private static bool SamePath(string a, string? b)
    {
        if (b == null) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    internal static string? ExecutableOf(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"')) { int end = command.IndexOf('"', 1); return end > 1 ? command[1..end] : null; }
        int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? command[..(exe + 4)] : null;
    }
}
