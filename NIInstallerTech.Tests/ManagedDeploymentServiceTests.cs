using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NIInstallerTech.Services;
using Xunit;

namespace NIInstallerTech.Tests;

public sealed class ManagedDeploymentServiceTests
{
    private const string CatalogPath = "/metadata/catalogs/prototype-managed-install-catalog-v0.1.json";
    private static readonly Uri RepositoryUri = new("http://repository.test/");

    [Fact]
    public async Task PreflightAsync_BlocksAndLogs_WhenCatalogIsMissing()
    {
        using var workspace = new TestWorkspace();
        using var client = new HttpClient(new RepositoryHandler(_ => Response(HttpStatusCode.NotFound)));
        var service = new ManagedDeploymentService(workspace.RootDirectory, client);
        var log = new PrototypeOperationLog(workspace.RootDirectory);

        var result = await service.PreflightAsync(RepositoryUri, ["max.configuration"], log);

        Assert.False(result.IsReady);
        Assert.Contains("No approved deployment catalog", result.Message);
        Assert.Contains("\"Outcome\":\"blocked\"", await File.ReadAllTextAsync(log.FilePath));
    }

    [Fact]
    public async Task PreflightAsync_Blocks_WhenAnInterruptedDeploymentIsStillOwned()
    {
        using var workspace = new TestWorkspace();
        var artifact = CreateZip(("payload/bin/component.txt", "managed payload"));
        var digest = Digest(artifact);
        using var client = CreateRepositoryClient(CreateCatalog(new CatalogComponent("max.configuration", "1.0.0", digest)), new Dictionary<string, byte[]> { [digest] = artifact });
        var service = new ManagedDeploymentService(workspace.RootDirectory, client);
        var log = new PrototypeOperationLog(workspace.RootDirectory);
        var interrupted = new ManagedInstalledComponent(
            "max.configuration",
            "MAX",
            "1.0.0",
            digest,
            Path.Combine(service.InstallRoot, "max.configuration", "1.0.0"),
            "interrupted-transaction",
            DateTimeOffset.UtcNow,
            "installing",
            null);
        await File.WriteAllTextAsync(service.LedgerPath, JsonSerializer.Serialize(new DeploymentLedger("ni-setup-managed-ledger-v0.1", [interrupted])));

        var result = await service.PreflightAsync(RepositoryUri, ["max.configuration"], log);

        Assert.False(result.IsReady);
        Assert.Contains("interrupted deployment", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallAndUninstallAsync_DeploysOnlyManagedPayloadAndRecordsOwnership()
    {
        using var workspace = new TestWorkspace();
        var artifact = CreateZip(("payload/bin/component.txt", "managed payload"));
        var digest = Digest(artifact);
        using var client = CreateRepositoryClient(CreateCatalog(new CatalogComponent("max.configuration", "1.0.0", digest)), new Dictionary<string, byte[]> { [digest] = artifact });
        var service = new ManagedDeploymentService(workspace.RootDirectory, client);
        var log = new PrototypeOperationLog(workspace.RootDirectory);

        var preflight = await service.PreflightAsync(RepositoryUri, ["max.configuration"], log);
        var install = await service.InstallAsync(preflight, log);
        var deployedFile = Path.Combine(service.InstallRoot, "max.configuration", "1.0.0", "bin", "component.txt");

        Assert.True(install.IsSuccess);
        Assert.Equal("managed payload", await File.ReadAllTextAsync(deployedFile));
        Assert.Equal(1, service.GetInstalledComponentCount());
        Assert.True(File.Exists(service.LedgerPath));

        var uninstall = await service.UninstallAllAsync(log);

        Assert.True(uninstall.IsSuccess);
        Assert.False(Directory.Exists(Path.Combine(service.InstallRoot, "max.configuration")));
        Assert.Equal(0, service.GetInstalledComponentCount());
    }

    [Fact]
    public async Task InstallAsync_RejectsTamperedArtifactWithoutCreatingComponentDirectory()
    {
        using var workspace = new TestWorkspace();
        var artifact = CreateZip(("payload/bin/component.txt", "tampered"));
        var expectedDigest = new string('a', 64);
        using var client = CreateRepositoryClient(CreateCatalog(new CatalogComponent("max.configuration", "1.0.0", expectedDigest)), new Dictionary<string, byte[]> { [expectedDigest] = artifact });
        var service = new ManagedDeploymentService(workspace.RootDirectory, client);
        var log = new PrototypeOperationLog(workspace.RootDirectory);

        var preflight = await service.PreflightAsync(RepositoryUri, ["max.configuration"], log);
        var result = await service.InstallAsync(preflight, log);

        Assert.False(result.IsSuccess);
        Assert.Contains("digest mismatch", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(service.InstallRoot, "max.configuration")));
        Assert.Equal(0, service.GetInstalledComponentCount());
    }

    [Fact]
    public async Task InstallAsync_RejectsPayloadPathTraversal()
    {
        using var workspace = new TestWorkspace();
        var artifact = CreateZip(("payload/../../outside.txt", "must not escape"));
        var digest = Digest(artifact);
        using var client = CreateRepositoryClient(CreateCatalog(new CatalogComponent("max.configuration", "1.0.0", digest)), new Dictionary<string, byte[]> { [digest] = artifact });
        var service = new ManagedDeploymentService(workspace.RootDirectory, client);
        var log = new PrototypeOperationLog(workspace.RootDirectory);

        var preflight = await service.PreflightAsync(RepositoryUri, ["max.configuration"], log);
        var result = await service.InstallAsync(preflight, log);

        Assert.False(result.IsSuccess);
        Assert.Contains("escaped", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(service.InstallRoot, "max.configuration")));
        Assert.Equal(0, service.GetInstalledComponentCount());
    }

