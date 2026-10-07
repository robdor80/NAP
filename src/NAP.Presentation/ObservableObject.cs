using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NAP.Presentation;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name); return true; }
}
public sealed class RelayCommand(Action<object?> action, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) action(parameter); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed class AsyncCommand(Func<Task> action, Action<Exception> onError) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running;
    public async void Execute(object? parameter) => await ExecuteAsync();
    public async Task ExecuteAsync()
    {
        if (_running) return;
        _running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { onError(ex); }
        finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
public enum ExplorerState { Loading, Empty, Error, Ready }
