using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class PackageSemanticValidatorTests
{
    [Fact]
    public void ValidNimroelProducesAnImmutableValidatedPackageWithExactIdentityAndPaths()
    {
        using var fixture = new PackageSemanticTestFixture();
        var result = fixture.ValidateReadOnly(fixture.PackageRoot + Path.DirectorySeparatorChar);
        Assert.True(result.IsValid);
        var package = Assert.IsType<ValidatedAssetPackage>(result.Package);
        Assert.Equal(new UniverseAssetKey(new UniverseId("nimroel"), fixture.Manifest.AssetId), package.AssetKey);
        Assert.Equal(fixture.PackageRoot, package.PackageRoot);
        Assert.Equal(fixture.ManifestPath, package.ManifestPath);
        Assert.True(fixture.Context.Profile.TryGetAssetRule("portrait", "portrait_npc", out var rule));
        Assert.Same(rule, package.AssetRule);
        Assert.Equal(JsonSerializer.Serialize(fixture.Manifest), JsonSerializer.Serialize(package.Manifest));
        Assert.Equal(new[] { "master", "prompt", "info", "visual_identity" }, package.FilesByRole.Keys);
        Assert.All(rule!.PackageFiles, file => Assert.Equal(fixture.PathFor(file), package.FilesByRole[file.Role]));
        Assert.All(package.FilesByRole.Values, path => Assert.True(Path.IsPathFullyQualified(path)));
        Assert.False(package.FilesByRole.ContainsKey("manifest"));
        Assert.False(package.FilesByRole.ContainsKey("MASTER"));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)package.FilesByRole).Clear());
        fixture.Manifest.Classification.Clear();
        var exposed = package.Manifest;
        exposed.Classification["location"] = "changed";
        exposed.Classification.Clear();
        Assert.Equal(4, package.Manifest.Classification.Count);
        Assert.Equal("unregistered_value", package.Manifest.Classification["location"]);
        Assert.Empty(typeof(ValidatedAssetPackage).GetConstructors()); // External callers cannot forge one from a DTO.
        // The fixture PNG is 3:2: this boundary applies no Nimroel aspect ratio rule.
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("file")]
    public void InvalidRootIsControlledAndNeverCreated(string mode)
    {
        using var fixture = new PackageSemanticTestFixture();
        var path = mode == "file" ? Path.Combine(fixture.Root, "outside.txt") : Path.Combine(fixture.Root, "missing");
        var result = fixture.ValidateReadOnly(path);
        var issue = Only(result, NapIssueCodes.PackageRootInvalid);
        Assert.Equal(Path.GetFullPath(path), issue.SubjectPath);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "missing")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void InvalidArgumentsAreProgrammingErrors(string? path) =>
        Assert.ThrowsAny<ArgumentException>(() => new PackageSemanticValidator().Validate(path!, null!));

    [Fact]
    public void NullContextIsAProgrammingError()
    {
        using var fixture = new PackageSemanticTestFixture();
        fixture.AssertReadOnly(() => Assert.Throws<ArgumentNullException>(() => new PackageSemanticValidator().Validate(fixture.PackageRoot, null!)));
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("portrait_example_001_prompt.md")]
    public void SubdirectoriesAreRejectedWithoutDescending(string name)
    {
        using var fixture = new PackageSemanticTestFixture();
        var path = Path.Combine(fixture.PackageRoot, name);
        if (File.Exists(path)) File.Delete(path);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "bad_manifest.json"), "malformed JSON");
        Assert.Equal(path, Only(fixture.ValidateReadOnly(), NapIssueCodes.PackageStructureInvalid).SubjectPath);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong_suffix_case")]
    [InlineData("ambiguous")]
    public void ManifestDiscoveryUsesExactSuffixAndNeverChoosesArbitrarily(string mode)
    {
        using var fixture = new PackageSemanticTestFixture();
        switch (mode)
        {
            case "missing": File.Delete(fixture.ManifestPath); break;
            case "wrong_suffix_case": File.Move(fixture.ManifestPath, Path.Combine(fixture.PackageRoot, "different_MANIFEST.json")); break;
            case "ambiguous": File.WriteAllText(Path.Combine(fixture.PackageRoot, "other_manifest.json"), "invalid JSON"); break;
        }
        Only(fixture.ValidateReadOnly(), mode == "ambiguous" ? NapIssueCodes.PackageManifestAmbiguous : NapIssueCodes.PackageManifestMissing);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("malformed")]
    [InlineData("type_mismatch")]
    [InlineData("bad_classification")]
    public void InvalidManifestStopsBeforeProfileRules(string mode)
    {
        using var fixture = new PackageSemanticTestFixture();
        var json = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
        switch (mode)
        {
            case "v1": json["schema_version"] = 1; json.Remove("universe_id"); break;
            case "type_mismatch": json["asset_type"] = "scene"; break;
            case "bad_classification": json["classification"]!["location"] = " Treskal "; break;
        }
        File.WriteAllText(fixture.ManifestPath, mode == "malformed" ? "{\"private_marker\":" : json.ToJsonString());
        File.Delete(Path.Combine(fixture.PackageRoot, fixture.Manifest.AssetId + "_prompt.md"));
        var issue = Only(fixture.ValidateReadOnly(), NapIssueCodes.PackageManifestInvalid);
        Assert.Equal(fixture.ManifestPath, issue.SubjectPath);
        Assert.NotNull(issue.Detail);
        Assert.DoesNotContain("private_marker", issue.Detail);
        Assert.DoesNotContain("Treskal", issue.Detail);
    }

    [Theory]
    [InlineData("different_manifest.json")]
    [InlineData("Portrait_example_001_manifest.json")]
    public void NonCanonicalManifestFilenameIsNotRenamed(string name)
    {
        using var fixture = new PackageSemanticTestFixture();
        var path = Path.Combine(fixture.PackageRoot, name);
        var intermediate = Path.Combine(fixture.PackageRoot, "manifest.pending");
        File.Move(fixture.ManifestPath, intermediate);
        File.Move(intermediate, path);
        var result = fixture.ValidateReadOnly();
        Assert.Equal(new[] { NapIssueCodes.PackageManifestFilenameMismatch, NapIssueCodes.PackageUnexpectedFile }, Codes(result));
        Assert.Equal(path, result.Issues.Issues[0].SubjectPath);
    }

    [Theory]
    [InlineData("different_package")]
    [InlineData("Portrait_example_001")]
    public void RootNameMustMatchAssetIdOrdinally(string name)
    {
        using var fixture = new PackageSemanticTestFixture();
        // A distinct intermediate spelling makes a case-only rename reliable on Windows.
        fixture.RenameRoot("intermediate");
        fixture.RenameRoot(name);
        Assert.Equal(fixture.PackageRoot, Only(fixture.ValidateReadOnly(), NapIssueCodes.PackageRootNameMismatch).SubjectPath);
    }

    [Fact]
    public void UniverseMismatchDoesNotApplyTheActiveProfile()
    {
        using var fixture = new PackageSemanticTestFixture();
        var profile = new UniverseProfile(new UniverseId("test_universe"), "Test", ["dimension"],
            [new UniverseAssetRule("portrait", "portrait_npc", ["dimension"], ["dimension"],
                [new AssetPackageFileRule("audio", "_audio", ".wav", true, "future_validator")])]);
        var context = new UniverseContext(profile, new UniverseStorageConfig(profile.Id,
            Path.Combine(fixture.Root, "other_workspace"), Path.Combine(fixture.Root, "other_production"), Path.Combine(fixture.Root, "other_archive")));
        Only(fixture.ValidateReadOnly(context: context), NapIssueCodes.PackageUniverseMismatch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownTypeOrProductionProfileHasNoFallback(bool type)
    {
        var empty = new UniverseProfile(new UniverseId("nimroel"), "Nimroel");
        using var fixture = type
            ? new PackageSemanticTestFixture(empty, "future_type_example_001", "future_type", "future_profile")
            : new PackageSemanticTestFixture(productionProfile: "future_profile");
        Only(fixture.ValidateReadOnly(), NapIssueCodes.PackageRuleNotFound);
    }

    [Theory]
    [InlineData("optional_dimensions")]
    [InlineData("missing_location")]
    [InlineData("extra_faction")]
    public void ClassificationChecksDimensionsWithoutVocabularies(string mode)
    {
        using var fixture = new PackageSemanticTestFixture();
        switch (mode)
        {
            case "optional_dimensions": fixture.Manifest.Classification["realm"] = "not_in_any_vocabulary"; fixture.Manifest.Classification["region"] = "another_unregistered_value"; break;
            case "missing_location": fixture.Manifest.Classification.Remove("location"); break;
            case "extra_faction": fixture.Manifest.Classification["faction"] = "private_value"; break;
        }
        fixture.WriteManifest();
        var result = fixture.ValidateReadOnly();
        if (mode == "optional_dimensions") Assert.True(result.IsValid);
        else
        {
            var detail = Only(result, NapIssueCodes.PackageClassificationInvalid).Detail;
            Assert.Contains(mode == "missing_location" ? "location" : "faction", detail);
            Assert.DoesNotContain("private_value", detail);
            Assert.DoesNotContain("unregistered_value", detail);
        }
    }

    [Theory]
    [InlineData("master")]
    [InlineData("prompt")]
    [InlineData("info")]
    [InlineData("visual_identity")]
    public void EveryNimroelRequiredFileMustBePresent(string role)
    {
        using var fixture = new PackageSemanticTestFixture();
        var file = fixture.Context.Profile.AssetRules[0].PackageFiles.Single(file => file.Role == role);
        File.Delete(fixture.PathFor(file));
        var issue = Only(fixture.ValidateReadOnly(), NapIssueCodes.PackageRequiredFileMissing);
        Assert.Equal(fixture.PathFor(file), issue.SubjectPath);
        Assert.Equal(role, issue.Detail);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("thumb.jpg")]
    [InlineData("old.png")]
    [InlineData("portrait_example_001.webp")]
    [InlineData("portrait_other_001.png")]
    public void AllUnexpectedFilesIncludingProductionWebPAreRejected(string name)
    {
        using var fixture = new PackageSemanticTestFixture();
        var path = Path.Combine(fixture.PackageRoot, name);
        File.WriteAllText(path, "extra");
        Assert.Equal(path, Only(fixture.ValidateReadOnly(), NapIssueCodes.PackageUnexpectedFile).SubjectPath);
    }

    [Fact]
    public void IncorrectFileCasingProducesMissingAndUnexpectedEvenOnWindows()
    {
        using var fixture = new PackageSemanticTestFixture();
        var correct = Path.Combine(fixture.PackageRoot, fixture.Manifest.AssetId + "_prompt.md");
        var actual = Path.Combine(fixture.PackageRoot, fixture.Manifest.AssetId + "_PROMPT.md");
        File.Move(correct, actual);
        var result = fixture.ValidateReadOnly();
        Assert.Equal(new[] { NapIssueCodes.PackageRequiredFileMissing, NapIssueCodes.PackageUnexpectedFile }, Codes(result));
        Assert.Equal(correct, result.Issues.Issues[0].SubjectPath);
        Assert.Equal(actual, result.Issues.Issues[1].SubjectPath);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("truncated")]
    [InlineData("unsupported")]
    public void PngIssuesPreserveExistingMapperCodeAndConcretePath(string mode)
    {
        using var fixture = new PackageSemanticTestFixture();
        var path = Path.Combine(fixture.PackageRoot, fixture.Manifest.AssetId + ".png");
        var bytes = PackageSemanticTestFixture.Png(mode == "unsupported");
        if (mode == "signature") bytes[0] = 0;
        if (mode == "truncated") bytes = bytes[..^1];
        File.WriteAllBytes(path, bytes);
        var expected = NapIssueMapper.Map(new PngMasterValidator().Validate(path), path)!;
        var actual = Only(fixture.ValidateReadOnly(), mode == "unsupported" ? NapIssueCodes.PngUnsupportedFeature : NapIssueCodes.PngInvalid);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void FutureValidatorIsRejectedOnlyWhenItsFileIsPresent(bool required, bool present)
    {
        var profile = GenericProfile(new AssetPackageFileRule("metadata", "_metadata", ".json", required, "future_validator"));
        using var fixture = new PackageSemanticTestFixture(profile);
        var path = fixture.PathFor(profile.AssetRules[0].PackageFiles[0]);
        if (!present) File.Delete(path);
        var result = fixture.ValidateReadOnly();
        if (present)
            Assert.Equal("future_validator", Only(result, NapIssueCodes.PackageContentValidatorUnsupported).Detail);
        else if (required) Only(result, NapIssueCodes.PackageRequiredFileMissing);
        else { Assert.True(result.IsValid); Assert.Empty(result.Package!.FilesByRole); }
    }

    [Fact]
    public void OptionalPresentFilesAreIncludedAndNullValidatorDoesNotInspectCompanionContent()
    {
        var profile = GenericProfile(new AssetPackageFileRule("metadata", "_metadata", ".json", false));
        using var fixture = new PackageSemanticTestFixture(profile);
        var result = fixture.ValidateReadOnly();
        Assert.True(result.IsValid);
        Assert.Equal(fixture.PathFor(profile.AssetRules[0].PackageFiles[0]), Assert.Single(result.Package!.FilesByRole).Value);
    }

    [Fact]
    public void GenericAudioRuleDoesNotRequirePngOrNimroelCompanions()
    {
        var profile = new UniverseProfile(new UniverseId("test_universe"), "Test", [],
            [new UniverseAssetRule("audio", "audio_source", [], [], [new AssetPackageFileRule("audio_master", "", ".wav", true)])]);
        using var fixture = new PackageSemanticTestFixture(profile, "audio_example_001", "audio", "audio_source");
        var result = fixture.ValidateReadOnly();
        Assert.True(result.IsValid);
        Assert.Equal("audio_master", Assert.Single(result.Package!.FilesByRole).Key);
    }

    [Fact]
    public void HistoricalProfileV1CanValidateAManifestOnlyRuleWithoutInventingFiles()
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.HistoricalConfigPath);
        using var fixture = new PackageSemanticTestFixture(profile);
        var result = fixture.ValidateReadOnly();
        Assert.True(result.IsValid);
        Assert.Empty(result.Package!.FilesByRole);
    }

    [Fact]
    public void IndependentIssuesAreCollectedInDocumentedDeterministicOrder()
    {
        using var fixture = new PackageSemanticTestFixture();
        fixture.Manifest.Classification.Remove("location");
        fixture.Manifest.Classification["z_extra"] = "secret_value";
        fixture.Manifest.Classification["a_extra"] = "secret_value";
        fixture.WriteManifest();
        var files = fixture.Context.Profile.AssetRules[0].PackageFiles;
        File.Delete(fixture.PathFor(files[1])); File.Delete(fixture.PathFor(files[2]));
        File.WriteAllText(Path.Combine(fixture.PackageRoot, "z-extra.txt"), "extra");
        File.WriteAllText(Path.Combine(fixture.PackageRoot, "a-extra.txt"), "extra");
        File.WriteAllBytes(fixture.PathFor(files[0]), [0]);
        var first = fixture.ValidateReadOnly();
        var second = fixture.ValidateReadOnly();
        Assert.Equal(first.Issues.Issues, second.Issues.Issues);
        Assert.Equal(new[] { NapIssueCodes.PackageClassificationInvalid, NapIssueCodes.PackageRequiredFileMissing,
            NapIssueCodes.PackageRequiredFileMissing, NapIssueCodes.PackageUnexpectedFile, NapIssueCodes.PackageUnexpectedFile,
            NapIssueCodes.PngInvalid }, Codes(first));
        Assert.Equal("prompt", first.Issues.Issues[1].Detail);
        Assert.Equal("info", first.Issues.Issues[2].Detail);
        Assert.EndsWith("a-extra.txt", first.Issues.Issues[3].SubjectPath);
        Assert.EndsWith("z-extra.txt", first.Issues.Issues[4].SubjectPath);
        Assert.Contains("a_extra, z_extra", first.Issues.Issues[0].Detail);
        Assert.DoesNotContain("secret_value", first.Issues.Issues[0].Detail);
    }

    [Fact]
    public void ResultConstructorEnforcesCleanReportAndPackageTogether()
    {
        using var fixture = new PackageSemanticTestFixture();
        var package = fixture.ValidateReadOnly().Package!;
        var failure = new NapIssueReport([new NapIssue(NapIssueCodes.PackageRootInvalid, NapIssueSeverity.Error, NapIssueDisposition.Stop, "Failure.")]);
        Assert.Throws<ArgumentNullException>(() => new PackageSemanticValidationResult(null!, null));
        Assert.Throws<ArgumentException>(() => new PackageSemanticValidationResult(new NapIssueReport([]), null));
        Assert.Throws<ArgumentException>(() => new PackageSemanticValidationResult(failure, package));
        Assert.False(new PackageSemanticValidationResult(failure, null).IsValid);
        Assert.True(new PackageSemanticValidationResult(new NapIssueReport([]), package).IsValid);
        var advisory = new NapIssueReport([new NapIssue("advisory", NapIssueSeverity.Info, NapIssueDisposition.Continue, "Advisory.")]);
        Assert.Throws<ArgumentException>(() => new PackageSemanticValidationResult(advisory, package));
    }

    [Fact]
    public void WindowsSharingErrorsPropagateWithoutBecomingPackageIssues()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new PackageSemanticTestFixture();
        foreach (var path in new[] { fixture.ManifestPath, Path.Combine(fixture.PackageRoot, fixture.Manifest.AssetId + ".png") })
        {
            fixture.AssertReadOnly(() =>
            {
                using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                Assert.Throws<IOException>(() => new PackageSemanticValidator().Validate(fixture.PackageRoot, fixture.Context));
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryLinksAreRejectedWithoutFollowingTheirTargets(bool rootLink)
    {
        using var fixture = new PackageSemanticTestFixture();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "bad_manifest.json"), "must not be read");
        var link = Path.Combine(rootLink ? fixture.Root : fixture.PackageRoot, "junction");
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe", Arguments = $"/c mklink /J \"{link}\" \"{target}\"", UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        try { Only(fixture.ValidateReadOnly(rootLink ? link : null), NapIssueCodes.PackageStructureInvalid); }
        finally { Directory.Delete(link); } // Remove the link itself, never recurse into its target.
    }

    [Fact]
    public void UnixFileLinkCannotBeReadAsARequiredMaster()
    {
        if (OperatingSystem.IsWindows()) return; // Windows file symlinks require privileges; junctions above cover its reparse handling.
        using var fixture = new PackageSemanticTestFixture();
        var target = Path.Combine(fixture.Root, "outside.png");
        File.WriteAllBytes(target, PackageSemanticTestFixture.Png());
        var link = Path.Combine(fixture.PackageRoot, fixture.Manifest.AssetId + ".png");
        File.Delete(link);
        File.CreateSymbolicLink(link, target);
        try { Assert.Equal(link, Only(fixture.ValidateReadOnly(), NapIssueCodes.PackageStructureInvalid).SubjectPath); }
        finally { File.Delete(link); }
    }

    private static UniverseProfile GenericProfile(params AssetPackageFileRule[] files) => new(new UniverseId("test_universe"), "Test", [],
        [new UniverseAssetRule("portrait", "portrait_npc", [], [], files)]);
    private static string[] Codes(PackageSemanticValidationResult result) => result.Issues.Issues.Select(issue => issue.Code).ToArray();
    private static NapIssue Only(PackageSemanticValidationResult result, string code)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Package);
        var issue = Assert.Single(result.Issues.Issues);
        Assert.Equal(code, issue.Code);
        return issue;
    }
}
