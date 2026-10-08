using System.Diagnostics;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

/// <summary>The test project remains net8.0. Only this subprocess rendering test needs the Windows desktop runtime.</summary>
public sealed class VisualExplorerWindowsFactAttribute : FactAttribute
{
    public VisualExplorerWindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "WPF rendering requires Windows; portable explorer tests run on Linux."; }
}
public sealed class VisualExplorerWindowsSmokeTests
{
    [VisualExplorerWindowsFact]
    public async Task RealCatalogFiltersDetailStatisticsAndThirtyThousandRecycledItemsRender()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        f.Publish(ArchiveTestFixture.NimroelAsset, automatic: true);
        f.Catalog.SaveObjective(new(f.Context.Id, "coverage", "Dos portraits", new(assetType: "portrait"), 2));
        var settings = new LocalUniverseSettingsStore(Path.Combine(f.Root, "local-settings.json")); settings.Save([f.Context.Storage]);
        var sources = f.Sources(); var database = File.ReadAllBytes(f.Catalog.CatalogPath);
        using var result = await Run(settings.SettingsPath, Environment.GetEnvironmentVariable("NAP_UI_SMOKE_OUTPUT"));
        Assert.Equal(1, result.RootElement.GetProperty("assets").GetInt32()); Assert.True(result.RootElement.GetProperty("documents").GetInt32() > 0);
        Assert.Equal("Ready", result.RootElement.GetProperty("state").GetString());
        Assert.Equal(20, result.RootElement.GetProperty("pages").GetInt32());
        Assert.Equal(0, result.RootElement.GetProperty("bindingErrors").GetInt32());
        Assert.Equal("restored", result.RootElement.GetProperty("tray").GetString());
        Assert.Equal("static", result.RootElement.GetProperty("selectorMode").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("mascotChecks").GetInt32());
        Assert.True(result.RootElement.GetProperty("mascotAnimationReleased").GetBoolean());
        Assert.True(result.RootElement.GetProperty("tooltipChecks").GetInt32() > 0);
        Assert.Equal(new[] { "1200x700", "2560x1600" }, result.RootElement.GetProperty("viewports").EnumerateArray().Select(v => v.GetString()));
        Assert.InRange(result.RootElement.GetProperty("realizedBefore").GetInt32(), 1, 200);
        Assert.InRange(result.RootElement.GetProperty("realizedAfter").GetInt32(), 1, 200);
        f.AssertSources(sources); Assert.Equal(database, File.ReadAllBytes(f.Catalog.CatalogPath));
    }
    [VisualExplorerWindowsFact]
    public async Task EmptyBootstrapAndVirtualizedPanelRenderWithoutCreatingSettings()
    { using var f = new CatalogTestFixture(); var path = Path.Combine(f.Root, "missing-settings.json"); using var result = await Run(path); Assert.Equal("Empty", result.RootElement.GetProperty("state").GetString()); Assert.Equal("static", result.RootElement.GetProperty("selectorMode").GetString()); Assert.Equal(2, result.RootElement.GetProperty("mascotChecks").GetInt32()); Assert.False(File.Exists(path)); }
    [VisualExplorerWindowsFact]
    public async Task ImportAddsSecondUniverseAndSwitchesSelectorWithoutCreatingRootsOrSettings()
    {
        using var f = new CatalogTestFixture(); var settings = Path.Combine(f.Root, "missing-settings.json"); var profile = Path.Combine(f.Root, "profile.json");
        File.WriteAllText(profile, "{\"schema_version\":1,\"universe_id\":\"terra\",\"display_name\":\"Terra\",\"classification_dimensions\":[],\"asset_rules\":[]}");
        using var result = await Run(settings, importSource: profile);
        Assert.True(result.RootElement.GetProperty("importedProfile").GetBoolean()); Assert.Equal(2, result.RootElement.GetProperty("universeCount").GetInt32());
        Assert.Equal(0, result.RootElement.GetProperty("bindingErrors").GetInt32()); Assert.False(File.Exists(settings));
        Assert.Equal(File.ReadAllBytes(profile), File.ReadAllBytes(Path.Combine(f.Root, "installed-profiles", "terra", "profile.json")));
    }
    private static async Task<JsonDocument> Run(string settings, string? outputDirectory = null, string? importSource = null)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var app = Path.Combine(root, "src/NAP.App/bin/Release/net8.0-windows/NAP.App.dll"); Assert.True(File.Exists(app), "Build NAP.sln Release before the Windows smoke tests.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(app); start.ArgumentList.Add("--smoke-test"); start.ArgumentList.Add("--settings=" + settings);
        start.ArgumentList.Add("--profiles-root=" + Path.Combine(Path.GetDirectoryName(settings)!, "installed-profiles"));
        if (importSource is not null) start.ArgumentList.Add("--smoke-import-profile=" + importSource);
        if (outputDirectory is not null) start.ArgumentList.Add("--smoke-output=" + outputDirectory);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var text = await output; var errorText = await errors;
        Assert.True(process.ExitCode == 0, "WPF smoke failed: " + text + errorText); Assert.DoesNotContain("BindingExpression path error", errorText);
        return JsonDocument.Parse(text.Trim());
    }
}
