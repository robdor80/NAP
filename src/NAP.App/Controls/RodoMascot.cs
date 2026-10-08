using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace NAP.App.Controls;

/// <summary>Canonical static PNG; WPF owns idle motion. No timers, requests or external codecs.</summary>
public sealed class RodoMascot : Image
{
    private static readonly BitmapSource Portrait = LoadPortrait();
    private readonly TranslateTransform _float = new();
    private bool _loaded;
    public RodoMascot()
    {
        Source = Portrait; Stretch = Stretch.Uniform; RenderTransform = _float;
        Loaded += (_, _) => { _loaded = true; UpdateMotion(); };
        Unloaded += (_, _) => { _loaded = false; StopMotion(); };
        IsVisibleChanged += (_, _) => UpdateMotion();
    }
    internal bool IsAnimating => _float.HasAnimatedProperties;
    private void UpdateMotion()
    {
        if (!_loaded || !IsVisible) { StopMotion(); return; }
        if (IsAnimating) return;
        _float.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-3, 3, TimeSpan.FromSeconds(2.1))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }
    private void StopMotion() { _float.BeginAnimation(TranslateTransform.YProperty, null); _float.Y = 0; }
    private static BitmapSource LoadPortrait()
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri("pack://application:,,,/NAP.App;component/Assets/RoDo/rodo_v0_1.png"); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
}
