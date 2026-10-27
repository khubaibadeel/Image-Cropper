using System.Windows;

namespace PrecisionImageCropper.Utilities;

public enum CropPointerRegion
{
    Outside, Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight
}

/// <summary>Pure hit-testing geometry for crop handles and the draggable crop body.</summary>
public static class CropInteractionGeometry
{
    public static CropPointerRegion GetRegion(Rect selection, Point point, double handleRadius = 10)
    {
        var points = HandlePoints(selection);
        var regions = new[]
        {
            CropPointerRegion.TopLeft, CropPointerRegion.Top, CropPointerRegion.TopRight,
            CropPointerRegion.Right, CropPointerRegion.BottomRight, CropPointerRegion.Bottom,
            CropPointerRegion.BottomLeft, CropPointerRegion.Left
        };

        for (var index = 0; index < points.Count; index++)
            if ((points[index] - point).Length <= handleRadius) return regions[index];

        return selection.Contains(point) ? CropPointerRegion.Move : CropPointerRegion.Outside;
    }

    public static IReadOnlyList<Point> HandlePoints(Rect selection) => new[]
    {
        new Point(selection.Left, selection.Top),
        new Point(selection.Left + selection.Width / 2, selection.Top),
        new Point(selection.Right, selection.Top),
        new Point(selection.Right, selection.Top + selection.Height / 2),
        new Point(selection.Right, selection.Bottom),
        new Point(selection.Left + selection.Width / 2, selection.Bottom),
        new Point(selection.Left, selection.Bottom),
        new Point(selection.Left, selection.Top + selection.Height / 2)
    };
}
