namespace NAP.Core;

/// <summary>An immutable authorized production root, produced after root validation. Does no I/O.</summary>
public sealed class ValidatedProductionRepository
{
    internal ValidatedProductionRepository(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        UniverseId = context.Id;
        RootPath = context.Storage.ProductionRoot;
    }

    public UniverseId UniverseId { get; }
    public string RootPath { get; }
}
