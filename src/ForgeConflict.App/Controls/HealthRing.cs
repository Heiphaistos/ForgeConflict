using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.App.Controls;

/// <summary>
/// Anneau segmente : un arc par source d'inventaire, colore selon son etat.
/// Repond d'un regard a la question qui compte au demarrage : tout a-t-il repondu ?
///
/// Une piste de fond continue reste visible sous les segments : sans elle, un anneau
/// a trois sources et un anneau a dix se ressemblent, et l'oeil perd l'echelle.
/// </summary>
public sealed class HealthRing : Control
{
    private const double Thickness = 13;
    private const double TrackThickness = 3;
    private const double GapDegrees = 3.5;

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<HealthLevel>), typeof(HealthRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Fraction dessinee, de 0 a 1. Anime a l'apparition des resultats.</summary>
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(HealthRing),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<HealthLevel>? Segments
    {
        get => (IReadOnlyList<HealthLevel>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;

        DrawTrack(context, center, radius);

        var segments = Segments;
        if (segments is null || segments.Count == 0) return;

        var visible = (int)Math.Ceiling(segments.Count * Math.Clamp(Progress, 0, 1));
        var sweep = 360.0 / segments.Count;

        for (var i = 0; i < visible; i++)
        {
            // -90 place le premier segment en haut plutot qu'a droite.
            var start = (i * sweep) - 90 + (GapDegrees / 2);
            var end = start + sweep - GapDegrees;

            var brush = BrushFor(segments[i]);

            var pen = new Pen(brush, Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };

            context.DrawGeometry(null, pen, BuildArc(center, radius, start, end));
        }
    }

    private void DrawTrack(DrawingContext context, Point center, double radius)
    {
        var track = TryFindResource("BordureTheme") as Brush ?? Brushes.Gray;
        var pen = new Pen(track, TrackThickness);
        context.DrawEllipse(null, pen, center, radius, radius);
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
