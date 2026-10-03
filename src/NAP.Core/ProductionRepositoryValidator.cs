namespace NAP.Core;

/// <summary>Reads only the authorized root's attributes; never enumerates entries or writes to storage.</summary>
public sealed class ProductionRepositoryValidator
{
    public ProductionRepositoryValidationResult Validate(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = context.Storage.ProductionRoot;
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(root);
        }
        catch (FileNotFoundException)
        {
            return Failure(NapIssueCodes.ProductionRootMissing, "The production root does not exist.", root);
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(NapIssueCodes.ProductionRootMissing, "The production root does not exist.", root);
        }

        if ((attributes & FileAttributes.Directory) == 0)
            return Failure(NapIssueCodes.ProductionRootInvalid, "The production root must be a directory.", root);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return Failure(NapIssueCodes.ProductionRootReparse, "The production root must not be a reparse point.", root);

        return new ProductionRepositoryValidationResult(new NapIssueReport([]), new ValidatedProductionRepository(context));
    }

    private static ProductionRepositoryValidationResult Failure(string code, string message, string root) =>
        new(new NapIssueReport([new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, root)]), null);
}
