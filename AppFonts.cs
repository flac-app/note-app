using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
namespace StickyNotes;
internal static class AppFonts
{
    private static readonly Uri Bundle = new("pack://application:,,,/note.app;component/");
    private static FontFamily? brand, sans;
    public static FontFamily Brand => brand ??= new FontFamily(Bundle, "./#Tomorrow");
    public const string Sans = "42dot Sans";
    public const string SansLabel = "Asta Sans";
    private const string BundledSansSource = "./Fonts/#42dot Sans";
    public static FontFamily Get(string name) => name == Sans ? sans ??= new FontFamily(Bundle, BundledSansSource) : new FontFamily(name);
    public static string[] Choices() => Fonts.SystemFontFamilies.Select(f => Label(f.Source)).Append(SansLabel).Distinct().OrderBy(f => f).ToArray();
    public static string Label(string name) => name == Sans ? SansLabel : name;
    public static string Name(string label) => label == SansLabel ? Sans : label;
    public static void RestoreBundled(FlowDocument document)
    {
        for (var p = document.ContentStart; p != null && p.CompareTo(document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is TextElement element
                && element.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily { Source: BundledSansSource or Sans })
                element.FontFamily = Get(Sans);
    }
}
