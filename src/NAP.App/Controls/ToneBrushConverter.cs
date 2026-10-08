using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NAP.Presentation;
namespace NAP.App.Controls;
public sealed class ToneBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current.FindResource(value is UiTone tone ? tone switch
        { UiTone.Success => "SuccessBrush", UiTone.Warning => "WarningBrush", UiTone.Error => "ErrorBrush", UiTone.Ai => "AiBrush", _ => "TextBrush" } : "TextBrush");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class BackupKindLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is NAP.Core.BackupKind kind ? UiTerminology.Backup(kind) : "No disponible";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class VerifiedLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? "Verificado" : "No verificado";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
