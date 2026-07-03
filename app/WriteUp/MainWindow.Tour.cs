using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace WriteUp;

/// <summary>
/// The in-app guided tour: dims the window, cuts a spotlight hole over one UI
/// element at a time, and shows an explainer card next to it. Runs on first
/// launch and can be replayed (or disabled) from ⚙ Settings.
/// </summary>
public partial class MainWindow
{
    private sealed record TourStop(Func<FrameworkElement> Target, string Title, string Body);

    private List<TourStop>? _tourStops;
    private int _tourIndex = -1;

    private List<TourStop> BuildTourStops() => new()
    {
        new(() => RecordBtn, "Start here",
            "Press this (or Ctrl+Alt+R from anywhere, even inside CAD) to start recording. " +
            "Every click captures a screenshot with a marker at the exact spot; typing, keys, " +
            "scrolls and pans become steps too. Press it again to stop."),
        new(() => DetailsCard, "Document details",
            "Title, subtitle, author, department, revision — this becomes the title block of the " +
            "exported write-up. Your entries are remembered as defaults for next time."),
        new(() => ControlBar, "While recording",
            "Watch the elapsed time and step count here. \"Add note\" inserts a free-text step, " +
            "and \"📂 Open…\" reloads a previously saved session so you can keep editing where " +
            "you left off."),
        new(() => StepsCard, "Review and clean up",
            "Every action lands here. Rewrite captions, rename a section label once to rename the " +
            "whole visit, toggle the zoom inset, reorder with ▲▼, delete noise with ✕, and use ✎ " +
            "to annotate — arrows, boxes, callouts, or blur/redact anything sensitive."),
        new(() => PreviewCard, "Live preview",
            "This pane shows exactly what the exported write-up will look like. Click any step " +
            "card on the left to jump to it here."),
        new(() => ExportBar, "Share it",
            "One click to a polished PDF, Word-compatible RTF, or a self-contained HTML page with " +
            "images embedded."),
        new(() => HeaderToggles, "Window & settings",
            "Keep the app floating with Always on top, or hide it entirely while recording with " +
            "Compact mode — a small Stop bar appears instead. ⚙ Settings has the session-cleanup " +
            "and screenshot options, and can replay this tour. That's everything — happy recording!")
    };

    private void StartTour()
    {
        _tourStops = BuildTourStops();
        _tourIndex = 0;
        TourOverlay.Visibility = Visibility.Visible;
        SizeChanged += Tour_OnWindowSizeChanged;
        ShowTourStep();
    }

    private void EndTour()
    {
        SizeChanged -= Tour_OnWindowSizeChanged;
        TourOverlay.Visibility = Visibility.Collapsed;
        _tourStops = null;
        _tourIndex = -1;

        // Seen it (or skipped it) — don't auto-run again. Settings can re-enable.
        _settings.ShowGuidedTour = false;
        PersistSettings();
    }

    private void Tour_OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_tourIndex >= 0)
            Dispatcher.BeginInvoke(ShowTourStep, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void TourNext_Click(object sender, RoutedEventArgs e)
    {
        if (_tourStops == null) return;
        if (_tourIndex >= _tourStops.Count - 1) { EndTour(); return; }
        _tourIndex++;
        ShowTourStep();
    }

    private void TourBack_Click(object sender, RoutedEventArgs e)
    {
        if (_tourIndex <= 0) return;
        _tourIndex--;
        ShowTourStep();
    }

    private void TourSkip_Click(object sender, RoutedEventArgs e) => EndTour();

    private void ShowTourStep()
    {
        if (_tourStops == null || _tourIndex < 0 || _tourIndex >= _tourStops.Count) return;
        var stop = _tourStops[_tourIndex];
        var target = stop.Target();

        TourStepLabel.Text = $"STEP {_tourIndex + 1} OF {_tourStops.Count}";
        TourTitle.Text = stop.Title;
        TourBody.Text = stop.Body;
        TourBackBtn.Visibility = _tourIndex == 0 ? Visibility.Hidden : Visibility.Visible;
        TourNextBtn.Content = _tourIndex == _tourStops.Count - 1 ? "Done ✓" : "Next";

        // Spotlight hole over the target, in overlay coordinates.
        Rect hole;
        try
        {
            var topLeft = target.TransformToVisual(RootGrid).Transform(new Point(0, 0));
            hole = new Rect(topLeft, new Size(target.ActualWidth, target.ActualHeight));
            hole.Inflate(6, 6);
        }
        catch
        {
            hole = new Rect(0, 0, 0, 0); // target not realized; dim everything
        }

        double w = RootGrid.ActualWidth, h = RootGrid.ActualHeight;
        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry(new Rect(0, 0, w, h)));
        dim.Children.Add(new RectangleGeometry(hole, 10, 10));
        dim.Freeze();
        TourDim.Data = dim;

        PlaceTourCard(hole, w, h);
    }

    private void PlaceTourCard(Rect hole, double w, double h)
    {
        TourCard.Measure(new Size(w, h));
        double cw = TourCard.DesiredSize.Width;
        double ch = TourCard.DesiredSize.Height;
        const double gap = 14, edge = 12;

        double x = Math.Clamp(hole.Left, edge, Math.Max(edge, w - cw - edge));

        double y = hole.Bottom + gap;                       // prefer below
        if (y + ch > h - edge) y = hole.Top - ch - gap;     // else above
        if (y < edge)                                       // else beside
        {
            y = Math.Clamp(hole.Top, edge, Math.Max(edge, h - ch - edge));
            x = hole.Right + gap;
            if (x + cw > w - edge) x = Math.Max(edge, hole.Left - cw - gap);
        }

        TourCard.Margin = new Thickness(x, y, 0, 0);
    }
}
