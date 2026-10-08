using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NAP.App.Controls;

/// <summary>The outline geometry leaves a real opening around the measured title; it never paints over a border.</summary>
public sealed class RobTitledFrame : HeaderedContentControl { }
public sealed class RobFrameOutline : FrameworkElement
{
    public static readonly DependencyProperty HeaderWidthProperty = DependencyProperty.Register(nameof(HeaderWidth), typeof(double), typeof(RobFrameOutline), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(RobFrameOutline), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(RobFrameOutline), new FrameworkPropertyMetadata(18d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TitleInsetProperty = DependencyProperty.Register(nameof(TitleInset), typeof(double), typeof(RobFrameOutline), new FrameworkPropertyMetadata(42d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TitleGapProperty = DependencyProperty.Register(nameof(TitleGap), typeof(double), typeof(RobFrameOutline), new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double HeaderWidth { get => (double)GetValue(HeaderWidthProperty); set => SetValue(HeaderWidthProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public double TitleInset { get => (double)GetValue(TitleInsetProperty); set => SetValue(TitleInsetProperty, value); }
    public double TitleGap { get => (double)GetValue(TitleGapProperty); set => SetValue(TitleGapProperty, value); }
    public double GapStart => TitleInset - TitleGap;
    public double GapEnd => Math.Min(ActualWidth - Radius, TitleInset + HeaderWidth + TitleGap);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth - 0.5; var h = ActualHeight - 0.5; var r = Math.Min(Radius, Math.Min(w, h) / 2);
        if (w <= 2 * r || h <= 2 * r) return;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new(Math.Max(r, GapEnd), 0.5), false, false);
            g.LineTo(new(w - r, 0.5), true, false); g.ArcTo(new(w, r), new(r, r), 0, false, SweepDirection.Clockwise, true, false);
            g.LineTo(new(w, h - r), true, false); g.ArcTo(new(w - r, h), new(r, r), 0, false, SweepDirection.Clockwise, true, false);
            g.LineTo(new(r, h), true, false); g.ArcTo(new(0.5, h - r), new(r, r), 0, false, SweepDirection.Clockwise, true, false);
            g.LineTo(new(0.5, r), true, false); g.ArcTo(new(r, 0.5), new(r, r), 0, false, SweepDirection.Clockwise, true, false);
            g.LineTo(new(Math.Max(r, GapStart), 0.5), true, false);
        }
        geometry.Freeze(); dc.DrawGeometry(null, new Pen(Stroke, 1), geometry);
    }
}
