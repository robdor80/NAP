namespace NAP.Core;

/// <summary>Dimension-level result, independent of human messages and value vocabularies.</summary>
public sealed class ClassificationValidationResult
{
    internal ClassificationValidationResult(IEnumerable<string> missingRequired, IEnumerable<string> notAllowed)
    {
        MissingRequired = Array.AsReadOnly(missingRequired.ToArray());
        NotAllowed = Array.AsReadOnly(notAllowed.ToArray());
    }

    public IReadOnlyList<string> MissingRequired { get; }
    public IReadOnlyList<string> NotAllowed { get; }
    public bool IsValid => MissingRequired.Count == 0 && NotAllowed.Count == 0;
}
