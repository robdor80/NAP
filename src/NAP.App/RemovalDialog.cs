using System.Windows;
using System.Windows.Controls;
using NAP.Core;
using NAP.Presentation;

namespace NAP.App;

internal static class RemovalDialog
{
    internal static async Task RunAsync(Button button, IRemovalClient client, UniverseContext context,
        RemovalSelection selection, Func<bool> current, Func<Task> refresh)
    {
        var owner = Window.GetWindow(button);
        var title = selection.Notice is null ? "Eliminar recurso" : "Eliminar aviso definitivamente";
        button.IsEnabled = false;
        try
        {
            var plan = await client.PreviewRemovalAsync(context, selection, CancellationToken.None);
            if (!current()) return;
            var details = new List<string> { plan.Name, "Identificador: " + plan.Identifier, "" };
            details.AddRange(plan.Entries.Select(e => (e.Directory ? "Carpeta  " : "Archivo  ") + e.Path));
            details.AddRange(plan.Indexes);
            if (plan.Preserved.Count > 0)
            {
                details.Add("");
                details.Add("Se conservan");
                details.AddRange(plan.Preserved);
            }
            var explanation = selection.Notice is null
                ? "Revisa los archivos y registros que se retirarán definitivamente."
                : "Se retirará este aviso y sus copias internas. Los recursos publicados se conservan.";
            var options = !selection.IncludeInbox && plan.CanIncludeInbox
                ? "Incluir también el ZIP original de Entrada (se mostrará otra confirmación)" : null;
            var dialog = new ConfirmationDialog(new(title, explanation, context.Id, details, context.Profile.DisplayName, options))
                { Owner = owner };
            if (dialog.ShowDialog() != true || !current()) return;
            if (dialog.IncludeInbox)
            {
                await RunAsync(button, client, context, selection with { IncludeInbox = true }, current, refresh);
                return;
            }

            var result = await client.RemoveAsync(context, new(plan.Selection, plan.Fingerprint), CancellationToken.None);
            if (result.Removed) await refresh();
            var summary = result.Removed
                ? $"Se retiraron {plan.Name} y sus referencias ({result.Changes.Count} elementos en el inventario)."
                : "La eliminación no se completó.";
            var rows = result.Problems.Select(problem => "Pendiente  " + problem)
                .Concat(result.Removed ? result.Changes.Select(change => "Retirado  " + change) : []).ToArray();
            new ConfirmationDialog(new(title, summary, context.Id, rows, context.Profile.DisplayName), readOnly: true)
                { Owner = owner }.ShowDialog();
        }
        catch (Exception error)
        {
            MessageBox.Show(owner, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { button.IsEnabled = true; }
    }
}
