using System.Windows;
using NAP.Presentation;
using Forms = System.Windows.Forms;
namespace NAP.App;
/// <summary>Native Windows tray only. Menus navigate/show/exit, never execute production or Git actions.</summary>
internal sealed class TrayHost : IDisposable
{
    private readonly MainWindow _window;
    private readonly ShellViewModel _shell;
    private readonly Forms.NotifyIcon _icon;
    internal TrayHost(MainWindow window, ShellViewModel shell)
    {
        _window = window; _shell = shell;
        var menu = new Forms.ContextMenuStrip();
        Add(menu, "Abrir NAP", null); Add(menu, "Inicio", "Inicio"); Add(menu, "Producción", "Producción"); Add(menu, "Historial", "Historial");
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => _window.Dispatcher.Invoke(_window.Close));
        _icon = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "NAP · Nexus Asset Platform", ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => Restore();
    }
    private void Add(Forms.ContextMenuStrip menu, string title, string? page) => menu.Items.Add(title, null, (_, _) => _window.Dispatcher.Invoke(() => { Restore(); if (page is not null) _shell.NavigateTo(page); }));
    internal void Hide()
    {
        if (_shell.IsExecuting) return;
        _icon.Visible = true; _window.Hide();
    }
    internal bool IsVisible => _icon.Visible;
    internal void Restore()
    {
        _window.Dispatcher.Invoke(() => { _window.Show(); _window.WindowState = WindowState.Maximized; _window.Activate(); _icon.Visible = false; });
    }
    public void Dispose() { _icon.Visible = false; _icon.ContextMenuStrip?.Dispose(); _icon.Dispose(); }
}
