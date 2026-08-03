using NIInstallerTech.Services;
using Xunit;

namespace NIInstallerTech.Tests;

public sealed class RepackagingPreviewServiceTests
{
    [Fact]
    public void Analyze_MeasuresPackagesAndProjectsSmallerSingleComponent()
    {
        using var workspace = new TestWorkspace();
        var pathA = Path.Combine(workspace.RootDirectory, "ni-example-core.nipkg");
        var pathB = Path.Combine(workspace.RootDirectory, "ni-example-runtime.nipkg");
        File.WriteAllBytes(pathA, new byte[4000]);
        File.WriteAllBytes(pathB, new byte[6000]);

        var packages = new List<LegacyPackageOption>
        {
            new(workspace.RootDirectory, "ni-example-core", "26.3.0", pathA, string.Empty, ["ni-shared (>= 1.0)"], DateTimeOffset.UtcNow),
            new(workspace.RootDirectory, "ni-example-runtime", "26.3.0", pathB, string.Empty, ["ni-shared (>= 1.0)", "ni-extra"], DateTimeOffset.UtcNow),
        };
        var product = new LegacyProductGroup("ni-example", "Example 2026 Q3", "26.3", packages);

        var preview = RepackagingPreviewService.Analyze(product);

        Assert.Equal(2, preview.LegacyPackageCount);
        Assert.True(preview.LegacyFullyMeasured);
        Assert.Equal(10000, preview.LegacyMeasuredBytes);
        Assert.Equal(2, preview.UniqueDependencyCount);
        Assert.Equal(1, preview.SharedPrerequisiteCount);
        Assert.Equal(1, preview.ProjectedInstallableCount);
        Assert.True(preview.ProjectedEstimatedBytesLow < preview.ProjectedEstimatedBytesHigh);
        Assert.True(preview.ProjectedEstimatedBytesHigh < preview.LegacyMeasuredBytes);
        Assert.Contains(preview.Highlights, highlight => highlight.Contains("shared prerequisite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsCountsOnlyWhenPackageFilesAreMissing()
    {
        var packages = new List<LegacyPackageOption>
        {
            new("/missing", "ni-example-core", "26.3.0", "/missing/ni-example-core.nipkg", string.Empty, [], DateTimeOffset.UtcNow),
        };
        var product = new LegacyProductGroup("ni-example", "Example 2026 Q3", "26.3", packages);

        var preview = RepackagingPreviewService.Analyze(product);

        Assert.False(preview.HasMeasuredSize);
        Assert.Equal(0, preview.LegacyMeasuredBytes);
        Assert.False(preview.LegacyFullyMeasured);
        Assert.Equal(1, preview.LegacyPackageCount);
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "ni-repack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
        }

        public string RootDirectory { get; }

        public void Dispose()
        {
            try { Directory.Delete(RootDirectory, recursive: true); }
            catch (IOException) { }
        }
    }
}
