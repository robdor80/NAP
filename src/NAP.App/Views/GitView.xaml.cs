using System.Windows.Controls;
namespace NAP.App.Views;
public partial class GitView : UserControl { public GitView() => InitializeComponent();
    private void ConfigureGit(object sender, System.Windows.RoutedEventArgs e) { AdvancedGit.IsExpanded = true; AdvancedGit.BringIntoView(); } }
