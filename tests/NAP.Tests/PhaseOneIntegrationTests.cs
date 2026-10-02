using System.IO.Compression;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class PhaseOneIntegrationTests
{
    [Fact]
    public async Task LegitimatePackage_CompletesPhaseOneWithoutChangingOriginals()
    {
        const string packageName = "portrait_treskal_farmer_male_001";
        var expectedNames = new[]
        {
            $"{packageName}.png",
            $"{packageName}_info.md",
            $"{packageName}_manifest.json",
            $"{packageName}_prompt.md",
            $"{packageName}_visual_identity.json"
        };
        var fixturePath = Path.Combine(AppContext.BaseDirectory,
            "test-data", "phase1", "first-valid-package", "payload");
        Assert.Equal(expectedNames, Directory.GetFiles(fixturePath)
            .Select(path => Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal));
        var fixtureBytes = expectedNames.ToDictionary(name => name,
            name => File.ReadAllBytes(Path.Combine(fixturePath, name)));

        var temporaryRoot = Directory.CreateTempSubdirectory("nap-phase1-integration-").FullName;
        try
        {
            var inbox = new InboxPreparer().Prepare(Path.Combine(temporaryRoot, "Inbox"));
            var staging = Directory.CreateDirectory(Path.Combine(temporaryRoot, "Staging")).FullName;
            var extraction = Directory.CreateDirectory(Path.Combine(temporaryRoot, "Extraction")).FullName;
            var unrelatedFiles = new Dictionary<string, byte[]>();
            foreach (var area in new[] { inbox, staging, extraction })
            {
                var unrelatedDirectory = Directory.CreateDirectory(Path.Combine(area, "unrelated")).FullName;
                foreach (var directory in new[] { area, unrelatedDirectory })
                {
                    var keep = Path.Combine(directory, "keep.txt");
                    File.WriteAllText(keep, $"Preserve unrelated content: {directory}");
                    unrelatedFiles.Add(keep, File.ReadAllBytes(keep));
                }
            }

            // Generate only in this test's private area; fixture sources are never written.
            var generatedZip = Path.Combine(temporaryRoot, $"{packageName}.zip");
            ZipFile.CreateFromDirectory(fixturePath, generatedZip);
            var expectedZipBytes = File.ReadAllBytes(generatedZip);
            var inboxZip = Path.Combine(inbox, $"{packageName}.zip");
            File.Copy(generatedZip, inboxZip, overwrite: false);

            var candidate = Assert.Single(new InboxPackageDetector().Detect(inbox));
            Assert.Equal($"{packageName}.zip", candidate.FileName);
            Assert.Equal(inboxZip, candidate.FullPath);
            AssertZipMatchesFixture(inboxZip, fixtureBytes);

            var readinessChecker = new InboxPackageReadinessChecker(new InboxPackageReadinessOptions
            {
                RequiredSamples = 2,
                SampleInterval = TimeSpan.Zero
            });
            var readiness = await readinessChecker.CheckAsync(candidate);
            Assert.Equal(InboxPackageReadinessStatus.Ready, readiness.Status);

            var staged = await new InboxPackageStager(readinessChecker).StageAsync(candidate, staging);
            Assert.Equal(InboxPackageStagingStatus.Staged, staged.Status);
            Assert.Equal(inboxZip, staged.SourcePath);
            Assert.Equal(Path.Combine(staging, $"{packageName}.zip"), staged.FinalStagedPath);
            Assert.True(File.Exists(inboxZip));
            Assert.True(File.Exists(staged.FinalStagedPath));
            Assert.Equal(expectedZipBytes, File.ReadAllBytes(inboxZip));
            Assert.Equal(expectedZipBytes, File.ReadAllBytes(staged.FinalStagedPath));
            AssertZipMatchesFixture(staged.FinalStagedPath, fixtureBytes);

            var extracted = await new StagedPackageExtractor().ExtractAsync(staged.FinalStagedPath, extraction);
            Assert.Equal(StagedPackageExtractionStatus.Extracted, extracted.Status);
            Assert.Equal(staged.FinalStagedPath, extracted.StagedZipPath);
            Assert.Equal(Path.Combine(extraction, packageName), extracted.FinalPath);
            Assert.True(Directory.Exists(extracted.FinalPath));
            Assert.Equal(expectedNames, Directory.GetFiles(extracted.FinalPath!, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(extracted.FinalPath!, path))
                .OrderBy(name => name, StringComparer.Ordinal));
            Assert.Empty(Directory.EnumerateDirectories(extracted.FinalPath!, "*", SearchOption.AllDirectories));
            foreach (var (name, expectedBytes) in fixtureBytes)
            {
                Assert.Equal(expectedBytes, File.ReadAllBytes(Path.Combine(extracted.FinalPath!, name)));
                Assert.Equal(expectedBytes, File.ReadAllBytes(Path.Combine(fixturePath, name)));
            }

            Assert.Equal(expectedZipBytes, File.ReadAllBytes(inboxZip));
            Assert.Equal(expectedZipBytes, File.ReadAllBytes(staged.FinalStagedPath));
            Assert.Empty(Directory.EnumerateFiles(temporaryRoot, "*.partial", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateDirectories(temporaryRoot, ".nap-*.extracting", SearchOption.AllDirectories));
            foreach (var (path, expectedBytes) in unrelatedFiles)
            {
                Assert.Equal(expectedBytes, File.ReadAllBytes(path));
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void AssertZipMatchesFixture(string zipPath, IReadOnlyDictionary<string, byte[]> fixture)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Equal(fixture.Keys.OrderBy(name => name, StringComparer.Ordinal),
            archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal));
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open();
            using var content = new MemoryStream();
            input.CopyTo(content);
            Assert.Equal(fixture[entry.FullName], content.ToArray());
        }
    }
}
