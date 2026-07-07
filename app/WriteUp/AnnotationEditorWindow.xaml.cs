using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WriteUp.Models;
using WriteUp.Services;

// Disambiguate from the WinForms/System.Drawing global usings
// injected by <UseWindowsForms>true</UseWindowsForms>.
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Rectangle = System.Windows.Shapes.Rectangle;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WriteUp;

/// <summary>
/// Post-recording image editor: draw arrows, boxes and text callouts, or mark
/// regions to blur/redact. The canvas lives inside a uniform Viewbox, so mouse
/// positions on it are already in image-pixel coordinates at any window size.
/// Nothing touches disk until Save (except the explicit Reset).
/// </summary>
public partial class AnnotationEditorWindow : Window
{
    private enum DragMode { None, Drawing, Moving, Handle }

    private readonly Step? _step;
    private readonly string _plainPath;
    private readonly string? _zoomPath;

    private string _shotPath;
    private List<Annotation> _annotations = new();
    private double _defaultStroke = 4;
    private bool _showZoom;
    private bool _suppressZoomToggle;

    private Annotation? _selected;
    private Annotation? _draft;
    private DragMode _drag = DragMode.None;
    private Point _dragStart;
    private Action<Point>? _activeHandle;   // set while dragging a grip
    private bool _suppressCalloutTextEvent;

    /// <summary>True if the screenshot file was rewritten (Save or Reset), so
    /// the caller should refresh any cached thumbnails/previews.</summary>
    public bool ChangedOnDisk { get; private set; }

    public AnnotationEditorWindow(Step step)
    {
        InitializeComponent();
        _step = step;
        _plainPath = step.ScreenshotPath ?? step.ImagePath ?? "";
        _zoomPath = step.HasZoom ? step.ZoomImagePath : null;
        _showZoom = step.ShowZoom && _zoomPath != null;
        _shotPath = _showZoom ? _zoomPath! : _plainPath;

        // Annotations are shared across the plain/zoom variants (same pixel size);
        // the toggle only swaps which base image they're drawn on, so toggling
        // the inset never discards edits.
        _annotations = AnnotationStore.Load(_shotPath);

        // Only offer the zoom toggle when the step actually has a zoomed variant.
        if (_zoomPath != null)
        {
            ZoomInsetToggle.Visibility = Visibility.Visible;
            _suppressZoomToggle = true;
            ZoomInsetToggle.IsChecked = _showZoom;
            _suppressZoomToggle = false;
        }

        LoadBaseImage();
        Select(null);

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => RebuildInk();
    }

    /// <summary>(Re)load just the base image for the current variant. The
    /// annotation list is left untouched so switching the zoom inset keeps edits.</summary>
    private void LoadBaseImage()
    {
        var bmp = LoadUncached(AnnotationStore.BasePathFor(_shotPath));
        BaseImage.Source = bmp;
        // Size everything in raw image pixels (Stretch=Fill on the Image) so
        // canvas coordinates equal image coordinates even if the PNG carries
        // non-96 DPI metadata from a scaled monitor.
        BaseImage.Width = bmp.PixelWidth;
        BaseImage.Height = bmp.PixelHeight;
        Surface.Width = bmp.PixelWidth;
        Surface.Height = bmp.PixelHeight;

        // Sensible mark size relative to the screenshot resolution.
        _defaultStroke = Math.Max(3.0, bmp.PixelWidth / 320.0);
    }