    [Fact]
    public async Task InstallAsync_RollsBackEarlierComponent_WhenLaterArtifactFailsVerification()
    {
        using var workspace = new TestWorkspace();
        var firstArtifact = CreateZip(("payload/bin/first.txt", "first"));
        var firstDigest = Digest(firstArtifact);
        var secondArtifact = CreateZip(("payload/bin/second.txt", "tampered second"));
        var secondDigest = new string('b', 64);
        using var client = CreateRepositoryClient(
            CreateCatalog(
                new CatalogComponent("max.configuration", "1.0.0", firstDigest),
                new CatalogComponent("daqmx.runtime.user-mode", "1.0.0", secondDigest)),
            new Dictionary<string, byte[]>
            {
                [firstDigest] = firstArtifact,
                [secondDigest] = secondArtifact
            });
        var service = new ManagedDeploymentService(workspace.RootDirectory, client);
        var log = new PrototypeOperationLog(workspace.RootDirectory);

        var preflight = await service.PreflightAsync(RepositoryUri, ["max.configuration", "daqmx.runtime.user-mode"], log);
        var result = await service.InstallAsync(preflight, log);

        Assert.False(result.IsSuccess);
        Assert.False(Directory.Exists(Path.Combine(service.InstallRoot, "max.configuration")));
        Assert.Equal(0, service.GetInstalledComponentCount());
        Assert.Contains("\"Phase\":\"rollback\"", await File.ReadAllTextAsync(log.FilePath));
    }

    [Fact]
    public async Task UninstallAllAsync_RemovesInterruptedDeploymentAndTransactionStaging()
    {
        using var workspace = new TestWorkspace();
        using var client = new HttpClient(new RepositoryHandler(_ => Response(HttpStatusCode.NotFound)));
        var service = new ManagedDeploymentService(workspace.RootDirectory, client);
        var log = new PrototypeOperationLog(workspace.RootDirectory);
        var targetDirectory = Path.Combine(service.InstallRoot, "max.configuration", "1.0.0");
        var stagingDirectory = Path.Combine(service.StagingRoot, "interrupted-transaction");
        Directory.CreateDirectory(targetDirectory);
        Directory.CreateDirectory(stagingDirectory);
        await File.WriteAllTextAsync(Path.Combine(targetDirectory, "component.txt"), "owned payload");
        await File.WriteAllTextAsync(Path.Combine(stagingDirectory, "artifact.zip"), "owned staging data");
        var interrupted = new ManagedInstalledComponent(
            "max.configuration",
            "MAX",
            "1.0.0",
            new string('c', 64),
            targetDirectory,
            "interrupted-transaction",
            DateTimeOffset.UtcNow,
            "installing",
            null);
        await File.WriteAllTextAsync(service.LedgerPath, JsonSerializer.Serialize(new DeploymentLedger("ni-setup-managed-ledger-v0.1", [interrupted])));

        var result = await service.UninstallAllAsync(log);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(targetDirectory));
        Assert.False(Directory.Exists(service.StagingRoot));
        Assert.Equal(0, service.GetInstalledComponentCount());
    }

    private static HttpClient CreateRepositoryClient(byte[] catalog, IReadOnlyDictionary<string, byte[]> artifacts)
        => new(new RepositoryHandler(requestUri =>
        {
            if (requestUri.AbsolutePath == CatalogPath) return Response(HttpStatusCode.OK, catalog, "application/json");
            var digest = requestUri.Segments.LastOrDefault()?.Trim('/');
            return digest is not null && artifacts.TryGetValue(digest, out var artifact)
                ? Response(HttpStatusCode.OK, artifact, "application/octet-stream")
                : Response(HttpStatusCode.NotFound);
        }));

    private static byte[] CreateCatalog(params CatalogComponent[] components)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "ni-setup-managed-catalog-v0.1",
            components = components.Select(component => new
            {
                id = component.Id,
                displayName = component.Id,
                version = component.Version,
                artifactSha256 = component.Digest,
                installMode = "managed-file-copy",
                approvedForManagedPrototypeInstall = true
            })
        });

    private static byte[] CreateZip(params (string Path, string Contents)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, contents) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(contents);
            }
        }
        return stream.ToArray();
    }

    private static string Digest(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static HttpResponseMessage Response(HttpStatusCode statusCode, byte[]? content = null, string? mediaType = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (content is not null)
        {
            response.Content = new ByteArrayContent(content);
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType ?? "application/octet-stream");
        }
        return response;
    }

    private sealed record CatalogComponent(string Id, string Version, string Digest);

    private sealed class RepositoryHandler(Func<Uri, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request.RequestUri!));
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