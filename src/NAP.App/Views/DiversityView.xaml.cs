using System.Windows;
using System.Windows.Controls;
using NAP.Presentation;

namespace NAP.App.Views;

public partial class DiversityView : UserControl
{
    public DiversityView() => InitializeComponent();

    private void CopyAggregateJson(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiversityViewModel { Report: { } report })
            Clipboard.SetText(report.ToJson());
    }
}