    private void ZoomInset_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressZoomToggle || _zoomPath == null) return;
        _showZoom = ZoomInsetToggle.IsChecked == true;
        _shotPath = _showZoom ? _zoomPath : _plainPath;
        LoadBaseImage();   // swap the base image only; keep the same annotations
        RebuildInk();
    }

    private static BitmapImage LoadUncached(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bi.UriSource = new Uri(path, UriKind.Absolute);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    // ---- tool / color state --------------------------------------------------

    private AnnotationKind? CurrentTool =>
        ToolArrow.IsChecked == true ? AnnotationKind.Arrow :
        ToolBox.IsChecked == true ? AnnotationKind.Box :
        ToolCallout.IsChecked == true ? AnnotationKind.Callout :
        ToolBlur.IsChecked == true ? AnnotationKind.Blur :
        ToolRedact.IsChecked == true ? AnnotationKind.Redact :
        null; // Select

    private string CurrentColorHex =>
        SwRed.IsChecked == true ? "#E5484D" :
        SwBlue.IsChecked == true ? "#0B68CB" :
        SwGreen.IsChecked == true ? "#18794E" :
        SwBlack.IsChecked == true ? "#1A1A1A" :
        "#8A0101";

    // ---- mouse ----------------------------------------------------------------

    private void Ink_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Point p = e.GetPosition(Ink);
        var tool = CurrentTool;

        if (tool == null) // Select
        {
            // Grabbing a grip of the already-selected mark edits that end/corner
            // (arrow length+angle, callout head/label, box/region resize).
            if (_selected != null && _annotations.Contains(_selected))
            {
                foreach (var h in HandlesFor(_selected))
                    if (Distance(p.X, p.Y, h.pos.X, h.pos.Y) <= HandleHitRadius)
                    {
                        _drag = DragMode.Handle;
                        _activeHandle = h.apply;
                        _dragStart = p;
                        Ink.CaptureMouse();
                        return;
                    }
            }

            var hit = HitTest(p);
            Select(hit);
            if (hit != null)
            {
                _drag = DragMode.Moving;
                _dragStart = p;
                Ink.CaptureMouse();
            }
            return;
        }

        _draft = new Annotation
        {
            Kind = tool.Value,
            X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y,
            ColorHex = tool.Value is AnnotationKind.Blur or AnnotationKind.Redact ? "#1A1A1A" : CurrentColorHex,
            StrokeWidth = _defaultStroke
        };
        _drag = DragMode.Drawing;
        _dragStart = p;
        Ink.CaptureMouse();
        RebuildInk();
    }

    private void Ink_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == DragMode.None) return;
        Point p = e.GetPosition(Ink);

        if (_drag == DragMode.Drawing && _draft != null)
        {
            if (_draft.Kind == AnnotationKind.Callout)
            {
                // Press = target point, drag = where the label goes.
                _draft.X1 = p.X; _draft.Y1 = p.Y;
            }
            else
            {
                _draft.X2 = p.X; _draft.Y2 = p.Y;
            }
            RebuildInk();
        }
        else if (_drag == DragMode.Moving && _selected != null)
        {
            _selected.Offset(p.X - _dragStart.X, p.Y - _dragStart.Y);
            _dragStart = p;
            RebuildInk();
        }
        else if (_drag == DragMode.Handle && _activeHandle != null)
        {
            _activeHandle(p);   // move just this grip's endpoint/corner
            RebuildInk();
        }
    }

    private void Ink_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Ink.ReleaseMouseCapture();
        var mode = _drag;
        _drag = DragMode.None;
        _activeHandle = null;

        if (mode != DragMode.Drawing || _draft == null) return;
        var a = _draft;
        _draft = null;

        bool keep = a.Kind switch
        {
            AnnotationKind.Arrow => Distance(a.X1, a.Y1, a.X2, a.Y2) >= 8,
            AnnotationKind.Callout => true,
            _ => a.Width >= 5 && a.Height >= 5
        };
        if (!keep) { RebuildInk(); return; }

        if (a.Kind == AnnotationKind.Callout && Distance(a.X1, a.Y1, a.X2, a.Y2) < 8)
        {
            // Simple click: place the label a bit away from the target.
            a.X1 = a.X2 + 40;
            a.Y1 = a.Y2 - 60;
        }

        _annotations.Add(a);
        Select(a);
        RebuildInk();

        if (a.Kind == AnnotationKind.Callout)
            CalloutTextBox.Focus();
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

    // ---- selection -------------------------------------------------------------

    private void Select(Annotation? a)
    {
        _selected = a;
        DeleteBtn.IsEnabled = a != null;

        bool isCallout = a?.Kind == AnnotationKind.Callout;
        CalloutBar.Visibility = isCallout ? Visibility.Visible : Visibility.Collapsed;
        if (isCallout)
        {
            _suppressCalloutTextEvent = true;
            CalloutTextBox.Text = a!.Text;
            _suppressCalloutTextEvent = false;
        }
        RebuildInk();
    }

    private Annotation? HitTest(Point p)
    {
        double tol = Math.Max(8, _defaultStroke * 2);
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            var a = _annotations[i];
            var b = BoundsOf(a);
            b.Inflate(tol, tol);
            if (!b.Contains(p)) continue;

            if (a.Kind == AnnotationKind.Arrow)
            {
                if (DistanceToSegment(p, new Point(a.X1, a.Y1), new Point(a.X2, a.Y2)) <= tol)
                    return a;
                continue;
            }
            return a;
        }
        return null;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-6) return Distance(p.X, p.Y, a.X, a.Y);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Distance(p.X, p.Y, a.X + t * dx, a.Y + t * dy);
    }

    private Rect BoundsOf(Annotation a)
    {
        if (a.Kind == AnnotationKind.Callout)
        {
            Size label = MeasureCallout(a);
            var box = new Rect(a.X1, a.Y1, label.Width, label.Height);
            box.Union(new Point(a.X2, a.Y2));
            return box;
        }
        return new Rect(new Point(a.X1, a.Y1), new Point(a.X2, a.Y2));
    }

    // ---- editing actions --------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || _selected == null) return;
        if (Keyboard.FocusedElement is TextBox) return; // typing in the callout box
        DeleteSelected_Click(this, new RoutedEventArgs());
        e.Handled = true;
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        _annotations.Remove(_selected);
        Select(null);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_annotations.Count == 0) return;
        var last = _annotations[^1];
        _annotations.RemoveAt(_annotations.Count - 1);
        if (ReferenceEquals(_selected, last)) Select(null);
        else RebuildInk();
    }

    private void CalloutTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressCalloutTextEvent || _selected?.Kind != AnnotationKind.Callout) return;
        _selected.Text = CalloutTextBox.Text;
        RebuildInk();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this,
            "Remove all arrows, boxes and callouts from this screenshot?\n\n" +
            "Blur and redaction that were already saved are permanent and cannot be restored.",
            "Reset image", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        AnnotationStore.ResetToOriginal(_shotPath);
        ChangedOnDisk = true;
        _annotations.Clear();
        Select(null);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AnnotationStore.Save(_shotPath, _annotations);   // save the chosen variant
            if (_step != null) _step.ShowZoom = _showZoom;   // remember the chosen view
            ChangedOnDisk = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save the annotated image:\n" + ex.Message,
                "WriteUp", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ---- drawing the live preview -------------------------------------------------

    private void RebuildInk()
    {
        Ink.Children.Clear();

        foreach (var a in _annotations)
            AddVisual(a);
        if (_draft != null)
            AddVisual(_draft);

        if (_selected != null && _annotations.Contains(_selected))
        {
            var gripBrush = new SolidColorBrush(Color.FromRgb(0x0B, 0x68, 0xCB));

            // A dashed outline reads as "resize me" for rectangular marks; arrows
            // and callouts just get their end grips.
            if (_selected.IsRegion)
            {
                var b = BoundsOf(_selected);
                b.Inflate(6, 6);
                var outline = new Rectangle
                {
                    Width = Math.Max(1, b.Width),
                    Height = Math.Max(1, b.Height),
                    Stroke = gripBrush,
                    StrokeThickness = Math.Max(1.5, _defaultStroke / 3),
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(outline, b.X);
                Canvas.SetTop(outline, b.Y);
                Ink.Children.Add(outline);
            }

            double gs = HandleSize;
            foreach (var h in HandlesFor(_selected))
            {
                var grip = new Ellipse
                {
                    Width = gs, Height = gs,
                    Fill = Brushes.White,
                    Stroke = gripBrush,
                    StrokeThickness = Math.Max(1.5, _defaultStroke / 3),
                    IsHitTestVisible = false   // grabbed via manual hit-test in Ink_MouseDown
                };
                Canvas.SetLeft(grip, h.pos.X - gs / 2);
                Canvas.SetTop(grip, h.pos.Y - gs / 2);
                Ink.Children.Add(grip);
            }
        }
    }

    // ---- editable grips ------------------------------------------------------

    private double HandleSize => Math.Max(12.0, _defaultStroke * 3);
    private double HandleHitRadius => HandleSize;

    /// <summary>Draggable points for the selected mark and how each moves it:
    /// arrow ends (length+angle), callout head + label anchor, or the four
    /// corners of a box/blur/redact region (resize).</summary>
    private List<(Point pos, Action<Point> apply)> HandlesFor(Annotation a)
    {
        var h = new List<(Point, Action<Point>)>();
        switch (a.Kind)
        {
            case AnnotationKind.Arrow:
                h.Add((new Point(a.X1, a.Y1), p => { a.X1 = p.X; a.Y1 = p.Y; }));
                h.Add((new Point(a.X2, a.Y2), p => { a.X2 = p.X; a.Y2 = p.Y; }));
                break;
            case AnnotationKind.Callout:
                h.Add((new Point(a.X2, a.Y2), p => { a.X2 = p.X; a.Y2 = p.Y; }));  // leader head / target
                h.Add((new Point(a.X1, a.Y1), p => { a.X1 = p.X; a.Y1 = p.Y; }));  // label anchor
                break;
            default: // Box / Blur / Redact — four corners
                h.Add((new Point(a.X1, a.Y1), p => { a.X1 = p.X; a.Y1 = p.Y; }));
                h.Add((new Point(a.X2, a.Y1), p => { a.X2 = p.X; a.Y1 = p.Y; }));
                h.Add((new Point(a.X2, a.Y2), p => { a.X2 = p.X; a.Y2 = p.Y; }));
                h.Add((new Point(a.X1, a.Y2), p => { a.X1 = p.X; a.Y2 = p.Y; }));
                break;
        }
        return h;
    }

    private void AddVisual(Annotation a)
    {
        var brush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(a.ColorHex));
        double w = a.StrokeWidth;

        switch (a.Kind)
        {
            case AnnotationKind.Arrow:
            {
                double head = Math.Max(12, w * 3.5);
                double ang = Math.Atan2(a.Y2 - a.Y1, a.X2 - a.X1);
                var shaft = new Line
                {
                    X1 = a.X1, Y1 = a.Y1,
                    X2 = a.X2 - Math.Cos(ang) * head * 0.6,
                    Y2 = a.Y2 - Math.Sin(ang) * head * 0.6,
                    Stroke = brush, StrokeThickness = w,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
                var headPoly = new Polygon
                {
                    Points = new PointCollection
                    {
                        new(a.X2, a.Y2),
                        new(a.X2 - Math.Cos(ang - 0.42) * head, a.Y2 - Math.Sin(ang - 0.42) * head),
                        new(a.X2 - Math.Cos(ang + 0.42) * head, a.Y2 - Math.Sin(ang + 0.42) * head)
                    },
                    Fill = brush,
                    IsHitTestVisible = false
                };
                Ink.Children.Add(shaft);
                Ink.Children.Add(headPoly);
                break;
            }
            case AnnotationKind.Box:
            {
                var rect = new Rectangle
                {
                    Width = Math.Max(1, a.Width), Height = Math.Max(1, a.Height),
                    Stroke = brush, StrokeThickness = w,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, a.Left);
                Canvas.SetTop(rect, a.Top);
                Ink.Children.Add(rect);
                break;
            }
            case AnnotationKind.Callout:
            {
                double fontSize = CalloutFontSize(a);
                Size labelSize = MeasureCallout(a);
                // Attach the leader to the label edge nearest the target so it
                // flips sides when the target crosses the label (matches export).
                double lx = Math.Clamp(a.X2, a.X1, a.X1 + labelSize.Width);
                double ly = Math.Clamp(a.Y2, a.Y1, a.Y1 + labelSize.Height);
                var leader = new Line
                {
                    X1 = lx, Y1 = ly, X2 = a.X2, Y2 = a.Y2,
                    Stroke = brush, StrokeThickness = Math.Max(2, w * 0.75),
                    IsHitTestVisible = false
                };
                var dot = new Ellipse
                {
                    Width = 12, Height = 12, Fill = brush, IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, a.X2 - 6);
                Canvas.SetTop(dot, a.Y2 - 6);

                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                    BorderBrush = brush,
                    BorderThickness = new Thickness(2.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 6, 10, 6),
                    MaxWidth = 440,
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(a.Text) ? "…" : a.Text,
                        FontFamily = new FontFamily("Segoe UI"),
                        FontWeight = FontWeights.Bold,
                        FontSize = fontSize,
                        Foreground = brush,
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                Canvas.SetLeft(label, a.X1);
                Canvas.SetTop(label, a.Y1);

                Ink.Children.Add(leader);
                Ink.Children.Add(dot);
                Ink.Children.Add(label);
                break;
            }
            case AnnotationKind.Blur:
            case AnnotationKind.Redact:
            {
                bool redact = a.Kind == AnnotationKind.Redact;
                var region = new Border
                {
                    Width = Math.Max(1, a.Width), Height = Math.Max(1, a.Height),
                    Background = redact
                        ? Brushes.Black
                        : new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
                    BorderBrush = redact ? Brushes.Black : new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    BorderThickness = new Thickness(redact ? 0 : 2),
                    IsHitTestVisible = false
                };
                if (a.Width > 70 && a.Height > 26)
                {
                    region.Child = new TextBlock
                    {
                        Text = redact ? "REDACT" : "BLUR",
                        FontFamily = new FontFamily("Segoe UI"),
                        FontWeight = FontWeights.Bold,
                        FontSize = Math.Clamp(Math.Min(a.Width / 6, a.Height / 2), 12, 28),
                        Foreground = redact ? Brushes.White : Brushes.Black,
                        Opacity = 0.75,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                Canvas.SetLeft(region, a.Left);
                Canvas.SetTop(region, a.Top);
                Ink.Children.Add(region);
                break;
            }
        }
    }

    private static double CalloutFontSize(Annotation a) => Math.Max(14, a.StrokeWidth * 4);

    private Size MeasureCallout(Annotation a)
    {
        var ft = new FormattedText(
            string.IsNullOrWhiteSpace(a.Text) ? "…" : a.Text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, // qualified: Window has a FlowDirection property
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            CalloutFontSize(a),
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = 420 };
        return new Size(ft.WidthIncludingTrailingWhitespace + 25, ft.Height + 17);
    }
}
