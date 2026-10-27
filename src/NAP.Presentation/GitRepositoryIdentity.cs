using NAP.Core;

namespace NAP.Presentation;

/// <summary>Local display only: never exposes ancestors, URL or a separately selectable Git root.</summary>
public static class GitRepositoryIdentity
{
    public static string Label(UniverseId? universe, string? productionRoot)
    {
        if (universe is null || string.IsNullOrWhiteSpace(productionRoot)) return "Repositorio de producción aún no configurado";
        var root = productionRoot.Replace('\\', '/').TrimEnd('/');
        var name = root[(root.LastIndexOf('/') + 1)..];
        if (name.Length is < 1 or > 80 || name is "." or ".." || name.Any(c => !char.IsLetterOrDigit(c) && c is not (' ' or '-' or '_' or '.')))
            name = "carpeta de producción";
        return $"Repositorio: {name} · universo {universe.Value}";
    }
}
