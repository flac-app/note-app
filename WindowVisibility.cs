using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StickyNotes;

internal static class WindowVisibility
{
    internal static void SetHiddenFromSwitcher(Window window, bool hidden)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, -20);
        int next = hidden ? (style | 0x80) & ~0x40000 : (style & ~0x80) | 0x40000;
        if (next == style) return;
        SetWindowLong(handle, -20, next);
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20);
    }
    internal static void SetNoActivate(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x80);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
