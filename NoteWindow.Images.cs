using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace StickyNotes;
public sealed partial class NoteWindow
{
    private Image? selectedImage;
    private ImageResizeAdorner? imageAdorner;
    private AdornerLayer? imageLayer;
    private ImageResizeAdorner? lockAdorner;
    private Image? lockHoverImage;
    private readonly Stack<(Image Image, double Width)?> undoHistory = new(), redoHistory = new();
    private bool replayingText;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp" };
    private void ConfigureImageEditing()
    {
        editor.AllowDrop = true;
        editor.PreviewDragEnter += OnImageDragOver;
        editor.PreviewDragOver += OnImageDragOver;
        editor.PreviewDrop += OnImageDrop;
        editor.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
            if (imageAdorner != null && e.OriginalSource is Visual source && imageAdorner.IsAncestorOf(source)) return;
            var image = AncestorImage(e.OriginalSource as DependencyObject);
            if (image != null && IsImageLocked(image)) { SelectImage(null); return; }
            SelectImage(image);
            if (image != null) { editor.Focus(); e.Handled = true; }
        };
        editor.PreviewMouseMove += (_, e) => UpdateLockHover(e);
        editor.MouseLeave += (_, _) => ShowLockHover(null);
        editor.PreviewMouseRightButtonDown += (_, e) =>
        {
            var image = AncestorImage(e.OriginalSource as DependencyObject);
            if (image == null || !contentReadable) return;
            bool locked = IsImageLocked(image);
            if (!locked) SelectImage(image);
            var items = new List<(string, Action)> { ("Fit image to note", () => { RememberImageWidth(image); ResizeImage(image, ImageWidthLimit()); }), ("Original image size", () => { RememberImageWidth(image); ResizeImage(image, image.Source?.Width ?? 100); }) };
            items.Add((locked ? "Unlock image size" : "Lock image size", () => SetImageLocked(image, !locked)));
            if (image.Parent is InlineUIContainer inline)
                foreach (var (label, alignment) in new[] { ("top", BaselineAlignment.Top), ("middle", BaselineAlignment.Center), ("bottom", BaselineAlignment.Baseline) })
                    items.Add(($"Text beside image: {label}{(inline.BaselineAlignment == alignment ? "  ✓" : "")}", () => AlignTextBesideImage(inline, alignment)));
            ShowEditorMenu(image, items.ToArray());
            e.Handled = true;
        };
        editor.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) SelectImage(null); };
        editor.TextChanged += (_, e) =>
        {
            if (e.UndoAction == UndoAction.Create && !replayingText) { undoHistory.Push(null); redoHistory.Clear(); }
            else if (e.UndoAction == UndoAction.Clear) { undoHistory.Clear(); redoHistory.Clear(); }
            if (selectedImage != null && !DocumentImages().Contains(selectedImage)) SelectImage(null);
        };
        CommandManager.AddPreviewExecutedHandler(editor, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Undo) { e.Handled = true; Replay(undoHistory, redoHistory, () => editor.Undo()); }
            else if (e.Command == ApplicationCommands.Redo) { e.Handled = true; Replay(redoHistory, undoHistory, () => editor.Redo()); }
        });
        Closed += (_, _) => { SelectImage(null); ShowLockHover(null); };
    }
    private static bool IsImageLocked(Image image) => image.MinWidth == LockedImageMarker;
    private const double LockedImageMarker = 1;
    private void SetImageLocked(Image image, bool locked)
    {
        if (!contentReadable || IsImageLocked(image) == locked) return;
        if (locked) image.MinWidth = LockedImageMarker; else image.ClearValue(MinWidthProperty);
        dirty = true; QueueSave();
        if (locked)
        {
            if (selectedImage == image) SelectImage(null);
            ShowLockHover(ImageResizeAdorner.NearLock(image, Mouse.GetPosition(image)) ? image : null, animate: false);
        }
        else { ShowLockHover(null, animate: false); SelectImage(image); }
    }
    private void UpdateLockHover(MouseEventArgs e)
    {
        if (!contentReadable) return;
        if (lockHoverImage != null && ImageResizeAdorner.NearLock(lockHoverImage, e.GetPosition(lockHoverImage))) return;
        var image = AncestorImage(e.OriginalSource as DependencyObject);
        ShowLockHover(image != null && image != selectedImage && IsImageLocked(image) && ImageResizeAdorner.NearLock(image, e.GetPosition(image)) ? image : null);
    }
    private void ShowLockHover(Image? image, bool animate = true)
    {
        if (image == lockHoverImage) return;
        if (lockAdorner is { } old)
        {
            var layer = AdornerLayer.GetAdornerLayer(old.AdornedElement);
            if (animate) old.Fade(false, () => layer?.Remove(old)); else layer?.Remove(old);
        }
        lockAdorner = null; lockHoverImage = image;
        if (image == null || AdornerLayer.GetAdornerLayer(image) is not { } target) return;
        lockAdorner = ImageResizeAdorner.ForLocked(image, () => SetImageLocked(image, false));
        target.Add(lockAdorner);
        if (animate) { lockAdorner.Opacity = 0; lockAdorner.Fade(true); }
    }
    private void AlignTextBesideImage(InlineUIContainer inline, BaselineAlignment alignment)
    {
        if (!contentReadable || inline.BaselineAlignment == alignment) return;
        editor.BeginChange();
        try { inline.BaselineAlignment = alignment; }
        finally { editor.EndChange(); }
        imageAdorner?.InvalidateArrange(); dirty = true; QueueSave();
    }
    private void RememberImageWidth(Image image) { undoHistory.Push((image, image.Width)); redoHistory.Clear(); }
    private void Replay(Stack<(Image Image, double Width)?> from, Stack<(Image Image, double Width)?> to, Action textStep)
    {
        if (!from.TryPop(out var entry)) return;
        if (entry is not { } resize)
        {
            to.Push(null);
            replayingText = true;
            try { textStep(); } finally { replayingText = false; }
            return;
        }
        to.Push((resize.Image, resize.Image.Width));
        resize.Image.Width = resize.Width;
        if (resize.Image == selectedImage) imageAdorner?.InvalidateArrange();
        dirty = true; QueueSave();
    }
    private static Image? AncestorImage(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Image image) return image;
            if (element is not Visual) return null;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }
    private IEnumerable<Image> DocumentImages()
    {
        for (var p = editor.Document.ContentStart; p != null && p.CompareTo(editor.Document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.ElementStart) continue;
            if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer inline && inline.Child is Image image) yield return image;
            if (p.GetAdjacentElement(LogicalDirection.Forward) is BlockUIContainer block && block.Child is Image blockImage) yield return blockImage;
        }
    }
    private void SelectImage(Image? image)
    {
        if (imageAdorner != null) imageLayer?.Remove(imageAdorner);
        imageAdorner = null; imageLayer = null; selectedImage = image;
        if (image == null || !contentReadable) return;
        imageLayer = AdornerLayer.GetAdornerLayer(image);
        if (imageLayer == null) return;
        imageAdorner = new ImageResizeAdorner(image, () => SetImageLocked(image, true), () => RememberImageWidth(image), width => ResizeImage(image, width));
        imageLayer.Add(imageAdorner);
    }
    private double ImageWidthLimit() => Math.Max(32, editor.ActualWidth - editor.Padding.Left - editor.Padding.Right - 24);
    private void ResizeImage(Image image, double width)
    {
        if (!contentReadable) return;
        image.Width = Math.Clamp(width, 32, ImageWidthLimit());
        image.Height = double.NaN; image.Stretch = Stretch.Uniform;
        imageAdorner?.InvalidateArrange(); dirty = true; QueueSave();
    }
    private static bool HasImageDrop(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths) return paths.Length > 0;
        return data.GetDataPresent(DataFormats.Bitmap);
    }
    internal static bool IsNoteContent(IDataObject data) => data.GetDataPresent(DataFormats.XamlPackage);
    private void OnImageDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent(DataFormats.Bitmap) || IsNoteContent(e.Data)) return;
        e.Effects = contentReadable && HasImageDrop(e.Data) && (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnImageDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent(DataFormats.Bitmap) || IsNoteContent(e.Data)) return;
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (!contentReadable || !HasImageDrop(e.Data) || (e.AllowedEffects & DragDropEffects.Copy) == 0) return;
        if (docked) DetachFromEdge();
        var position = editor.GetPositionFromPoint(e.GetPosition(editor), true) ?? editor.Document.ContentEnd;
        if (ImportDroppedImages(e.Data, position) > 0) e.Effects = DragDropEffects.Copy;
    }
    private int ImportDroppedImages(IDataObject data, TextPointer position)
    {
        if (!contentReadable) return 0;
        var sources = new List<BitmapSource>();
        var failures = new List<string>();
        var links = new List<string>();
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                if (!ImageExtensions.Contains(Path.GetExtension(path)) || Directory.Exists(path)) { links.Add(path); continue; }
                try { sources.Add(ReadImage(path)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException)
                { failures.Add(Path.GetFileName(path)); }
            }
        }
        else if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap) sources.Add(bitmap);
        if (sources.Count > 0)
        {
            editor.Selection.Select(position, position);
            editor.BeginChange();
            try { foreach (var source in sources) InsertImage(source); }
            finally { editor.EndChange(); }
            editor.UpdateLayout(); SelectImage(DocumentImages().LastOrDefault(image => ReferenceEquals(image.Source, sources[^1])));
        }
        if (links.Count > 0)
        {
            if (sources.Count == 0) editor.Selection.Select(position, position);
            InsertFileLinks(links);
        }
        if (failures.Count > 0) { status.Text = "Could not read: " + string.Join(", ", failures); status.ToolTip = status.Text; }
        return sources.Count + links.Count;
    }
    internal void InsertFileLinks(IEnumerable<string> paths)
    {
        if (!contentReadable) return;
        editor.BeginChange();
        try
        {
            foreach (var path in paths)
            {
                if (!Uri.TryCreate(path, UriKind.Absolute, out var uri)) continue;
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var at = editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
                var link = new Hyperlink(new Run(string.IsNullOrEmpty(name) ? path : name), at) { NavigateUri = uri };
                ConfigureLink(link);
                var space = new Run(" ", link.ElementEnd);
                editor.CaretPosition = space.ContentEnd;
            }
        }
        finally { editor.EndChange(); }
        dirty = true; QueueSave();
    }
}
