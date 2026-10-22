using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAP.Core;
namespace NAP.App.Controls;

/// <summary>Presentation-only overlay of host-declared margins; never changes image bytes.</summary>
public sealed class ComparisonBorderGuides : FrameworkElement
{
    private static FrameworkPropertyMetadata Render(object? value)=>new(value,FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty SourceProperty=DependencyProperty.Register(nameof(Source),typeof(BitmapSource),typeof(ComparisonBorderGuides),Render(null));
    public static readonly DependencyProperty PreviewProperty=DependencyProperty.Register(nameof(Preview),typeof(ReviewPreview),typeof(ComparisonBorderGuides),Render(null));
    public static readonly DependencyProperty ShowGuidesProperty=DependencyProperty.Register(nameof(ShowGuides),typeof(bool),typeof(ComparisonBorderGuides),Render(false));
    public BitmapSource? Source{get=>(BitmapSource?)GetValue(SourceProperty);set=>SetValue(SourceProperty,value);}
    public ReviewPreview? Preview{get=>(ReviewPreview?)GetValue(PreviewProperty);set=>SetValue(PreviewProperty,value);}
    public bool ShowGuides{get=>(bool)GetValue(ShowGuidesProperty);set=>SetValue(ShowGuidesProperty,value);}
    internal int VisibleGuides{get;private set;}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);VisibleGuides=0;
        if(!ShowGuides||Source is not {} source||Preview is not {} p||p.CanvasWidth<=0||p.CanvasHeight<=0)return;
        var scale=Math.Min(ActualWidth/source.PixelWidth,ActualHeight/source.PixelHeight);
        var width=source.PixelWidth*scale;var height=source.PixelHeight*scale;
        var x=(ActualWidth-width)/2;var y=(ActualHeight-height)/2;
        var color=(Brush)FindResource("WarningBrush");var fill=color.Clone();fill.Opacity=.4;
        // A minimum visible stroke is a guide, not an assertion of extra image pixels.
        void Strip(int margin,Rect rect){if(margin>0){drawing.DrawRectangle(fill,new Pen(color,.8),rect);VisibleGuides++;}}
        var left=Math.Max(1,width*p.Left/p.CanvasWidth);var right=Math.Max(1,width*p.Right/p.CanvasWidth);
        var top=Math.Max(1,height*p.Top/p.CanvasHeight);var bottom=Math.Max(1,height*p.Bottom/p.CanvasHeight);
        Strip(p.Left,new(x,y,left,height));Strip(p.Right,new(x+width-right,y,right,height));
        Strip(p.Top,new(x,y,width,top));Strip(p.Bottom,new(x,y+height-bottom,width,bottom));
    }
}
