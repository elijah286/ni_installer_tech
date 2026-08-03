using System.Security.Cryptography;
using NIInstallerTech.Services;
using Xunit;

namespace NIInstallerTech.Tests;

public sealed class CleanMachinePackageServiceTests
{
    [Fact]
    public async Task StageFromFileAsync_CopiesAndVerifiesTheSelectedPackage()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = Path.Combine(workspace.RootDirectory, "source.tar");
        var bytes = "verified package"u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, bytes);
        var service = new CleanMachinePackageService(Path.Combine(workspace.RootDirectory, "downloads"));
        var package = CreatePackage("selected.tar", Digest(bytes));

        var result = await service.StageFromFileAsync(package, sourcePath);

        Assert.Equal(Path.Combine(service.DownloadDirectory, "selected.tar"), result.ArchivePath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.ArchivePath));
    }

    [Fact]
    public async Task StageFromFileAsync_RejectsTamperedPackageWithoutLeavingStagedBytes()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = Path.Combine(workspace.RootDirectory, "source.tar");
        await File.WriteAllTextAsync(sourcePath, "tampered package");
        var service = new CleanMachinePackageService(Path.Combine(workspace.RootDirectory, "downloads"));
        var package = CreatePackage("selected.tar", new string('a', 64));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.StageFromFileAsync(package, sourcePath));

        Assert.Contains("digest mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(service.DownloadDirectory, "selected.tar")));
        Assert.Empty(Directory.EnumerateFiles(service.DownloadDirectory, "*.partial-*"));
    }

    private static CleanMachinePackage CreatePackage(string archiveFileName, string digest)
        => new("labview.application.2026-q3.x64", "26.30.49792", archiveFileName, digest, "incoming-reference-captures/package.tar", "labview-application", "LabVIEW.exe");

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "NIInstallerTech.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
        }

        public string RootDirectory { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}