using System.Windows;
using System.Windows.Controls;
using NAP.Presentation;
namespace NAP.App.Views;
public partial class WorkflowView : UserControl
{
    public WorkflowView()=>InitializeComponent();
    private void OpenComparison(object sender,RoutedEventArgs e)
    {
        if(DataContext is not WorkflowViewModel {HasPreview:true} model)return;
        var owner=Window.GetWindow(this);
        new ComparisonWindow(model)
        {
            Owner=owner,Width=Math.Max(840,Math.Min(1120,owner.ActualWidth-48)),
            Height=Math.Max(560,Math.Min(820,owner.ActualHeight-48))
        }.ShowDialog();
    }
}
