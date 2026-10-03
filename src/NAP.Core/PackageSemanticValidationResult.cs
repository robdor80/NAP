namespace NAP.Core;

/// <summary>A package exists exactly when the issue report is clean.</summary>
public sealed class PackageSemanticValidationResult
{
    public PackageSemanticValidationResult(NapIssueReport issues, ValidatedAssetPackage? package)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.IsClean != (package is not null))
            throw new ArgumentException("A validated package requires a clean report, and a clean report requires a package.", nameof(package));
        Issues = issues;
        Package = package;
    }

    public NapIssueReport Issues { get; }
    public ValidatedAssetPackage? Package { get; }
    public bool IsValid => Package is not null;
}
