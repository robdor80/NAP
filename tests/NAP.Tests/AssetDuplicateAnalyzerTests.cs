using System.Globalization;
using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetDuplicateAnalyzerTests
{
    private const string CandidateId = "portrait_candidate_001";
    private const string ExistingId = "portrait_existing_002";
    private const string CrossUniverseMessage = "The candidate and existing fingerprints must belong to the same universe.";
    private readonly AssetDuplicateAnalyzer _analyzer = new();

    [Fact]
    public void AnalyzerIsPublicSealedStatelessWithOnlyExactPairwiseApi()
    {
        var type = typeof(AssetDuplicateAnalyzer);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
        Assert.Empty(type.GetProperties());
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("Analyze", method.Name);
        Assert.False(method.IsStatic);
        Assert.Equal(typeof(AssetDuplicateAnalysis), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(new[] { "candidate", "existing" }, parameters.Select(p => p.Name));
        Assert.Equal(new[] { typeof(AssetContentFingerprint), typeof(AssetContentFingerprint) }, parameters.Select(p => p.ParameterType));
    }

    [Fact]
    public void ResultIsPublicSealedWithInternalConstructorAndExactGetOnlyProperties()
    {
        var type = typeof(AssetDuplicateAnalysis);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.True(ResultConstructor.IsAssembly);
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Candidate", "Existing", "IsCollision", "IsIdempotent", "IsPossibleDuplicate", "Issues", "Relation" },
            properties.Select(p => p.Name));
        Assert.Equal(new[] { typeof(AssetContentFingerprint), typeof(AssetContentFingerprint), typeof(bool), typeof(bool),
            typeof(bool), typeof(NapIssueReport), typeof(AssetContentRelation) }, properties.Select(p => p.PropertyType));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    [Theory]
    [InlineData(true, true, "candidate")]
    [InlineData(true, false, "candidate")]
    [InlineData(false, true, "existing")]
    public void NullValidationUsesExactParametersInCandidateThenExistingOrder(bool nullCandidate, bool nullExisting, string parameter)
    {
        var fingerprint = Fingerprint(CandidateId, 'a');
        Assert.Equal(parameter, Assert.Throws<ArgumentNullException>(() =>
            _analyzer.Analyze(nullCandidate ? null! : fingerprint, nullExisting ? null! : fingerprint)).ParamName);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void EveryCrossUniverseCombinationIsRejectedBeforeClassification(bool sameAssetId, bool sameDigest)
    {
        var candidate = Fingerprint(CandidateId, 'b');
        var existing = Fingerprint(sameAssetId ? CandidateId : ExistingId, sameDigest ? 'b' : 'a', "test_universe");
        var exception = Assert.Throws<ArgumentException>(() => _analyzer.Analyze(candidate, existing));
        Assert.Equal("existing", exception.ParamName);
        Assert.Equal(new ArgumentException(CrossUniverseMessage, "existing").Message, exception.Message);
        var reverse = Assert.Throws<ArgumentException>(() => _analyzer.Analyze(existing, candidate));
        Assert.Equal(exception.Message, reverse.Message);
        Assert.Equal("existing", reverse.ParamName);
    }

    public static IEnumerable<object[]> Matrix()
    {
        yield return [true, true, AssetContentRelation.SameAssetSameContent];
        yield return [true, false, AssetContentRelation.SameAssetDifferentContent];
        yield return [false, true, AssetContentRelation.DifferentAssetSameContent];
        yield return [false, false, AssetContentRelation.Distinct];
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void FullMatrixUsesValueEqualityAndExactDiagnostics(bool sameKey, bool sameDigest, AssetContentRelation expected)
    {
        var candidate = Fingerprint(CandidateId, 'b');
        var existing = Fingerprint(sameKey ? CandidateId : ExistingId, sameDigest ? 'b' : 'a');
        Assert.NotSame(candidate.AssetKey.UniverseId, existing.AssetKey.UniverseId);
        var result = _analyzer.Analyze(candidate, existing);
        Assert.Same(candidate, result.Candidate);
        Assert.Same(existing, result.Existing);
        AssertAnalysis(result, expected);
    }

    [Fact]
    public void CollisionDetailsPreserveAndReverseExistingThenCandidateDigestDirection()
    {
        var candidate = Fingerprint(CandidateId, 'b');
        var existing = Fingerprint(CandidateId, 'a');
        var forward = _analyzer.Analyze(candidate, existing);
        var reverse = _analyzer.Analyze(existing, candidate);
        AssertAnalysis(forward, AssetContentRelation.SameAssetDifferentContent);
        AssertAnalysis(reverse, AssetContentRelation.SameAssetDifferentContent);
        Assert.Equal($"universe=nimroel; asset_id={CandidateId}; existing_sha256={new string('a', 64)}; candidate_sha256={new string('b', 64)}",
            Assert.Single(forward.Issues.Issues).Detail);
        Assert.Equal($"universe=nimroel; asset_id={CandidateId}; existing_sha256={new string('b', 64)}; candidate_sha256={new string('a', 64)}",
            Assert.Single(reverse.Issues.Issues).Detail);
    }

    [Fact]
    public void PossibleDuplicateDetailsPreserveAndReverseCandidateThenExistingAssetDirection()
    {
        var candidate = Fingerprint(CandidateId, 'a');
        var existing = Fingerprint(ExistingId, 'a');
        var forward = _analyzer.Analyze(candidate, existing);
        var reverse = _analyzer.Analyze(existing, candidate);
        AssertAnalysis(forward, AssetContentRelation.DifferentAssetSameContent);
        AssertAnalysis(reverse, AssetContentRelation.DifferentAssetSameContent);
        Assert.Equal($"universe=nimroel; candidate_asset_id={CandidateId}; existing_asset_id={ExistingId}; sha256={new string('a', 64)}",
            Assert.Single(forward.Issues.Issues).Detail);
        Assert.Equal($"universe=nimroel; candidate_asset_id={ExistingId}; existing_asset_id={CandidateId}; sha256={new string('a', 64)}",
            Assert.Single(reverse.Issues.Issues).Detail);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void InternalConstructorRejectsUndefinedRelations(int relation)
    {
        var fingerprint = Fingerprint(CandidateId, 'a');
        Assert.Equal("relation", Assert.IsType<ArgumentOutOfRangeException>(ConstructorFailure(
            (AssetContentRelation)relation, fingerprint, fingerprint, new NapIssueReport([]))).ParamName);
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("existing")]
    [InlineData("issues")]
    public void InternalConstructorRejectsNullInputs(string parameter)
    {
        var fingerprint = Fingerprint(CandidateId, 'a');
        Assert.Equal(parameter, Assert.IsType<ArgumentNullException>(ConstructorFailure(
            AssetContentRelation.SameAssetSameContent, parameter == "candidate" ? null! : fingerprint,
            parameter == "existing" ? null! : fingerprint, parameter == "issues" ? null! : new NapIssueReport([]))).ParamName);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void InternalConstructorEnforcesSameUniverseForEveryRelation(bool sameKey, bool sameDigest, AssetContentRelation relation)
    {
        var candidate = Fingerprint(CandidateId, 'b');
        var existing = Fingerprint(sameKey ? CandidateId : ExistingId, sameDigest ? 'b' : 'a', "test_universe");
        var error = Assert.IsType<ArgumentException>(ConstructorFailure(relation, candidate, existing, new NapIssueReport([])));
        Assert.Equal("existing", error.ParamName);
        Assert.Equal(new ArgumentException(CrossUniverseMessage, "existing").Message, error.Message);
    }

    public static IEnumerable<object[]> IncoherentRelations()
    {
        foreach (var row in Matrix())
            foreach (var wrong in Enum.GetValues<AssetContentRelation>())
                if (wrong != (AssetContentRelation)row[2]) yield return [row[0], row[1], wrong];
    }

    [Theory]
    [MemberData(nameof(IncoherentRelations))]
    public void InternalConstructorRejectsEveryRelationThatDisagreesWithFingerprints(bool sameKey, bool sameDigest, AssetContentRelation relation)
    {
        var error = Assert.IsType<ArgumentException>(ConstructorFailure(relation, Fingerprint(CandidateId, 'b'),
            Fingerprint(sameKey ? CandidateId : ExistingId, sameDigest ? 'b' : 'a'), new NapIssueReport([])));
        Assert.Equal("relation", error.ParamName);
    }

    [Theory]
    [InlineData(AssetContentRelation.Distinct)]
    [InlineData(AssetContentRelation.SameAssetSameContent)]
    public void CleanRelationsRejectAnyIssueEvenInfoContinue(AssetContentRelation relation)
    {
        var candidate = Fingerprint(CandidateId, 'b');
        var existing = relation == AssetContentRelation.Distinct ? Fingerprint(ExistingId, 'a') : candidate;
        var report = new NapIssueReport([new NapIssue("informational", NapIssueSeverity.Info, NapIssueDisposition.Continue, "Info.")]);
        Assert.Equal("issues", Assert.IsType<ArgumentException>(ConstructorFailure(relation, candidate, existing, report)).ParamName);
    }

    public static IEnumerable<object[]> IncoherentReports()
    {
        foreach (var collision in new[] { true, false })
            foreach (var flaw in new[] { "empty", "multiple", "code", "severity", "disposition" })
                yield return [collision, flaw];
    }

    [Theory]
    [MemberData(nameof(IncoherentReports))]
    public void NoncleanRelationsRejectWrongCountCodeSeverityOrDisposition(bool collision, string flaw)
    {
        var candidate = Fingerprint(CandidateId, 'b');
        var existing = Fingerprint(collision ? CandidateId : ExistingId, collision ? 'a' : 'b');
        var valid = _analyzer.Analyze(candidate, existing);
        var issue = Assert.Single(valid.Issues.Issues);
        var altered = new NapIssue(flaw == "code" ? "wrong_code" : issue.Code,
            flaw == "severity" ? NapIssueSeverity.Info : issue.Severity,
            flaw == "disposition" ? collision ? NapIssueDisposition.Continue : NapIssueDisposition.Stop : issue.Disposition,
            issue.Message, issue.SubjectPath, issue.Detail);
        var report = new NapIssueReport(flaw == "empty" ? [] : flaw == "multiple" ? [issue, issue] : [altered]);
        Assert.Equal("issues", Assert.IsType<ArgumentException>(ConstructorFailure(valid.Relation, candidate, existing, report)).ParamName);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ValidInternalResultsRetainInputsAndImmutableReport(bool sameKey, bool sameDigest, AssetContentRelation relation)
    {
        var candidate = Fingerprint(CandidateId, 'b');
        var existing = Fingerprint(sameKey ? CandidateId : ExistingId, sameDigest ? 'b' : 'a');
        var report = _analyzer.Analyze(candidate, existing).Issues;
        var result = (AssetDuplicateAnalysis)ResultConstructor.Invoke([relation, candidate, existing, report]);
        Assert.Same(candidate, result.Candidate);
        Assert.Same(existing, result.Existing);
        Assert.Same(report, result.Issues);
        var list = Assert.IsAssignableFrom<IList<NapIssue>>(result.Issues.Issues);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(new NapIssue("test_issue", NapIssueSeverity.Info, NapIssueDisposition.Continue, "Test.")));
        AssertAnalysis(result, relation);
    }

    [Fact]
    public void RepeatedCallsRemainEquivalentAfterUnrelatedCallsAndCallerReferencesAreRetained()
    {
        var candidate = Fingerprint(CandidateId, 'b');
        var existing = Fingerprint(CandidateId, 'a');
        var first = _analyzer.Analyze(candidate, existing);
        _analyzer.Analyze(candidate, candidate);
        _analyzer.Analyze(candidate, Fingerprint(ExistingId, 'b'));
        Assert.Throws<ArgumentException>(() => _analyzer.Analyze(candidate, Fingerprint(ExistingId, 'a', "test_universe")));
        var repeated = _analyzer.Analyze(candidate, existing);
        var equivalent = new AssetDuplicateAnalyzer().Analyze(Fingerprint(CandidateId, 'b'), Fingerprint(CandidateId, 'a'));
        Assert.NotSame(first, repeated);
        Assert.Same(candidate, repeated.Candidate);
        Assert.Same(existing, repeated.Existing);
        Assert.Equal(first.Relation, repeated.Relation);
        Assert.Equal(first.Issues.Issues, repeated.Issues.Issues);
        Assert.Equal(first.Relation, equivalent.Relation);
        Assert.Equal(first.Issues.Issues, equivalent.Issues.Issues);
    }

    public static IEnumerable<object[]> CulturesAndMatrix()
    {
        foreach (var culture in new[] { "tr-TR", "ar-SA" })
            foreach (var row in Matrix()) yield return [culture, row[0], row[1], row[2]];
    }

    [Theory]
    [MemberData(nameof(CulturesAndMatrix))]
    public void RelationsAndExactDiagnosticsAreCultureIndependent(string culture, bool sameKey, bool sameDigest, AssetContentRelation relation)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            AssertAnalysis(_analyzer.Analyze(Fingerprint(CandidateId, 'b'),
                Fingerprint(sameKey ? CandidateId : ExistingId, sameDigest ? 'b' : 'a')), relation);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void RealSha256OfIndependentByteSourcesDrivesAllFourRelations(bool sameKey, bool sameBytes, AssetContentRelation relation)
    {
        byte[] candidateBytes = [0, 1, 2, 255];
        byte[] existingBytes = sameBytes ? [0, 1, 2, 255] : [0, 1, 3, 255];
        using var candidateStream = new MemoryStream(candidateBytes, writable: false);
        using var existingStream = new MemoryStream(existingBytes, writable: false);
        var hasher = new Sha256Hasher();
        var candidate = new AssetContentFingerprint(Fingerprint(CandidateId, 'a').AssetKey, hasher.Compute(candidateStream));
        var existing = new AssetContentFingerprint(Fingerprint(sameKey ? CandidateId : ExistingId, 'a').AssetKey, hasher.Compute(existingStream));
        Assert.Equal(sameBytes, candidate.Digest == existing.Digest);
        AssertAnalysis(_analyzer.Analyze(candidate, existing), relation);
        Assert.True(candidateStream.CanRead);
        Assert.True(existingStream.CanRead);
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, candidateBytes);
        Assert.Equal(sameBytes ? new byte[] { 0, 1, 2, 255 } : new byte[] { 0, 1, 3, 255 }, existingBytes);
    }

    [Fact]
    public void RealNimroelValidatedMasterDependsOnlyOnKeyAndDigestDespiteDifferentPackageMetadata()
    {
        using var nimroel = new PackageSemanticTestFixture();
        var package = Assert.IsType<ValidatedAssetPackage>(nimroel.ValidateReadOnly().Package);
        // Same universe and asset, but a separate in-memory test profile, source role and classification.
        var alternateProfile = new UniverseProfile(new UniverseId("nimroel"), "Controlled test profile", ["category"],
            [new UniverseAssetRule("portrait", "portrait_alternate", ["category"], ["category"],
                [new AssetPackageFileRule("source_image", "_source", ".png", true, "png_master")])]);
        using var alternate = new PackageSemanticTestFixture(alternateProfile, productionProfile: "portrait_alternate");
        var otherPackage = Assert.IsType<ValidatedAssetPackage>(alternate.ValidateReadOnly().Package);
        Assert.Equal(package.AssetKey, otherPackage.AssetKey);
        Assert.NotEqual(package.PackageRoot, otherPackage.PackageRoot);
        Assert.NotEqual(package.Manifest.ProductionProfile, otherPackage.Manifest.ProductionProfile);
        Assert.NotEqual(package.Manifest.Classification.Keys, otherPackage.Manifest.Classification.Keys);
        Assert.True(package.FilesByRole.ContainsKey("master"));
        Assert.True(otherPackage.FilesByRole.ContainsKey("source_image"));
        nimroel.AssertReadOnly(() => alternate.AssertReadOnly(() =>
        {
            var hasher = new Sha256Hasher();
            var fingerprint = new AssetContentFingerprint(package.AssetKey, hasher.Compute(package.FilesByRole["master"]));
            var other = new AssetContentFingerprint(otherPackage.AssetKey, hasher.Compute(otherPackage.FilesByRole["source_image"]));
            AssertAnalysis(_analyzer.Analyze(fingerprint, other), AssetContentRelation.SameAssetSameContent);
            var otherKey = new AssetContentFingerprint(Fingerprint(ExistingId, 'a').AssetKey, other.Digest);
            AssertAnalysis(_analyzer.Analyze(fingerprint, otherKey), AssetContentRelation.DifferentAssetSameContent);
        }));
    }

    private static AssetContentFingerprint Fingerprint(string assetId, char hash, string universe = "nimroel") =>
        new(new UniverseAssetKey(new UniverseId(universe), assetId), new Sha256Digest(new string(hash, 64)));

    private static ConstructorInfo ResultConstructor => Assert.Single(typeof(AssetDuplicateAnalysis)
        .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));

    private static Exception ConstructorFailure(AssetContentRelation relation, AssetContentFingerprint candidate,
        AssetContentFingerprint existing, NapIssueReport issues) =>
        Assert.Throws<TargetInvocationException>(() => ResultConstructor.Invoke([relation, candidate, existing, issues])).InnerException!;

    private static void AssertAnalysis(AssetDuplicateAnalysis result, AssetContentRelation expected)
    {
        Assert.Equal(expected, result.Relation);
        var collision = expected == AssetContentRelation.SameAssetDifferentContent;
        var duplicate = expected == AssetContentRelation.DifferentAssetSameContent;
        Assert.Equal(expected == AssetContentRelation.SameAssetSameContent, result.IsIdempotent);
        Assert.Equal(collision, result.IsCollision);
        Assert.Equal(duplicate, result.IsPossibleDuplicate);
        Assert.Equal(!collision && !duplicate, result.Issues.IsClean);
        Assert.Equal(collision, result.Issues.HasErrors);
        Assert.Equal(collision, result.Issues.ShouldStop);
        Assert.Equal(!collision, result.Issues.CanContinue);
        if (!collision && !duplicate)
        {
            Assert.Empty(result.Issues.Issues);
            return;
        }
        var issue = Assert.Single(result.Issues.Issues);
        Assert.Equal(collision ? "asset_content_collision" : "asset_content_possible_duplicate", issue.Code);
        Assert.Equal(collision ? NapIssueSeverity.Error : NapIssueSeverity.Warning, issue.Severity);
        Assert.Equal(collision ? NapIssueDisposition.Stop : NapIssueDisposition.Continue, issue.Disposition);
        Assert.Equal(collision ? "The asset key is already associated with different content." :
            "The same content is already associated with a different asset key.", issue.Message);
        Assert.Null(issue.SubjectPath);
        var universe = result.Candidate.AssetKey.UniverseId.Value;
        Assert.Equal(collision ?
            $"universe={universe}; asset_id={result.Candidate.AssetKey.AssetId}; existing_sha256={result.Existing.Digest.Hex}; candidate_sha256={result.Candidate.Digest.Hex}" :
            $"universe={universe}; candidate_asset_id={result.Candidate.AssetKey.AssetId}; existing_asset_id={result.Existing.AssetKey.AssetId}; sha256={result.Candidate.Digest.Hex}", issue.Detail);
    }
}
