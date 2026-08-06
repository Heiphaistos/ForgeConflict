using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.App.Controls;

/// <summary>
/// Anneau segmente : un arc par source d'inventaire, colore selon son etat.
/// Repond d'un regard a la question qui compte au demarrage : tout a-t-il repondu ?
/// </summary>
public sealed class HealthRing : Control
{
    private const double Thickness = 14;
    private const double GapDegrees = 3;

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<HealthLevel>), typeof(HealthRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<HealthLevel>? Segments
    {
        get => (IReadOnlyList<HealthLevel>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        var segments = Segments;
        if (segments is null || segments.Count == 0) return;

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;
        var sweep = 360.0 / segments.Count;

        for (var i = 0; i < segments.Count; i++)
        {
            // -90 place le premier segment en haut plutot qu'a droite.
            var start = (i * sweep) - 90 + (GapDegrees / 2);
            var end = start + sweep - GapDegrees;

            var pen = new Pen(BrushFor(segments[i]), Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };

            context.DrawGeometry(null, pen, BuildArc(center, radius, start, end));
        }
    }

    private static Geometry BuildArc(Point center, double radius, double startDegrees, double endDegrees)
    {
        var start = PointOnCircle(center, radius, startDegrees);
        var end = PointOnCircle(center, radius, endDegrees);

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            point: end,
            size: new Size(radius, radius),
            rotationAngle: 0,
            isLargeArc: (endDegrees - startDegrees) > 180,
            sweepDirection: SweepDirection.Clockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }

    private Brush BrushFor(HealthLevel level)
    {
        var key = level switch
        {
            HealthLevel.Sain => "Sain",
            HealthLevel.Attention => "Attention",
            HealthLevel.Critique => "Critique",
            _ => "Inconnu",
        };

        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
