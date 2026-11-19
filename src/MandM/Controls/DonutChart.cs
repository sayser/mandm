using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using MandM.Models;

namespace MandM.Controls;

/// <summary>
/// Ability doughnut. Same ring as EQDM: fixed outer diameter, thick band,
/// slices start at 12 o'clock, empty slate ring when there is no damage.
/// </summary>
public sealed class DonutChart : FrameworkElement
{
    private static readonly Brush EmptyRingBrush = CreateFrozenBrush(Color.FromRgb(38, 54, 83));
    private static readonly Dictionary<int, Pen> EmptyRingPens = [];
    private static readonly Dictionary<(Brush Brush, int ThicknessKey), Pen> SegmentPens = [];

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (DonutChart)d;
        if (e.OldValue is INotifyCollectionChanged oldSource)
            oldSource.CollectionChanged -= chart.OnItemsChanged;
        if (e.NewValue is INotifyCollectionChanged newSource)
            newSource.CollectionChanged += chart.OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        long total = 0;
        List<AbilityDamageRow>? items = null;
        if (ItemsSource is not null)
        {
            foreach (var entry in ItemsSource)
            {
                if (entry is not AbilityDamageRow item || item.Damage <= 0)
                    continue;
                items ??= [];
                items.Add(item);
                total += item.Damage;
            }
        }

        var outerRadius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 6);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var thickness = Math.Clamp(outerRadius * 0.46, 12, outerRadius * 0.72);
        var pathRadius = Math.Max(0, outerRadius - thickness / 2);
        var thicknessKey = (int)Math.Round(thickness * 100);

        drawingContext.DrawEllipse(null, GetEmptyRingPen(thicknessKey, thickness), center, pathRadius, pathRadius);
        if (total <= 0 || pathRadius <= 0 || items is null)
            return;

        var start = -90d;
        foreach (var item in items)
        {
            var sweep = item.Damage * 360d / total;
            if (sweep >= 359.999)
            {
                drawingContext.DrawEllipse(null, GetSegmentPen(item.Color, thicknessKey, thickness), center, pathRadius,
                    pathRadius);
            }
            else if (sweep > 0.05)
            {
                var geometry = CreateArc(center, pathRadius, start, sweep);
                drawingContext.DrawGeometry(null, GetSegmentPen(item.Color, thicknessKey, thickness), geometry);
            }

            start += sweep;
        }
    }

    private static Pen GetEmptyRingPen(int thicknessKey, double thickness)
    {
        lock (EmptyRingPens)
        {
            if (EmptyRingPens.TryGetValue(thicknessKey, out var cached))
                return cached;
            var pen = new Pen(EmptyRingBrush, thickness);
            pen.Freeze();
            EmptyRingPens[thicknessKey] = pen;
            return pen;
        }
    }

    private static Pen GetSegmentPen(Brush brush, int thicknessKey, double thickness)
    {
        var key = (brush, thicknessKey);
        lock (SegmentPens)
        {
            if (SegmentPens.TryGetValue(key, out var cached))
                return cached;
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat
            };
            pen.Freeze();
            SegmentPens[key] = pen;
            return pen;
        }
    }

    private static PathGeometry CreateArc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var startRadians = startDegrees * Math.PI / 180d;
        var endRadians = (startDegrees + sweepDegrees) * Math.PI / 180d;
        var start = new Point(center.X + radius * Math.Cos(startRadians), center.Y + radius * Math.Sin(startRadians));
        var end = new Point(center.X + radius * Math.Cos(endRadians), center.Y + radius * Math.Sin(endRadians));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, sweepDegrees > 180,
            SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
