using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAP.Presentation;

namespace NAP.App;

// Runner-only self-test instrumentation. The same file is copied into the baseline build.
// Captures actual WPF visuals; Popup has its own HWND and is rendered separately, then
// composed at its logical anchor in the full client image, independent of runner desktop size.
internal static class Phase6UxEvidence
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Tree(child)) yield return nested;
        }
    }
    internal static async Task CaptureAsync(MainWindow window, ShellViewModel shell, BindingDiagnostics diagnostics, Grid capture, string lab)
    {
        var records = new List<object>();
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        async Task Layout()
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            capture.Measure(new Size(capture.Width, capture.Height));
            capture.Arrange(new Rect(0, 0, capture.Width, capture.Height)); capture.UpdateLayout();
        }
        void Png(Visual visual, int width, int height, string name)
        {
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = new FileStream(Path.Combine(lab, name + ".png"), FileMode.CreateNew); encoder.Save(file);
        }
        async Task Shot(string name, FrameworkElement? popup = null, FrameworkElement? anchor = null)
        {
            await Layout();
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(capture), null, new Rect(0, 0, capture.Width, capture.Height));
                if (popup is not null)
                {
                    popup.UpdateLayout();
                    var w = popup.ActualWidth; var h = popup.ActualHeight;
                    Require(w > 0 && h > 0, "Popup vacío: " + name);
                    Png(popup, (int)Math.Ceiling(w), (int)Math.Ceiling(h), name + "-popup");
                    var point = anchor!.TransformToAncestor(capture).Transform(new Point(0, anchor.ActualHeight));
                    var x = Math.Clamp(point.X, 0, Math.Max(0, capture.Width - w));
                    var y = point.Y + h <= capture.Height ? point.Y : Math.Max(0, point.Y - anchor.ActualHeight - h);
                    dc.DrawRectangle(new VisualBrush(popup), null, new Rect(x, y, w, h));
                }
            }
            Png(drawing, (int)capture.Width, (int)capture.Height, name);
            Require(diagnostics.Errors == 0, "Errores de binding UX.");
            records.Add(new { name, width = capture.Width, height = capture.Height, page = shell.CurrentPage.Name,
                section = shell.Settings.Section, rows = shell.Settings.Historical.Rows.Count, bindingErrors = diagnostics.Errors,
                rendering = "WPF client visual; separate native popup composed at logical control anchor" });
        }
        foreach (var (width, height) in new[] { (1440, 900), (1200, 700) })
        {
            capture.Width = width; capture.Height = height; shell.IsCompact = width == 1200;
            var size = $"{width}x{height}";
            shell.Navigate.Execute("Ajustes"); await shell.LastRefresh;
            shell.Settings.Section = "Histórico sintético";
            var historical = shell.Settings.Historical;
            historical.ResetFilters.Execute(null); Require(historical.Rows.Count == 39, "Reset inicial UX.");
            await Shot("ux-historico-39-" + size);
            foreach (var name in new[] { "Grupo histórico", "Estado de simulación" })
            {
                var combo = Tree(capture).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == name);
                combo.IsDropDownOpen = true; await Layout();
                var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
                await Shot("ux-historico-" + (name.StartsWith("Grupo") ? "grupos" : "estados") + "-" + size, (FrameworkElement)popup.Child, combo);
                combo.IsDropDownOpen = false;
            }
            historical.Search = "001"; Require(historical.Rows.Count == 1 && historical.Rows[0].Identity.Contains("001"), "Búsqueda 001 UX.");
            await Shot("ux-historico-busqueda-001-" + size);
            var reset = Tree(capture).OfType<Button>().Single(b => Equals(b.Content, "Restablecer") && b.IsVisible);
            reset.Command.Execute(reset.CommandParameter); Require(historical.Rows.Count == 39 && historical.Search == "", "Restablecer UX.");
            await Shot("ux-historico-restablecido-" + size);
            historical.Group = historical.Groups.Single(g => g.Value == "farmer"); Require(historical.Rows.Count == 10, "Farmer UX.");
            await Shot("ux-historico-farmer-10-" + size); historical.ResetFilters.Execute(null);

            foreach (var section in new[] { "Licencias", "Diagnóstico" })
            {
                shell.Settings.Section = section; await Layout();
                var picker = Tree(capture).OfType<DatePicker>().First(p => p.IsVisible);
                picker.SelectedDate = new DateTime(2026, 10, 25); picker.DisplayDate = new DateTime(2026, 10, 25);
                picker.BringIntoView(); await Layout();
                picker.IsDropDownOpen = true; await Layout();
                var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
                var calendarRoot = (FrameworkElement)popup.Child;
                var calendar = calendarRoot as Calendar ?? Tree(calendarRoot).OfType<Calendar>().Single();
                // Range is capture-only: demonstrate disabled and adjacent-month days.
                calendar.BlackoutDates.Add(new CalendarDateRange(new DateTime(2026, 10, 8), new DateTime(2026, 10, 10)));
                var prefix = "ux-calendario-" + (section == "Licencias" ? "licencias" : "diagnostico") + "-";
                await Shot(prefix + "dias-" + size, calendarRoot, picker);
                calendar.DisplayMode = CalendarMode.Year; await Shot(prefix + "meses-" + size, calendarRoot, picker);
                calendar.DisplayMode = CalendarMode.Decade; await Shot(prefix + "anios-" + size, calendarRoot, picker);
                calendar.DisplayMode = CalendarMode.Month;
                calendar.BlackoutDates.Clear();
                var item = Tree(calendar).OfType<CalendarItem>().Single();
                ((Button)item.Template.FindName("PART_NextButton", item)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(calendar.DisplayDate.Month == 11, "Navegación calendario UX.");
                calendar.SelectedDate = new DateTime(2026, 11, 6);
                Require(picker.SelectedDate == new DateTime(2026, 11, 6), "Selección fecha UX.");
                picker.IsDropDownOpen = false;
            }
            shell.ToolsExpanded = true;
            foreach (var tool in shell.Tools)
            {
                shell.Navigate.Execute(tool.Name); await shell.LastRefresh; await Layout();
                var button = Tree(capture).OfType<Button>().Single(b => Equals(b.Content, tool.Name) && b.IsVisible);
                button.BringIntoView(); await Layout();
                await Shot("ux-herramientas-" + tool.Name + "-" + size);
                button.Focus(); await Shot("ux-herramientas-" + tool.Name + "-foco-" + size);
                System.Windows.Input.Keyboard.ClearFocus();
                var scroll = Tree(capture).OfType<ScrollViewer>().Single(s => s.Name == "ToolsScroll");
                var bounds = button.TransformToAncestor(scroll).TransformBounds(new Rect(button.RenderSize));
                records.Add(new { tool = tool.Name, width, height, buttonBounds = bounds.ToString(), viewportWidth = scroll.ViewportWidth,
                    leftEdgeInsideViewport = bounds.Left >= 0, rightEdgeInsideViewport = bounds.Right <= scroll.ActualWidth });
            }
        }
        File.WriteAllText(Path.Combine(lab, "ux-verification.json"), JsonSerializer.Serialize(new { captures = records,
            search001 = 1, reset = 39, farmer = 10, dateNavigationAndSelection = true, bindingErrors = diagnostics.Errors }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
