using System.Windows.Input;

namespace NAP.Presentation;

public sealed class UiAsyncCommand(Func<Task> execute, Func<bool> permitted, Action<Exception> failed, Func<string?>? reason = null) : ObservableObject, ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && permitted();
    public string? DisabledReason => CanExecute(null) ? null : _running ? "Operación en curso. Espera a que termine." : reason?.Invoke() ?? "Esta acción no está disponible en el estado actual.";
    public async void Execute(object? parameter) => await ExecuteAsync();
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        _running = true; Refresh();
        try { await execute(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { failed(error); }
        finally { _running = false; Refresh(); }
    }
    public void Refresh() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); Notify(nameof(DisabledReason)); }
}
