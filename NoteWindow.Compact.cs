using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StickyNotes;

public sealed partial class NoteWindow
{
    private readonly Border compactCard = new() { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Visibility = Visibility.Collapsed, ClipToBounds = true };
    private readonly TextBlock compactTitle = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 10) };
    private FrameworkElement expandedView = null!;
    private Grid compactLayers = null!;
    private readonly Border animationPaper = new() { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private bool compact, compactAnimating;
    private int compactVersion;
    private long compactHoldUntil;
    private Storyboard? compactStoryboard;
    private Size compactFullSize, compactSmallSize;

    private void ConfigureCompactView()
    {
        expandedView = (FrameworkElement)Content; Content = null;
        compactLayers = new Grid { ClipToBounds = true };
        compactLayers.Children.Add(animationPaper); compactLayers.Children.Add(expandedView); compactLayers.Children.Add(compactCard); Content = compactLayers;
        var skeleton = new StackPanel(); skeleton.Children.Add(compactTitle);
        foreach (double fraction in new[] { 1d, .82, .57 })
        {
            var track = new Grid { Height = 7, Margin = new Thickness(0, 0, 0, 7) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Foreground, Opacity = .18 });
            skeleton.Children.Add(track);
        }
        compactCard.Child = skeleton;
        compactCard.ToolTip = "Hover to expand · click to keep open temporarily";
        Loaded += (_, _) => RefreshCompactMode();
        title.TextChanged += (_, _) => compactTitle.Text = title.Text;
        PreviewMouseDown += (_, _) => HoldCompactOpen(Environment.TickCount64);
        Closed += (_, _) => { compactVersion++; StopCompactAnimation(); };
    }

    private void RefreshCompactAppearance()
    {
        compactCard.Background = frame.Background; compactCard.BorderBrush = frame.BorderBrush;
        compactCard.CornerRadius = animationPaper.CornerRadius = frame.CornerRadius;
        animationPaper.Background = frame.Background; animationPaper.BorderBrush = frame.BorderBrush;
        compactTitle.Foreground = Foreground; compactTitle.Text = Note.Title;
        foreach (var bar in ((StackPanel)compactCard.Child).Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Border>())) bar.Background = Foreground;
    }

    internal void RefreshCompactMode()
    {
        if (!Note.ShrinkUnlessHovered || docked)
        {
            compactHoldUntil = 0;
            SetCompact(false, false);
        }
        else UpdateCompactState(PointerInside(), Environment.TickCount64);
    }

    private void HoldCompactOpen(long now)
    {
        if (!Note.ShrinkUnlessHovered || docked) return;
        compactHoldUntil = now + Math.Clamp(Note.CompactClickHoldSeconds, 0, 60) * 1000L;
        SetCompact(false, true);
    }

    private void UpdateCompactState(bool hovered, long now)
    {
        if (!Note.ShrinkUnlessHovered || docked || !IsVisible) return;
        bool interacting = Note.ReminderActive || noteMenu.IsOpen || editor.ContextMenu?.IsOpen == true
            || OwnedWindows.Cast<Window>().Any(w => w.IsVisible)
            || (Mouse.Captured is DependencyObject captured && IsAncestorOf(captured));
        SetCompact(!hovered && !interacting && now >= compactHoldUntil, true);
    }

    private void SetCompact(bool shrink, bool animate)
    {
        if (compact == shrink && (animate || !compactAnimating)) return;
        var from = VisibleNoteSize();
        double expandedOpacity = expandedView.IsVisible ? expandedView.Opacity : 0;
        double cardOpacity = compactCard.IsVisible ? compactCard.Opacity : 0;
        int version = ++compactVersion;
        StopCompactAnimation();
        compact = shrink; compactAnimating = true;
        compactFullSize = new Size(Math.Clamp(Note.Width, 120, 1600), Math.Clamp(Note.Height, 100, 1400));
        compactSmallSize = new Size(Math.Min(compactFullSize.Width, Math.Clamp(compactFullSize.Width * .58, 160, 240)), 100);
        if (!animate || !IsVisible) { FinishCompactAnimation(); return; }
        var target = shrink ? compactSmallSize : compactFullSize;
        double scale = Math.Min(1, Math.Min(compactFullSize.Width / 290, compactFullSize.Height / 240));
        frame.Width = compactFullSize.Width / scale; frame.Height = compactFullSize.Height / scale;
        expandedView.Width = compactFullSize.Width; expandedView.Height = compactFullSize.Height;
        compactCard.Width = compactSmallSize.Width; compactCard.Height = compactSmallSize.Height;
        expandedView.HorizontalAlignment = compactCard.HorizontalAlignment = HorizontalAlignment.Left;
        expandedView.VerticalAlignment = compactCard.VerticalAlignment = VerticalAlignment.Top;
        animationPaper.Width = from.Width; animationPaper.Height = from.Height;
        animationPaper.Visibility = Visibility.Visible;
        double radius = frame.CornerRadius.TopLeft;
        compactLayers.Clip = new RectangleGeometry(new Rect(from), radius, radius);
        compactCard.BorderBrush = Brushes.Transparent;
        expandedView.Opacity = expandedOpacity; compactCard.Opacity = cardOpacity;
        expandedView.Visibility = compactCard.Visibility = Visibility.Visible;
        expandedView.IsHitTestVisible = !shrink;
        compactCard.IsHitTestVisible = shrink;
        Width = compactFullSize.Width; Height = compactFullSize.Height;
        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(Note.CompactAnimationMilliseconds, 150, 1500));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        void Add(Timeline animation, DependencyObject element, PropertyPath path) { Storyboard.SetTarget(animation, element); Storyboard.SetTargetProperty(animation, path); storyboard.Children.Add(animation); }
        Add(new DoubleAnimation(from.Width, target.Width, duration) { EasingFunction = ease }, animationPaper, new PropertyPath(WidthProperty));
        Add(new DoubleAnimation(from.Height, target.Height, duration) { EasingFunction = ease }, animationPaper, new PropertyPath(HeightProperty));
        Add(new RectAnimation(new Rect(from), new Rect(target), duration) { EasingFunction = ease }, compactLayers, new PropertyPath("(0).(1)", ClipProperty, RectangleGeometry.RectProperty));
        void Fade(FrameworkElement element, double start, double end, double begin, double finish)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromTimeSpan(duration * begin)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(end, KeyTime.FromTimeSpan(duration * finish), new SineEase { EasingMode = EasingMode.EaseInOut }));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(end, KeyTime.FromTimeSpan(duration)));
            Add(animation, element, new PropertyPath(OpacityProperty));
        }
        var outgoing = shrink ? (FrameworkElement)expandedView : compactCard;
        var incoming = shrink ? (FrameworkElement)compactCard : expandedView;
        Fade(outgoing, shrink ? expandedOpacity : cardOpacity, 0, 0, .35);
        Fade(incoming, shrink ? cardOpacity : expandedOpacity, 1, .6, 1);
        storyboard.Completed += (_, _) => { if (version == compactVersion) FinishCompactAnimation(); };
        compactStoryboard = storyboard;
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    private Size VisibleNoteSize() => compactAnimating
        ? new Size(animationPaper.Width, animationPaper.Height)
        : new Size(Width, Height);

    private void StopCompactAnimation()
    {
        var storyboard = compactStoryboard; compactStoryboard = null;
        storyboard?.Remove(this);
    }

    private void FinishCompactAnimation()
    {
        var target = compact ? compactSmallSize : compactFullSize;
        expandedView.Opacity = compactCard.Opacity = 1;
        expandedView.Visibility = compact ? Visibility.Hidden : Visibility.Visible;
        compactCard.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        expandedView.IsHitTestVisible = !compact; compactCard.IsHitTestVisible = compact;
        compactCard.BorderBrush = frame.BorderBrush;
        animationPaper.Width = target.Width; animationPaper.Height = target.Height;
        StopCompactAnimation();
        compactLayers.Clip = null; animationPaper.Visibility = Visibility.Collapsed;
        Width = target.Width; Height = target.Height;
        expandedView.Width = compact ? compactFullSize.Width : double.NaN;
        expandedView.Height = compact ? compactFullSize.Height : double.NaN;
        compactCard.Width = compactCard.Height = double.NaN;
        expandedView.HorizontalAlignment = compactCard.HorizontalAlignment = HorizontalAlignment.Stretch;
        expandedView.VerticalAlignment = compactCard.VerticalAlignment = VerticalAlignment.Stretch;
        compactAnimating = false;
        UpdateScale(Width, Height);
    }

    private void ResetCompactView()
    {
        compactVersion++; compactHoldUntil = 0; compactAnimating = true;
        expandedView.Visibility = Visibility.Visible; compactCard.Visibility = Visibility.Collapsed;
        expandedView.Opacity = compactCard.Opacity = 1; expandedView.IsHitTestVisible = true;
        compactCard.BorderBrush = frame.BorderBrush;
        StopCompactAnimation();
        compactLayers.Clip = null; animationPaper.Visibility = Visibility.Collapsed;
        compact = false; Width = Math.Clamp(Note.Width, 120, 1600); Height = Math.Clamp(Note.Height, 100, 1400);
        expandedView.Width = expandedView.Height = compactCard.Width = compactCard.Height = double.NaN;
        expandedView.HorizontalAlignment = compactCard.HorizontalAlignment = HorizontalAlignment.Stretch;
        expandedView.VerticalAlignment = compactCard.VerticalAlignment = VerticalAlignment.Stretch;
        ResizeMode = ResizeMode.CanResize; compactAnimating = false;
    }
}
