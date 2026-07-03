using System;

namespace WriteUp.Models;

public enum AnnotationKind
{
    Arrow,      // line from (X1,Y1) to (X2,Y2) with an arrowhead at the end
    Box,        // rectangle outline between the two corners
    Callout,    // text label anchored at (X1,Y1) with a leader line to (X2,Y2)
    Blur,       // pixelate the rectangular region (burned in on save)
    Redact      // solid black over the rectangular region (burned in on save)
}

/// <summary>
/// One drawn mark on a screenshot. All coordinates are in image pixels, so
/// they are independent of editor zoom. Arrows/boxes/callouts stay editable
/// (persisted to a ".ann.json" sidecar and re-rendered from the pristine
/// original); Blur/Redact are destructive and are burned into the original
/// backup on save so no un-redacted copy of sensitive content remains.
/// </summary>
public class Annotation
{
    public AnnotationKind Kind { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string Text { get; set; } = "";
    public string ColorHex { get; set; } = "#8A0101";
    public double StrokeWidth { get; set; } = 4;

    public bool IsRegion => Kind is AnnotationKind.Box or AnnotationKind.Blur or AnnotationKind.Redact;
    public bool IsDestructive => Kind is AnnotationKind.Blur or AnnotationKind.Redact;

    public double Left => Math.Min(X1, X2);
    public double Top => Math.Min(Y1, Y2);
    public double Width => Math.Abs(X2 - X1);
    public double Height => Math.Abs(Y2 - Y1);

    public void Offset(double dx, double dy)
    {
        X1 += dx; Y1 += dy; X2 += dx; Y2 += dy;
    }
}
