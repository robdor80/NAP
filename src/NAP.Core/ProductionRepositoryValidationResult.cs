namespace NAP.Core;

/// <summary>A repository exists exactly when the issue report is clean.</summary>
public sealed class ProductionRepositoryValidationResult
{
    public ProductionRepositoryValidationResult(NapIssueReport issues, ValidatedProductionRepository? repository)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.IsClean != (repository is not null))
            throw new ArgumentException("A validated repository requires a clean report, and a clean report requires a repository.", nameof(repository));
        Issues = issues;
        Repository = repository;
    }

    public NapIssueReport Issues { get; }
    public ValidatedProductionRepository? Repository { get; }
    public bool IsValid => Repository is not null;
}
