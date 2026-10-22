namespace NAP.Core;
public sealed record LocalDiagnosticSnapshot(IReadOnlyList<JobState> States, bool More, bool ReadError);
public static class LocalDiagnosticReader
{
    /// <summary>Bounded existing last-state journals only. No initialization, recovery, Inbox scan, or inferred event dates.</summary>
    public static LocalDiagnosticSnapshot Read(UniverseContext context, CancellationToken cancellation)
    {
        var states=new List<JobState>();var error=false;
        try
        {
            ProductionPaths.CheckPath(context.Storage.StateRoot,NapIssueCodes.CatalogInvalid);
            if(!Directory.Exists(context.Storage.StateRoot))return new([],false,false);
            var paths=Directory.EnumerateFiles(context.Storage.StateRoot,"job_*.json",SearchOption.TopDirectoryOnly).Take(31).ToArray();
            foreach(var path in paths.Take(30))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    ProductionPaths.CheckPath(path,NapIssueCodes.CatalogInvalid);
                    if(new FileInfo(path).Length>16*1024)throw new InvalidDataException("Journal exceeds its diagnostic bound.");
                    states.Add(new JobStateStore(context).Load(new(Path.GetFileNameWithoutExtension(path))).State);
                }
                catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException){error=true;}
            }
            return new(states,paths.Length>30,error);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException){return new(states,false,true);}
    }
}
