using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using NAP.Presentation;
namespace NAP.App;

/// <summary>Read-only enlargement of the already verified buffers. No asset I/O or approval commands.</summary>
public partial class ComparisonWindow : Window
{
    public static readonly DependencyProperty ZoomValueProperty=DependencyProperty.Register(nameof(ZoomValue),typeof(double),typeof(ComparisonWindow),new PropertyMetadata(1d));
    public double ZoomValue{get=>(double)GetValue(ZoomValueProperty);set=>SetValue(ZoomValueProperty,value);}
    private readonly WorkflowViewModel _model;
    internal ComparisonWindow(WorkflowViewModel model)
    {
        InitializeComponent();Style=(Style)FindResource(typeof(Window));DataContext=_model=model;
        model.PropertyChanged+=ModelChanged;
        Closed+=(_,_)=>model.PropertyChanged-=ModelChanged;
        SizeChanged+=(_,_)=>FitImages();
        Loaded+=(_,_)=>FitImages();
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {if(e.PropertyName==nameof(WorkflowViewModel.HasPreview)&&!_model.HasPreview)Close();}
    private void FitImages()
    {
        if(ImageScroll.ActualWidth>0&&ImageScroll.ActualHeight>0)
        {
            // 1x means fit to the current viewport; larger values scroll on both axes.
            ImageStage.Width=Math.Max(200,ImageScroll.ActualWidth-32);
            ImageStage.Height=Math.Max(180,ImageScroll.ActualHeight-32);
        }
    }
    private void ZoomIn(object sender,RoutedEventArgs e)=>ZoomValue=Math.Min(3,ZoomValue+.25);
    private void ZoomOut(object sender,RoutedEventArgs e)=>ZoomValue=Math.Max(.5,ZoomValue-.25);
    private void Fit(object sender,RoutedEventArgs e){ZoomValue=1;FitImages();ImageScroll.ScrollToHome();}
    private void CloseComparison(object sender,RoutedEventArgs e)=>Close();
    private void HandleKey(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}}
    internal void PrepareCapture(double zoom){UpdateLayout();FitImages();ZoomValue=zoom;UpdateLayout();}
}
