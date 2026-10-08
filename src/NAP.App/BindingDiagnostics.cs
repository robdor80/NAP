using System.Diagnostics;
namespace NAP.App;
internal sealed class BindingDiagnostics : TraceListener
{
    internal int Errors { get; private set; }
    public override void Write(string? message) { }
    public override void WriteLine(string? message) { if (!string.IsNullOrWhiteSpace(message)) { Errors++; Console.Error.WriteLine("wpf_binding_error: " + message); } }
    internal void Attach() { PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error; PresentationTraceSources.DataBindingSource.Listeners.Add(this); }
    internal void Detach() => PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
}
