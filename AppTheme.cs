using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace StickyNotes;
internal static class AppTheme
{
    public static Brush Muted => App.CurrentApp.Store.Preferences.DarkMode ? Brushes.LightGray : Brushes.DimGray;
    public static void Apply(Window window)
    {
        ApplyIcon(window);
        ApplyResources(window);
        window.Background = (Brush)window.Resources["Page"];
        window.Foreground = (Brush)window.Resources["Ink"];
        window.SourceInitialized += (_, _) => ApplyNativeTheme(window); ApplyNativeTheme(window);
    }
    public static void ApplyNativeTheme(Window window)
    {
        int dark = App.CurrentApp.Store.Preferences.DarkMode ? 1 : 0; var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    public static void ApplyIcon(Window window) => window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
        new Uri("pack://application:,,,/note.app;component/NoteappFinal.ico"));
    public static void ApplyResources(FrameworkElement element)
    {
        element.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/note.app;component/LibraryTheme.xaml", UriKind.Relative) });
        ApplyBrushes(element);
    }
    public static void ApplyBrushes(FrameworkElement element)
    {
        bool dark = App.CurrentApp.Store.Preferences.DarkMode;
        Brush B(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
        element.Resources["Ink"] = B(dark ? "#F5F5F5" : "#24251F");
        element.Resources["ScrollInk"] = B(dark ? "#AFB5BE" : "#524F48");
        element.Resources["Muted"] = B(dark ? "#A3A9B2" : "#65685F");
        element.Resources["Surface"] = B(dark ? "#171717" : "#FFFFFF");
        element.Resources["Line"] = B(dark ? "#383838" : "#DBDCD3");
        element.Resources["Hover"] = B(dark ? "#292929" : "#E9EDE4");
        element.Resources["Page"] = B(dark ? "#0C0C0C" : "#F8F7F1");
        element.Resources["Danger"] = B(dark ? "#FFA9A9" : "#AD3030");
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
