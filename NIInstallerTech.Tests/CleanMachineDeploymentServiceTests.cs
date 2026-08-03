using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using NIInstallerTech.Services;
using Xunit;

namespace NIInstallerTech.Tests;

public sealed class CleanMachineDeploymentServiceTests
{
    [Fact]
    public void InstallAndUninstall_RequiresPocConsentAndRecordsOnlyOwnedFiles()
    {
        using var workspace = new TestWorkspace();
        var archivePath = CreateReferenceArchive(workspace.RootDirectory);
        var targetDirectory = Path.Combine(workspace.RootDirectory, "Program Files", "National Instruments", "LabVIEW 2026");
        var service = new CleanMachineDeploymentService(Path.Combine(workspace.RootDirectory, "state"));
        var log = new PrototypeOperationLog(Path.Combine(workspace.RootDirectory, "log"));
        var blockedRequest = CreateRequest(archivePath, targetDirectory, allowReferenceDerivedPoc: false);

        var blocked = service.Install(blockedRequest, log);

        Assert.False(blocked.IsSuccess);
        Assert.Contains("explicit", blocked.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(targetDirectory));

        var install = service.Install(CreateRequest(archivePath, targetDirectory, allowReferenceDerivedPoc: true), log);

        Assert.True(install.IsSuccess);
        Assert.Equal(2, install.ChangedFileCount);
        Assert.Equal("LabVIEW executable", File.ReadAllText(Path.Combine(targetDirectory, "LabVIEW.exe")));
        Assert.Equal("documentation", File.ReadAllText(Path.Combine(targetDirectory, "docs", "readme.txt")));
        Assert.False(Directory.Exists(Path.Combine(targetDirectory, "labview-application")));
        Assert.Contains("\"State\": \"installed\"", File.ReadAllText(service.LedgerPath));

        var uninstall = service.Uninstall("labview.application.2026-q3.x64", log);

        Assert.True(uninstall.IsSuccess);
        Assert.False(Directory.Exists(targetDirectory));
    }

    [Fact]
    public void Install_RefusesToOverwriteAnExistingMachineTarget()
    {
        using var workspace = new TestWorkspace();
        var archivePath = CreateReferenceArchive(workspace.RootDirectory);
        var targetDirectory = Path.Combine(workspace.RootDirectory, "Program Files", "National Instruments", "LabVIEW 2026");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "existing.txt"), "do not overwrite");
        var service = new CleanMachineDeploymentService(Path.Combine(workspace.RootDirectory, "state"));
        var log = new PrototypeOperationLog(Path.Combine(workspace.RootDirectory, "log"));

        var result = service.Install(CreateRequest(archivePath, targetDirectory, allowReferenceDerivedPoc: true), log);

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("do not overwrite", File.ReadAllText(Path.Combine(targetDirectory, "existing.txt")));
    }

    private static CleanMachineDeploymentRequest CreateRequest(string archivePath, string targetDirectory, bool allowReferenceDerivedPoc)
        => new(
            "labview.application.2026-q3.x64",
            "26.30.49792",
            archivePath,
            Digest(archivePath),
            targetDirectory,
            "labview-application",
            "LabVIEW.exe",
            allowReferenceDerivedPoc);

    private static string CreateReferenceArchive(string workspace)
    {
        var sourceDirectory = Path.Combine(workspace, "archive-source");
        var rootDirectory = Path.Combine(sourceDirectory, "26.30.49792");
        var payloadDirectory = Path.Combine(rootDirectory, "payload", "labview-application");
        var stagingDirectory = Path.Combine(rootDirectory, "staging");
        Directory.CreateDirectory(Path.Combine(payloadDirectory, "docs"));
        Directory.CreateDirectory(stagingDirectory);
        File.WriteAllText(Path.Combine(payloadDirectory, "LabVIEW.exe"), "LabVIEW executable");
        File.WriteAllText(Path.Combine(payloadDirectory, "docs", "readme.txt"), "documentation");

        var files = new[]
        {
            CreateFile("labview-application/LabVIEW.exe", Path.Combine(payloadDirectory, "LabVIEW.exe")),
            CreateFile("labview-application/docs/readme.txt", Path.Combine(payloadDirectory, "docs", "readme.txt"))
        };
        File.WriteAllText(Path.Combine(stagingDirectory, "manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = "reference-poc-manifest-v0.1",
            id = "labview.application.2026-q3.x64",
            version = "26.30.49792",
            classification = "reference-derived-poc",
            redistributable = false,
            payloadCopied = true,
            files = files.Length,
            bytes = files.Sum(file => file.SizeBytes)
        }));
        File.WriteAllText(Path.Combine(stagingDirectory, "payload-manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = "payload-manifest-v0.1",
            files
        }));

        var archivePath = Path.Combine(workspace, "labview-reference-poc.tar");
        TarFile.CreateFromDirectory(sourceDirectory, archivePath, includeBaseDirectory: false);
        return archivePath;
    }

    private static TestPayloadFile CreateFile(string destination, string path)
        => new(destination, Digest(path), new FileInfo(path).Length);

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record TestPayloadFile(string Destination, string Sha256, long SizeBytes);

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