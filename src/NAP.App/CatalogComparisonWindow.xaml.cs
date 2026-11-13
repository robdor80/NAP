using System.Windows;
using NAP.Presentation;

namespace NAP.App;

/// <summary>Temporary read-only visual comparison: no second catalog reader or asset writer.</summary>
public partial class CatalogComparisonWindow : Window
{
    private readonly AssetTileViewModel _right;
    public CatalogComparisonWindow(AssetTileViewModel left, AssetTileViewModel right)
    {
        ArgumentNullException.ThrowIfNull(left);
        _right = right ?? throw new ArgumentNullException(nameof(right));
        InitializeComponent();
        DataContext = new CatalogComparisonPair(left, right);
        Closed += (_, _) => _right.Dispose();
    }

    private void CloseComparison(object sender, RoutedEventArgs e) => Close();
}

public sealed record CatalogComparisonPair(AssetTileViewModel Left, AssetTileViewModel Right);
