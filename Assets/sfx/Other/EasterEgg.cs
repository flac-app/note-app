#if EASTER_EGG
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace StickyNotes;
public sealed partial class LibraryWindow
{
    private const int EggClicks = 12;
    private static readonly TimeSpan EggClickGap = TimeSpan.FromMilliseconds(700);
    private static bool eggRefused;
    private readonly MediaPlayer eggSound = new();
    private int eggStreak;
    private DateTime lastBrandClick;
    private bool eggAsking, eggAnswered;
    partial void EasterEggClick(ref bool handled)
    {
        if (eggAsking) { handled = true; return; }
        if (eggRefused) return;
        var now = DateTime.UtcNow;
        eggStreak = now - lastBrandClick <= EggClickGap ? eggStreak + 1 : 1;
        lastBrandClick = now;
        if (eggStreak < EggClicks) return;
        eggStreak = 0; handled = true; AskColorfulMode();
    }
    private void AskColorfulMode()
    {
        eggAsking = true; eggAnswered = false;
        PlayEggSound("bling.mp3");
        var prompt = new StackPanel { Orientation = Orientation.Horizontal };
        var question = new TextBlock { Text = "Colorful mode?", FontSize = 12, Margin = new Thickness(0, 0, 8, 0) };
        question.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var slash = new TextBlock { Text = "/", FontSize = 12, Margin = new Thickness(5, 0, 5, 0) };
        slash.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        prompt.Children.Add(question);
        prompt.Children.Add(EggChoice("Yes", () => { PlayEggSound("I like you.mp3"); App.CurrentApp.AddColorfulNotes(); }));
        prompt.Children.Add(slash);
        prompt.Children.Add(EggChoice("No", () => { PlayEggSound("why.mp3"); eggRefused = true; }));
        var slide = (TranslateTransform)creditHost.RenderTransform;
        slide.BeginAnimation(TranslateTransform.YProperty, null); slide.Y = -CreditHeight;
        creditHost.BeginAnimation(OpacityProperty, null); creditHost.Opacity = 0;
        creditHost.Child = prompt;
        SetCreditOpen(true);
    }
    private TextBlock EggChoice(string text, Action chosen)
    {
        var choice = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        choice.MouseEnter += (_, _) => choice.TextDecorations = TextDecorations.Underline;
        choice.MouseLeave += (_, _) => choice.TextDecorations = null;
        choice.MouseLeftButtonUp += (_, _) =>
        {
            if (eggAnswered) return;
            eggAnswered = true; chosen();
            SetCreditOpen(false, () => { creditHost.Child = creditLink; eggAsking = false; });
        };
        return choice;
    }
    private void PlayEggSound(string file)
    {
        try
        {
            using var sound = typeof(LibraryWindow).Assembly.GetManifestResourceStream("egg/" + file);
            if (sound == null) return;
            var folder = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(NoteStore.DefaultRoot)!, ".egg"));
            if (!folder.Exists) { folder.Create(); folder.Attributes |= FileAttributes.Hidden; }
            var path = Path.Combine(folder.FullName, file);
            if (!File.Exists(path) || new FileInfo(path).Length != sound.Length)
                using (var target = File.Create(path)) sound.CopyTo(target);
            eggSound.Open(new Uri(path)); eggSound.Play();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
public partial class App
{
    public void AddColorfulNotes()
    {
        var separators = new[] { ("Blue", "#5AA8BC", "★"), ("Red", "#E0674A", "▲"), ("Gray", "#B4B2AC", "▦"), ("Yellow", "#E3AE1E", "◎"), ("Purple", "#B160D2", "⛤"), ("Green", "#9CC63E", "✚") };
        for (int i = 0; i < separators.Length; i++)
        {
            var (name, color, icon) = separators[i];
            var note = new NoteData
            {
                Title = name, Color = i % 2 == 0 ? "#3B3026" : "#A89070", Width = 520, Height = 430, X = 300, Y = 200,
                Edge = "Right", EdgeTabY = 180 + i * 5, EdgeTabColor = color, EdgeTabIcon = icon,
                Paper = "Notebook", TabStyle = "Notebook", Created = DateTimeOffset.Now, LastOpened = DateTimeOffset.Now,
            };
            Store.Notes.Add(note);
            var window = CreateWindow(note); window.Show();
            if (name == "Blue") window.SetText("The Lash™ sends his regards.");
            window.Dock();
        }
        Save();
    }
}
public sealed partial class NoteWindow
{
    internal void SetText(string text) { editor.Document.Blocks.Clear(); editor.AppendText(text); dirty = true; }
}
#endif
