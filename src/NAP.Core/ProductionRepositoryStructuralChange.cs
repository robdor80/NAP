namespace NAP.Core;

/// <summary>A neutral structural observation. Does not infer moves, content changes or asset semantics.</summary>
public sealed class ProductionRepositoryStructuralChange
{
    internal ProductionRepositoryStructuralChange(string relativePath, RepositoryStructuralChangeKind kind,
        ProductionRepositoryEntryKind? beforeKind, ProductionRepositoryEntryKind? afterKind)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (beforeKind is { } before && !Enum.IsDefined(before))
            throw new ArgumentOutOfRangeException(nameof(beforeKind));
        if (afterKind is { } after && !Enum.IsDefined(after))
            throw new ArgumentOutOfRangeException(nameof(afterKind));
        var consistent = kind switch
        {
            RepositoryStructuralChangeKind.Added => beforeKind is null && afterKind is not null,
            RepositoryStructuralChangeKind.Removed => beforeKind is not null && afterKind is null,
            RepositoryStructuralChangeKind.KindChanged => beforeKind is not null && afterKind is not null && beforeKind != afterKind,
            _ => false
        };
        if (!consistent)
            throw new ArgumentException("Structural change kinds must agree with the before and after entry kinds.", nameof(kind));
        RelativePath = relativePath;
        Kind = kind;
        BeforeKind = beforeKind;
        AfterKind = afterKind;
    }

    public string RelativePath { get; }
    public RepositoryStructuralChangeKind Kind { get; }
    public ProductionRepositoryEntryKind? BeforeKind { get; }
    public ProductionRepositoryEntryKind? AfterKind { get; }
}
