using System.Windows;
using System.Windows.Controls;
using NAP.Presentation;
using NAP.Core;
namespace NAP.App.Views;
public partial class WorkflowView : UserControl
{
    public WorkflowView()=>InitializeComponent();
    private async void DeleteNotice(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not WorkflowViewModel
            { NoticeRemovalClient: { } client, NoticeContext: { } context, SelectedNotice: { } id } model) return;
        await RemovalDialog.RunAsync(button, client, context, new(Notice: id),
            () => model.NoticeContext == context && model.SelectedNotice == id, model.NoticeRemovedAsync);
    }
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
