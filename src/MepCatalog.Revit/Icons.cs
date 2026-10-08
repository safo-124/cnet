using System.Windows.Media;

namespace MepCatalog.Revit;

/// <summary>Ribbon icons drawn from vector paths, so no image files have to ship with the add-in.</summary>
internal static class Icons
{
    // Steel blue, the web app's accent color.
    private static readonly Brush Stroke = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0)));

    /// <summary>Magnifier over a checklist.</summary>
    public static ImageSource Audit { get; } = Create("M3 5 H11 M3 10 H8 M3 15 H7 M15 15 m-5 0 a5 5 0 1 0 10 0 a5 5 0 1 0 -10 0 M18.6 18.6 L22 22");

    /// <summary>List with a plus.</summary>
    public static ImageSource Parameters { get; } = Create("M3 5 H15 M3 11 H15 M3 17 H10 M18 14 V22 M14 18 H22");

    /// <summary>Two sliders.</summary>
    public static ImageSource Settings { get; } = Create("M3 7 H21 M3 17 H21 M9 7 m-2.5 0 a2.5 2.5 0 1 0 5 0 a2.5 2.5 0 1 0 -5 0 M15 17 m-2.5 0 a2.5 2.5 0 1 0 5 0 a2.5 2.5 0 1 0 -5 0");

    private static ImageSource Create(string path)
    {
        var pen = new Pen(Stroke, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var drawing = new DrawingGroup();
        // A transparent 24x24 frame keeps every icon the same size and centered, whatever its shape.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 24, 24))));
        drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(path)));
        return Freeze(new DrawingImage(drawing));
    }

    private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
