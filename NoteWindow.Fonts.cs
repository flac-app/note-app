using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
namespace StickyNotes;
public sealed partial class NoteWindow
{
    private static readonly double[] FontSizes = { 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 72 };
    private Popup? fontPopup;
    private ListBox? fontList;
    private TextBox? fontSearch;
    private TextBox? fontSize;
    private string[] fontChoices = Array.Empty<string>();
    private void ConfigureFonts()
    {
        formatButtons[Key.F].ToolTip = "Font and size for the selected text";
        editor.IsInactiveSelectionHighlightEnabled = true;
    }
    private string? SelectionFont() => editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family ? FontLabel(family) : null;
    private static string FontLabel(FontFamily family) => family.Source.EndsWith("#" + AppFonts.Sans, StringComparison.Ordinal) ? AppFonts.SansLabel : AppFonts.Label(family.Source);
    private void ShowFontPicker()
    {
        if (editor.IsReadOnly) return;
        if (fontPopup == null) BuildFontPicker();
        fontChoices = AppFonts.Choices();
        fontSearch!.Text = "";
        FilterFonts();
        var current = SelectionFont();
        fontList!.SelectedItem = current;
        if (current != null) fontList.ScrollIntoView(current);
        fontSize!.Text = editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size ? size.ToString("0.#", CultureInfo.CurrentCulture) : "";
        fontPopup!.IsOpen = true;
        fontSearch.Focus();
    }
    private void BuildFontPicker()
    {
        var panel = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8), Width = 240 };
        AppTheme.ApplyResources(panel);
        panel.SetResourceReference(Border.BackgroundProperty, "Surface"); panel.SetResourceReference(Border.BorderBrushProperty, "Line");
        var stack = new StackPanel(); panel.Child = stack;
        fontSearch = new TextBox { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 6), ToolTip = "Type to find a font · Enter applies the first match" };
        fontSearch.SetResourceReference(ForegroundProperty, "Ink"); fontSearch.SetResourceReference(TextBox.CaretBrushProperty, "Ink");
        fontSearch.SetResourceReference(BackgroundProperty, "Page"); fontSearch.SetResourceReference(BorderBrushProperty, "Line");
        fontSearch.TextChanged += (_, _) => FilterFonts();
        fontSearch.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && fontList!.Items.Count > 0) { ApplyFont((string)(fontList.SelectedItem ?? fontList.Items[0])); e.Handled = true; }
            else if (e.Key == Key.Down && fontList!.Items.Count > 0) { fontList.SelectedIndex = Math.Min(fontList.SelectedIndex + 1, fontList.Items.Count - 1); fontList.ScrollIntoView(fontList.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Up && fontList!.SelectedIndex > 0) { fontList.SelectedIndex--; fontList.ScrollIntoView(fontList.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Escape) { fontPopup!.IsOpen = false; e.Handled = true; }
        };
        stack.Children.Add(fontSearch);
        var item = new FrameworkElementFactory(typeof(DockPanel));
        var sample = new FrameworkElementFactory(typeof(TextBlock));
        sample.SetValue(TextBlock.TextProperty, "Abc");
        sample.SetBinding(TextBlock.FontFamilyProperty, new Binding { Converter = new FontPreview() });
        sample.SetValue(TextBlock.FontSizeProperty, 15.0);
        sample.SetValue(TextBlock.OpacityProperty, .75);
        sample.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 2, 0));
        sample.SetValue(DockPanel.DockProperty, System.Windows.Controls.Dock.Right);
        item.AppendChild(sample);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding());
        name.SetValue(TextBlock.FontSizeProperty, 13.0);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        item.AppendChild(name);
        item.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 2, 2, 2));
        fontList = new ListBox { Height = 220, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ItemTemplate = new DataTemplate { VisualTree = item } };
        fontList.SetResourceReference(ForegroundProperty, "Ink");
        VirtualizingPanel.SetIsVirtualizing(fontList, true); ScrollViewer.SetHorizontalScrollBarVisibility(fontList, ScrollBarVisibility.Disabled);
        fontList.PreviewMouseLeftButtonUp += (_, e) => { if (ItemsControl.ContainerFromElement(fontList, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: string font }) ApplyFont(font); };
        stack.Children.Add(fontList);
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var sizeCaption = new TextBlock { Text = "Size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) };
        sizeCaption.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); sizeRow.Children.Add(sizeCaption);
        Button Step(string label, string tooltip, int direction)
        {
            var button = new Button { Content = label, ToolTip = tooltip, Width = 26, Height = 24, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(0), Focusable = false };
            button.Click += (_, _) => StepFontSize(direction);
            return button;
        }
        fontSize = new TextBox { Width = 44, Height = 24, Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(4, 0, 4, 0), TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Font size for the selected text · Enter applies" };
        fontSize.SetResourceReference(ForegroundProperty, "Ink"); fontSize.SetResourceReference(TextBox.CaretBrushProperty, "Ink");
        fontSize.SetResourceReference(BackgroundProperty, "Page"); fontSize.SetResourceReference(BorderBrushProperty, "Line");
        fontSize.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyFontSize(fontSize.Text); e.Handled = true; }
            else if (e.Key is Key.Up or Key.Down) { StepFontSize(e.Key == Key.Up ? 1 : -1); e.Handled = true; }
        };
        sizeRow.Children.Add(Step("−", "Smaller", -1)); sizeRow.Children.Add(fontSize); sizeRow.Children.Add(Step("+", "Larger", 1));
        stack.Children.Add(sizeRow);
        fontPopup = new Popup { Child = panel, StaysOpen = false, PlacementTarget = formatButtons[Key.F], Placement = PlacementMode.Top, AllowsTransparency = true };
        fontPopup.Closed += (_, _) => editor.Focus();
    }
    private void FilterFonts()
    {
        var query = fontSearch!.Text.Trim();
        var matches = query.Length == 0 ? fontChoices : fontChoices.Where(f => f.Contains(query, StringComparison.CurrentCultureIgnoreCase)).OrderBy(f => !f.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        fontList!.ItemsSource = matches;
    }
    private void ApplyFont(string label)
    {
        fontPopup!.IsOpen = false;
        if (editor.IsReadOnly) return;
        editor.Focus();
        editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, AppFonts.Get(AppFonts.Name(label)));
        StyleBoxes();
        dirty = true; QueueSave(); UpdateFormatStates();
    }
    private void StepFontSize(int direction)
    {
        double current = double.TryParse(fontSize!.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var shown) ? shown : Note.FontSize;
        double next = direction > 0 ? FontSizes.FirstOrDefault(s => s > current, FontSizes[^1]) : FontSizes.LastOrDefault(s => s < current, FontSizes[0]);
        fontSize.Text = next.ToString("0.#", CultureInfo.CurrentCulture);
        ApplyFontSize(fontSize.Text);
    }
    private void ApplyFontSize(string text)
    {
        if (editor.IsReadOnly || !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size)) return;
        size = Math.Clamp(size, 6, 96);
        if (fontSize != null) fontSize.Text = size.ToString("0.#", CultureInfo.CurrentCulture);
        editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        dirty = true; QueueSave(); UpdateFormatStates();
    }
    private sealed class FontPreview : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string label ? AppFonts.Get(AppFonts.Name(label)) : SystemFonts.MessageFontFamily;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
