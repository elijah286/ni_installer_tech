using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NIInstallerTech.Services;
using Xunit;

namespace NIInstallerTech.Tests;

public sealed class CandidateCatalogServiceTests
{
    [Fact]
    public async Task InspectAndUpsertAsync_CreatesEvidenceAndPreservesReviewAcrossRescan()
    {
        using var workspace = new TestWorkspace();
        var artifactPath = Path.Combine(workspace.RootDirectory, "legacy-installer.msi");
        await File.WriteAllTextAsync(artifactPath, "legacy installer evidence");
        var service = new CandidateCatalogService(Path.Combine(workspace.RootDirectory, "catalog"));

        var discovery = await service.InspectAndUpsertAsync(new CandidateIntakeRequest("NI Example", "ni.example", [artifactPath]));
        var expectedDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(artifactPath))).ToLowerInvariant();

        Assert.Equal("ni-example", discovery.Candidate.Id);
        Assert.Equal("awaiting-rd-review", discovery.Candidate.ReviewStatus);
        Assert.Single(discovery.Candidate.Evidence);
        Assert.Equal(expectedDigest, discovery.Candidate.Evidence[0].Sha256);
        Assert.Contains(discovery.Candidate.Warnings, warning => warning.Contains("MSI", StringComparison.Ordinal));

        await service.UpdateReviewAsync(discovery.Candidate.Id, new CandidateReviewUpdate("NI Example", "r-and-d-review", "native-transaction", "Driver resources excluded.", "Installer R&D"));
        var rescanned = await service.InspectAndUpsertAsync(new CandidateIntakeRequest("", "ni.example", [artifactPath]));
        var saved = (await service.LoadAsync()).Components.Single();

        Assert.Equal("r-and-d-review", rescanned.Candidate.ReviewStatus);
        Assert.Equal("native-transaction", saved.DeclaredInstallMode);
        Assert.Equal("Driver resources excluded.", saved.RAndDNotes);
        Assert.Equal("Installer R&D", saved.ReviewedBy);
    }

    [Fact]
    public async Task InspectAndUpsertAsync_ReadsNativePackageControlMetadata()
    {
        using var workspace = new TestWorkspace();
        var packagePath = Path.Combine(workspace.RootDirectory, "ni-example.nipkg");
        await File.WriteAllBytesAsync(packagePath, CreateNativePackage("Package: ni-example\nVersion: 1.2.3\nDepends: ni-runtime (>= 1.0), ni-common\n\n"));
        var service = new CandidateCatalogService(Path.Combine(workspace.RootDirectory, "catalog"));

        var discovery = await service.InspectAndUpsertAsync(new CandidateIntakeRequest("NI Example", "ni.example", [packagePath]));

        Assert.Equal("1.2.3", discovery.Candidate.ObservedVersion);
        Assert.Equal(["ni-example"], discovery.Candidate.LegacyPackageNames);
        Assert.Equal(["ni-common", "ni-runtime"], discovery.Candidate.Dependencies.OrderBy(value => value));
        Assert.Equal("ni-example", discovery.Candidate.Evidence.Single().PackageName);
    }

    private static byte[] CreateNativePackage(string controlContents)
    {
        byte[] controlTar;
        using (var tarStream = new MemoryStream())
        {
            using (var writer = new TarWriter(tarStream, TarEntryFormat.Pax, leaveOpen: true))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./control")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(controlContents))
                });
            }
            controlTar = tarStream.ToArray();
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var gzip = new GZipStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(controlTar);
            }
            compressed = compressedStream.ToArray();
        }

        using var archive = new MemoryStream();
        archive.Write(Encoding.ASCII.GetBytes("!<arch>\n"));
        var header = $"{"control.tar.gz/",-16}{0,-12}{0,-6}{0,-6}{644,-8}{compressed.Length,-10}`\n";
        archive.Write(Encoding.ASCII.GetBytes(header));
        archive.Write(compressed);
        if (compressed.Length % 2 != 0) archive.WriteByte((byte)'\n');
        return archive.ToArray();
    }

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