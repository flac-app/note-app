using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace StickyNotes;
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShortcutAction { NewNote, ShowLibrary, ShowAllNotes, HideAllNotes }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShortcutTrigger { Press, DoublePress, Hold }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppCommand { NewNote, Search, AddLink, Bold, Italic, Underline, Strikethrough, Bullets }
public interface IKeyCombo
{
    ModifierKeys Modifiers { get; set; }
    Key Key { get; set; }
    string Name { get; }
}
public sealed class ShortcutBinding : IKeyCombo
{
    public ShortcutAction Action { get; set; }
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }
    public ShortcutTrigger Trigger { get; set; }
    [JsonIgnore] public string Name => Label(Action);
    public static List<ShortcutBinding> Defaults() => new() { new() { Action = ShortcutAction.NewNote, Modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key = Key.N } };
    public static string Label(ShortcutAction action) => action switch
    {
        ShortcutAction.NewNote => "New note",
        ShortcutAction.ShowLibrary => "Open library",
        ShortcutAction.ShowAllNotes => "Show all notes",
        _ => "Hide all notes"
    };
    public static string Label(ShortcutTrigger trigger) => trigger switch { ShortcutTrigger.DoublePress => "Double press", ShortcutTrigger.Hold => "Hold", _ => "Press" };
    public string Keys() => Format(Modifiers, Key);
    public static string Format(ModifierKeys modifiers, Key key)
    {
        if (key == Key.None) return "Not set";
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString());
        return string.Join(" + ", parts);
    }
    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
    public static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;
}
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint NoRepeat = 0x4000;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private readonly HwndSource source;
    private readonly Action<ShortcutAction> run;
    private readonly Dictionary<int, ShortcutBinding> registered = new();
    private readonly Dictionary<int, long> lastPress = new();
    private readonly DispatcherTimer holdTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private int holdingId; private long holdStart;
    private AppPreferences preferences = new();
    public HashSet<ShortcutBinding> Failed { get; } = new();
    public GlobalHotkeys(Action<ShortcutAction> run)
    {
        this.run = run;
        source = new HwndSource(new HwndSourceParameters("note.hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        source.AddHook(Hook);
        holdTimer.Tick += (_, _) => PollHold();
    }
    public void Apply(AppPreferences prefs)
    {
        preferences = prefs; Suspend(); Failed.Clear();
        int id = 1;
        foreach (var binding in prefs.Shortcuts.Where(b => b.Key != Key.None))
        {
            uint key = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);
            if (RegisterHotKey(source.Handle, id, (uint)binding.Modifiers | NoRepeat, key)) registered[id++] = binding;
            else Failed.Add(binding);
        }
    }
    public void Suspend()
    {
        foreach (var id in registered.Keys) UnregisterHotKey(source.Handle, id);
        registered.Clear(); lastPress.Clear(); holdTimer.Stop(); holdingId = 0;
    }
    private IntPtr Hook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey || !registered.TryGetValue(wParam.ToInt32(), out var binding)) return IntPtr.Zero;
        handled = true;
        int id = wParam.ToInt32(); long now = Environment.TickCount64;
        switch (binding.Trigger)
        {
            case ShortcutTrigger.Press: run(binding.Action); break;
            case ShortcutTrigger.DoublePress:
                if (lastPress.TryGetValue(id, out var last) && now - last <= Math.Clamp(preferences.DoublePressMilliseconds, 100, 1000)) { lastPress.Remove(id); run(binding.Action); }
                else lastPress[id] = now;
                break;
            case ShortcutTrigger.Hold: holdingId = id; holdStart = now; holdTimer.Start(); break;
        }
        return IntPtr.Zero;
    }
    private void PollHold()
    {
        if (!registered.TryGetValue(holdingId, out var binding)) { holdTimer.Stop(); return; }
        bool down = (GetAsyncKeyState(KeyInterop.VirtualKeyFromKey(binding.Key)) & 0x8000) != 0;
        if (!down) { holdTimer.Stop(); holdingId = 0; return; }
        if (Environment.TickCount64 - holdStart < Math.Clamp(preferences.HoldMilliseconds, 150, 3000)) return;
        holdTimer.Stop(); holdingId = 0; run(binding.Action);
    }
    public void Dispose() { Suspend(); source.RemoveHook(Hook); source.Dispose(); }
}

public sealed class CommandShortcut : IKeyCombo
{
    public AppCommand Command { get; set; }
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }
    [JsonIgnore] public string Name => Label(Command);
    public string Keys() => ShortcutBinding.Format(Modifiers, Key);
    private static readonly (AppCommand Command, Key Key)[] Builtin =
    {
        (AppCommand.NewNote, Key.N), (AppCommand.Search, Key.F), (AppCommand.AddLink, Key.K), (AppCommand.Bold, Key.B),
        (AppCommand.Italic, Key.I), (AppCommand.Underline, Key.U), (AppCommand.Strikethrough, Key.S), (AppCommand.Bullets, Key.P)
    };
    public static List<CommandShortcut> Defaults() => Builtin.Select(b => new CommandShortcut { Command = b.Command, Modifiers = ModifierKeys.Control, Key = b.Key }).ToList();
    public static List<CommandShortcut> Normalize(List<CommandShortcut>? saved)
    {
        var result = new List<CommandShortcut>();
        foreach (var fallback in Defaults())
            result.Add(saved?.FirstOrDefault(c => c.Command == fallback.Command) ?? fallback);
        return result;
    }
    public static string Label(AppCommand command) => command switch
    {
        AppCommand.NewNote => "New note",
        AppCommand.Search => "Search notes",
        AppCommand.AddLink => "Add or edit a link",
        AppCommand.Bold => "Bold",
        AppCommand.Italic => "Italic",
        AppCommand.Underline => "Underline",
        AppCommand.Strikethrough => "Strikethrough",
        _ => "Bullet points"
    };
    public static AppCommand? Match(AppPreferences preferences, ModifierKeys modifiers, Key key) =>
        key == Key.None ? null : preferences.Commands.FirstOrDefault(c => c.Key == key && c.Modifiers == modifiers)?.Command;
    public static string KeysFor(AppPreferences preferences, AppCommand command) =>
        preferences.Commands.FirstOrDefault(c => c.Command == command)?.Keys() ?? "Not set";
}
