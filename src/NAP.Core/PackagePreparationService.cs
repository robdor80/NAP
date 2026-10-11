namespace NAP.Core;

public sealed class PackagePreparationStoppedException(NapIssueReport issues) : InvalidOperationException("Package preparation stopped.")
{
    public NapIssueReport Issues { get; } = issues;
}
public sealed record PreparedAssetPlans(ValidatedAssetPackage Package, ProcessingPlan Processing,
    ArchiveMasterPlan Archive, NapIssueReport Validation);

/// <summary>Portable deterministic preparation. Plans read storage; this service never publishes assets.</summary>
public sealed class PackagePreparationService
{
    public const long MaxInputPixels = 64_000_000;
    public ValidatedAssetPackage Validate(UniverseContext context, string root, long maxInputPixels = MaxInputPixels,
        bool requireExactGeometry = true)
    {
        var result = new PackageSemanticValidator().Validate(root, context);
        Require(result.Issues);
        var package = result.Package ?? throw new InvalidDataException("Semantic validation has no package.");
        var conversion = new ImageConversionResolver().Resolve(package, maxInputPixels);
        if (conversion is not null)
        {
            var png = new PngMasterValidator().Validate(conversion.SourcePath);
            if ((long)png.ImageInfo!.Width * png.ImageInfo.Height > maxInputPixels)
                throw new PackagePreparationStoppedException(new([new("preparation_pixel_limit", NapIssueSeverity.Error,
                    NapIssueDisposition.Stop, "The input exceeds the preparation pixel budget.")]));
            if (requireExactGeometry) Require(new ImageConversionGeometryValidator().Validate(png.ImageInfo, conversion));
        }
        return package;
    }
    public PreparedAssetPlans Plan(UniverseContext context, ValidatedAssetPackage package)
    {
        var validatedRoot = new ProductionRepositoryValidator().Validate(context); Require(validatedRoot.Issues);
        var repository = validatedRoot.Repository ?? throw new InvalidDataException("Production repository unavailable.");
        var destination = new ProductionDestinationResolver().Resolve(package, repository);
        var processing = new ProcessingPlanBuilder().Build(package, repository, destination);
        var scan = new ProductionRepositoryScanner().Scan(repository); Require(scan.Issues);
        var issues = new ProcessingPlanValidator().Validate(processing, scan.Snapshot ?? throw new InvalidDataException("Production snapshot unavailable."));
        Require(issues);
        var archive = new ArchiveMasterPlanner(context).Plan(package, processing); Require(archive.Issues);
        return new(package, processing, archive, new(issues.Issues.Concat(archive.Issues.Issues)));
    }
    /// <summary>Manual adapter preserves its journal progression; durable preparation owns its separate checkpoints.</summary>
    public PreparedAssetPlans PrepareManual(UniverseContext context, JobId job, string root)
    {
        var package = Validate(context, root); var jobs = new JobStateStore(context);
        jobs.Transition(job, JobState.Validated);
        var result = Plan(context, package); jobs.Transition(job, JobState.Planned); return result;
    }
    private static void Require(NapIssueReport issues) { if (issues.ShouldStop) throw new PackagePreparationStoppedException(issues); }
}
