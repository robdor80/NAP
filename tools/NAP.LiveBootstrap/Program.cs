using NAP.Core;

if (args.Length != 5)
{
    Console.Error.WriteLine("Uso: NAP.LiveBootstrap <perfil> <ajustes> <workspace> <produccion> <archivo>");
    return 2;
}
try
{
    var profile = UniverseProfileLoader.Load(args[0]);
    if (profile.Id.Value != "nimroel") throw new InvalidDataException("Se necesita el perfil Nimroel.");
    var storage = new UniverseStorageConfig(profile.Id, args[2], args[3], args[4]);
    LocalUniverseSettingsStore.ValidateAvailable(storage);
    var context = new UniverseContext(profile, storage);
    new AssetCatalog(context).Initialize();
    new LocalUniverseSettingsStore(args[1]).Save([storage], profile.Id);
    Console.WriteLine("NAP-LIVE: CATÁLOGO INICIALIZADO Y RAÍCES CONFIGURADAS.");
    Console.WriteLine("Workspace: " + storage.WorkspaceRoot);
    Console.WriteLine("Producción: " + storage.ProductionRoot);
    Console.WriteLine("Archivo: " + storage.ArchiveRoot);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("NAP-LIVE: " + error.Message);
    return 1;
}
