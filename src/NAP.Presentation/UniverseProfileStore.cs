using System.Security.Cryptography;
using NAP.Core;

namespace NAP.Presentation;

public sealed record InstalledUniverseProfile(UniverseProfile Profile, bool Imported)
{
    public string Name => Profile.DisplayName;
    public string Id => Profile.Id.Value;
    public string Origin => Imported ? "Importado" : "Incluido con NAP";
}
public sealed record UniverseProfilesSnapshot(IReadOnlyList<InstalledUniverseProfile> Installed, IReadOnlyList<UiNotice> Issues, bool BuiltInsComplete = true);
public sealed record UniverseProfileImport(UniverseId Id, UniverseProfilesSnapshot Snapshot);
public sealed class UniverseProfileStoreException(string message, string code) : IOException(message)
{ public string Code { get; } = code; }
public interface IProfileFilePicker { string? Pick(); }
public interface IUniverseProfileStore
{
    string LocalRoot { get; }
    UniverseProfilesSnapshot Discover(IReadOnlyList<UniverseStorageConfig> storage);
    UniverseProfileImport Import(string sourcePath, IReadOnlyList<UniverseStorageConfig> storage);
    void ValidateStorage(IReadOnlyList<UniverseStorageConfig> storage);
}

/// <summary>Bounded, one-level discovery and no-overwrite installation. JSON never supplies filesystem paths.</summary>
public sealed class UniverseProfileStore : IUniverseProfileStore
{
    public const int MaxProfileBytes = 1024 * 1024;
    public const int MaxEntries = 128;
    public static string DefaultLocalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NAP", "universes");
    private readonly string _builtInRoot;
    public string LocalRoot { get; }
    public UniverseProfileStore(string builtInRoot, string localRoot)
    {
        _builtInRoot = Absolute(builtInRoot); LocalRoot = Absolute(localRoot);
        if (Overlaps(_builtInRoot, LocalRoot)) throw Stop("Los perfiles locales deben estar separados de los incluidos con NAP.", "profile_root_overlap");
    }
    public UniverseProfilesSnapshot Discover(IReadOnlyList<UniverseStorageConfig> storage)
    {
        var entries = new List<InstalledUniverseProfile>(); var issues = new List<UiNotice>(); var ids = new HashSet<UniverseId>(); var builtInsComplete = true;
        ReadRoot(_builtInRoot, false);
        try { ValidateStorage(storage); ReadRoot(LocalRoot, true); }
        catch (Exception error) when (Recoverable(error)) { issues.Add(Notice(error, "No se pueden leer los perfiles locales con seguridad.")); }
        var ordered = entries.OrderBy(e => e.Id == "nimroel" ? 0 : 1).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
        _ = new UniverseRegistry(ordered.Select(e => e.Profile));
        return new(Array.AsReadOnly(ordered), issues.AsReadOnly(), builtInsComplete);

        void ReadRoot(string root, bool imported)
        {
            string[] children;
            try
            {
                var attributes = Guard(root, noCheckout: imported);
                if (attributes is null) { if (!imported) throw Stop("No están disponibles los perfiles incluidos con NAP.", "profile_builtin_missing"); return; }
                RequireDirectory(attributes);
                children = Directory.EnumerateFileSystemEntries(root).Take(MaxEntries + 1).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                if (children.Length > MaxEntries) throw Stop("La carpeta de perfiles supera el límite de entradas.", "profile_inventory_limit");
            }
            catch (Exception error) when (Recoverable(error)) { if (!imported) builtInsComplete = false; issues.Add(Notice(error, "No se puede leer la carpeta de perfiles con seguridad.")); return; }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                // A crashed import can leave only an unpublished private temporary directory.
                // Do not open, follow or delete it during discovery.
                if (imported && name.StartsWith(".nap-import-", StringComparison.Ordinal))
                { issues.Add(new("Hay una instalación de perfil incompleta. No se ha cargado.", "profile_import_incomplete", UiTone.Warning)); continue; }
                try
                {
                    SafeId(name); RequireDirectory(Guard(child, noCheckout: imported));
                    var (_, profile) = ReadProfile(Path.Combine(child, "profile.json"));
                    if (profile.Id.Value != name) throw Stop("El ID del perfil no coincide con su carpeta canónica.", "profile_id_mismatch");
                    if (!ids.Add(profile.Id)) throw Stop("Un perfil local no puede reemplazar un universo ya instalado.", "profile_duplicate");
                    entries.Add(new(profile, imported));
                }
                catch (Exception error) when (Recoverable(error))
                { if (!imported) builtInsComplete = false; issues.Add(Notice(error, AssetNamingRules.IsValidMachineIdentifier(name) ? $"El perfil «{name}» no se ha cargado: es inválido o no está disponible." : "Hay una entrada de perfil no autorizada. No se ha cargado.")); }
            }
        }
    }
    public UniverseProfileImport Import(string sourcePath, IReadOnlyList<UniverseStorageConfig> storage)
    {
        // Validate every source byte before creating any local persistent entry.
        var (bytes, profile) = ReadProfile(Absolute(sourcePath)); SafeId(profile.Id.Value); ValidateStorage(storage);
        var identity = OperatingSystem.IsWindows() ? LocalRoot.ToUpperInvariant() : LocalRoot;
        using var mutex = new Mutex(false, "NAP.UniverseProfiles." + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))));
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw Stop("Otra instalación de perfil está en curso. Reintenta cuando termine.", "profile_store_busy");
        try
        {
            ValidateStorage(storage);
            var snapshot = Discover(storage);
            if (!snapshot.BuiltInsComplete || snapshot.Issues.Any(i => i.Code is "profile_inventory_limit" or "profile_builtin_missing"))
                throw Stop("No se puede comprobar el inventario de perfiles completo.", "profile_inventory_unavailable");
            var destination = Path.Combine(LocalRoot, profile.Id.Value);
            if (snapshot.Installed.Any(p => p.Profile.Id == profile.Id) || Guard(destination, true) is not null)
                throw Stop("Ese UniverseId ya está instalado. No se sobrescribirá.", "profile_duplicate");
            if (snapshot.Installed.Count >= MaxEntries) throw Stop("Se ha alcanzado el límite de perfiles instalados.", "profile_inventory_limit");
            if (Directory.Exists(LocalRoot) && Directory.EnumerateFileSystemEntries(LocalRoot).Take(MaxEntries).Count() >= MaxEntries)
                throw Stop("La carpeta local ha alcanzado el límite de entradas.", "profile_inventory_limit");
            Directory.CreateDirectory(LocalRoot); RequireDirectory(Guard(LocalRoot, true));
            var temporary = Path.Combine(LocalRoot, ".nap-import-" + Guid.NewGuid().ToString("N"));
            var file = Path.Combine(temporary, "profile.json"); var created = false;
            try
            {
                Directory.CreateDirectory(temporary); created = true; RequireDirectory(Guard(temporary, true));
                using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
                ValidateStorage(storage); RequireDirectory(Guard(temporary, true));
                var (publishedBytes, _) = ReadProfile(file);
                if (!bytes.AsSpan().SequenceEqual(publishedBytes)) throw Stop("Los bytes del perfil cambiaron antes de publicar.", "profile_bytes_changed");
                if (Guard(destination, true) is not null) throw Stop("Ese UniverseId ya está instalado. No se sobrescribirá.", "profile_duplicate");
                // Same-volume atomic directory publication; Directory.Move never overwrites.
                Directory.Move(temporary, destination); created = false;
                return new(profile.Id, Discover(storage));
            }
            finally
            {
                if (created)
                {
                    RequireDirectory(Guard(temporary, true));
                    if (Guard(file, true) is not null) File.Delete(file);
                    Directory.Delete(temporary, recursive: false);
                }
            }
        }
        finally { mutex.ReleaseMutex(); }
    }
    public void ValidateStorage(IReadOnlyList<UniverseStorageConfig> storage)
    {
        Guard(LocalRoot, noCheckout: true);
        if (storage.SelectMany(s => new[] { s.WorkspaceRoot, s.ProductionRoot, s.ArchiveRoot }).Any(p => Overlaps(LocalRoot, p)))
            throw Stop("Los perfiles locales deben estar fuera de Workspace, Producción y Archivo de todos los universos.", "profile_storage_overlap");
    }
    private static (byte[] Bytes, UniverseProfile Profile) ReadProfile(string path)
    {
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) throw Stop("Selecciona un perfil JSON.", "profile_format_invalid");
        var attributes = Guard(path);
        if (attributes is null || (attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw Stop("El perfil debe ser un archivo normal disponible, sin enlaces.", "profile_file_invalid");
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > MaxProfileBytes) throw Stop("El perfil está vacío o supera el límite de 1 MiB.", "profile_size_invalid");
            var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes);
            if (input.ReadByte() != -1 || input.Length != bytes.Length) throw Stop("El perfil cambió durante la lectura.", "profile_bytes_changed");
            Guard(path); using var memory = new MemoryStream(bytes, writable: false);
            return (bytes, UniverseProfileLoader.Load(memory));
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException or ArgumentException or EndOfStreamException)
        { throw Stop("El perfil JSON no cumple el contrato de universo. No se ha instalado.", "profile_invalid"); }
    }
    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw Stop("La raíz de perfiles debe ser una ruta absoluta explícita.", "profile_path_invalid");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static void SafeId(string id)
    {
        _ = new UniverseId(id);
        if (new[] { "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9" }.Contains(id, StringComparer.Ordinal))
            throw Stop("El ID no puede utilizar un nombre de dispositivo reservado.", "profile_path_invalid");
    }
    private static FileAttributes? Guard(string path, bool noCheckout = false)
    {
        path = Absolute(path);
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || (OperatingSystem.IsWindows() && new DriveInfo(path).DriveType == DriveType.Network))
            throw Stop("Los perfiles deben estar en almacenamiento local.", "profile_path_invalid");
        var volume = Path.GetPathRoot(path)!; var current = volume;
        var parts = path[volume.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        FileAttributes? attributes = Check(current);
        foreach (var part in parts)
        {
            if (attributes is null) return null;
            RequireDirectory(attributes); current = Path.Combine(current, part); attributes = Check(current);
        }
        return attributes;
        FileAttributes? Check(string entry)
        {
            FileAttributes value;
            try { value = File.GetAttributes(entry); } catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
            if ((value & FileAttributes.ReparsePoint) != 0) throw Stop("No se pueden seguir symlinks o reparse points de perfiles.", "profile_reparse");
            if (noCheckout && HasEntry(Path.Combine(entry, ".git")))
                throw Stop("Los perfiles importados no pueden almacenarse en un checkout Git.", "profile_checkout");
            return value;
        }
    }
    private static bool HasEntry(string path)
    { try { _ = File.GetAttributes(path); return true; } catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; } }
    private static void RequireDirectory(FileAttributes? attributes)
    { if (attributes is null || (attributes & FileAttributes.Directory) == 0) throw Stop("Una carpeta de perfiles no está disponible.", "profile_directory_invalid"); }
    private static bool Overlaps(string a, string b)
    {
        a = Absolute(a); b = Absolute(b); var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return a.Equals(b, comparison) || b.StartsWith(Path.EndsInDirectorySeparator(a) ? a : a + Path.DirectorySeparatorChar, comparison) || a.StartsWith(Path.EndsInDirectorySeparator(b) ? b : b + Path.DirectorySeparatorChar, comparison);
    }
    private static bool Recoverable(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException;
    private static UniverseProfileStoreException Stop(string message, string code) => new(message, code);
    private static UiNotice Notice(Exception error, string fallback) => new(error is UniverseProfileStoreException e ? e.Message : fallback, error is UniverseProfileStoreException s ? s.Code : "profile_invalid", UiTone.Error);
}
