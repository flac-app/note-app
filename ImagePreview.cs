using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickyNotes;
internal static class ImagePreview
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    public static ToolTip Create(Uri uri)
    {
        var panel = new StackPanel { MaxWidth = 320 };
        var caption = new TextBlock { Text = uri.OriginalString, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        panel.Children.Add(caption);
        var tip = new ToolTip { Content = panel };
        if (!Regex.IsMatch(uri.IsAbsoluteUri ? uri.AbsolutePath : "", @"\.(png|jpe?g|gif|bmp|webp)$", RegexOptions.IgnoreCase)) return tip;
        bool loaded = false;
        CancellationTokenSource? pending = null;
        tip.Closed += (_, _) => pending?.Cancel();
        tip.Opened += async (_, _) =>
        {
            if (loaded) return;
            pending?.Dispose(); pending = new CancellationTokenSource(); var token = pending.Token;
            caption.Text = "Loading image preview…";
            try
            {
                BitmapImage bitmap;
                if (uri.IsFile) bitmap = await Task.Run(() => NoteWindow.ReadImage(uri.LocalPath), token);
                else if (uri.Scheme is "https" or "http")
                {
                    using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) throw new IOException("Image is too large to preview.");
                    if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true) throw new IOException("This link does not return an image.");
                    using var input = await response.Content.ReadAsStreamAsync(token);
                    using var buffer = new MemoryStream();
                    var bytes = new byte[32768]; int read;
                    while ((read = await input.ReadAsync(bytes, token)) > 0) { if (buffer.Length + read > 8 * 1024 * 1024) throw new IOException("Image is too large to preview."); buffer.Write(bytes, 0, read); }
                    buffer.Position = 0; bitmap = NoteWindow.DecodeImage(buffer);
                }
                else return;
                token.ThrowIfCancellationRequested();
                await tip.Dispatcher.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    panel.Children.Insert(0, new Image { Source = bitmap, MaxWidth = 300, MaxHeight = 220, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8) });
                    caption.Text = uri.OriginalString; loaded = true;
                });
            }
            catch (OperationCanceledException) { await tip.Dispatcher.InvokeAsync(() => caption.Text = uri.OriginalString); }
            catch (Exception) { await tip.Dispatcher.InvokeAsync(() => caption.Text = "Preview unavailable\n" + uri.OriginalString); }
        };
        return tip;
    }
}

