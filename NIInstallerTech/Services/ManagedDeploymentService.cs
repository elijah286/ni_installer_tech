using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NIInstallerTech.Services;

public sealed class ManagedDeploymentService
{
    private const string CatalogFileName = "prototype-managed-install-catalog-v0.1.json";
    private const string LedgerSchemaVersion = "ni-setup-managed-ledger-v0.1";
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string _rootDirectory;
    private readonly HttpClient _client;

    public ManagedDeploymentService(string? rootDirectory = null, HttpClient? client = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NISetupPrototype");
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public string RootDirectory => _rootDirectory;
    public string LedgerPath => Path.Combine(RootDirectory, "ledger.json");
    public string InstallRoot => Path.Combine(RootDirectory, "components");
    public string StagingRoot => Path.Combine(RootDirectory, "staging");

    public async Task<DeploymentPreflightResult> PreflightAsync(Uri repositoryUri, IReadOnlyCollection<string> componentIds, PrototypeOperationLog log, CancellationToken cancellationToken = default)
    {
        var catalogUri = new Uri(repositoryUri, $"metadata/catalogs/{CatalogFileName}");
        log.Write("preflight", "started", "Reading deployment catalog.", new { catalogUri, componentIds });

        try
        {
            using var response = await _client.GetAsync(catalogUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? $"No approved deployment catalog was found at {catalogUri.AbsolutePath}."
                    : $"The deployment catalog request returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
                log.Write("preflight", "blocked", detail);
                return DeploymentPreflightResult.Blocked(detail, catalogUri);
            }

            await using var catalogStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var catalog = await JsonSerializer.DeserializeAsync<ManagedDeploymentCatalog>(catalogStream, SerializerOptions, cancellationToken);
            if (catalog is null || !string.Equals(catalog.SchemaVersion, "ni-setup-managed-catalog-v0.1", StringComparison.Ordinal))
            {
                const string detail = "The deployment catalog schema is missing or not supported.";
                log.Write("preflight", "blocked", detail);
                return DeploymentPreflightResult.Blocked(detail, catalogUri);
            }

            var available = catalog.Components
                .Where(component => component.ApprovedForManagedPrototypeInstall && component.InstallMode == "managed-file-copy")
                .ToDictionary(component => component.Id, StringComparer.OrdinalIgnoreCase);
            var missing = componentIds.Where(componentId => !available.ContainsKey(componentId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0)
            {
                var detail = $"The approved catalog does not contain every selected component: {string.Join(", ", missing)}.";
                log.Write("preflight", "blocked", detail, new { missing });
                return DeploymentPreflightResult.Blocked(detail, catalogUri);
            }

            var selected = componentIds.Select(componentId => available[componentId]).ToArray();
            var invalid = selected.FirstOrDefault(component => !IsSha256(component.ArtifactSha256) || string.IsNullOrWhiteSpace(component.Version));
            if (invalid is not null)
            {
                var detail = $"Catalog component '{invalid.Id}' does not contain a valid immutable artifact digest and version.";
                log.Write("preflight", "blocked", detail);
                return DeploymentPreflightResult.Blocked(detail, catalogUri);
            }

            var ledger = LoadLedger();
            var interrupted = selected
                .Where(component => ledger.Components.Any(installed =>
                    string.Equals(installed.ComponentId, component.Id, StringComparison.OrdinalIgnoreCase) &&
                    installed.State is not ("installed" or "removed")))
                .Select(component => component.Id)
                .ToArray();
            if (interrupted.Length > 0)
            {
                var detail = $"An interrupted deployment is recorded for: {string.Join(", ", interrupted)}. Run managed prototype uninstall before installing again.";
                log.Write("preflight", "blocked", detail, new { interrupted });
                return DeploymentPreflightResult.Blocked(detail, catalogUri);
            }
            var alreadyInstalled = selected
                .Where(component => ledger.Components.Any(installed =>
                    string.Equals(installed.ComponentId, component.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(installed.ArtifactSha256, component.ArtifactSha256, StringComparison.OrdinalIgnoreCase) &&
                    installed.State == "installed"))
                .Select(component => component.Id)
                .ToArray();
            var deployable = selected.Where(component => !alreadyInstalled.Contains(component.Id, StringComparer.OrdinalIgnoreCase)).ToArray();

            log.Write("preflight", "ready", "The selected managed deployment is eligible to run.", new { deployable = deployable.Select(component => component.Id), alreadyInstalled });
            return DeploymentPreflightResult.Ready(catalogUri, deployable, alreadyInstalled);
        }
        catch (HttpRequestException exception)
        {
            var detail = $"The deployment catalog could not be retrieved: {exception.Message}";
            log.Write("preflight", "failed", detail);
            return DeploymentPreflightResult.Blocked(detail, catalogUri);
        }
        catch (JsonException exception)
        {
            var detail = $"The deployment catalog is invalid JSON: {exception.Message}";
            log.Write("preflight", "blocked", detail);
            return DeploymentPreflightResult.Blocked(detail, catalogUri);
        }
    }

    public async Task<DeploymentExecutionResult> InstallAsync(DeploymentPreflightResult preflight, PrototypeOperationLog log, IProgress<DeploymentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!preflight.IsReady)
        {
            return DeploymentExecutionResult.Failed("Installation was not started because preflight is blocked.", log.FilePath);
        }

        if (preflight.Components.Count == 0)
        {
            return DeploymentExecutionResult.Succeeded("Every selected component is already installed by this prototype.", log.FilePath, 0);
        }

        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(InstallRoot);
        var transactionId = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(StagingRoot, transactionId);
        var installedThisTransaction = new List<ManagedInstalledComponent>();
        var ledger = LoadLedger();
        log.Write("install", "started", "Managed deployment transaction started.", new { transactionId, components = preflight.Components.Select(component => component.Id) });

        try
        {
            for (var index = 0; index < preflight.Components.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var component = preflight.Components[index];
                progress?.Report(new DeploymentProgress(index, preflight.Components.Count, $"Downloading {component.DisplayName}..."));
                var artifactPath = Path.Combine(stagingRoot, "artifacts", component.ArtifactSha256);
                Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
                await DownloadAndVerifyAsync(preflight.CatalogUri, component.ArtifactSha256, artifactPath, cancellationToken);
                log.Write("install", "downloaded", $"Verified {component.DisplayName}.", new { component.Id, component.ArtifactSha256 });

                progress?.Report(new DeploymentProgress(index, preflight.Components.Count, $"Deploying {component.DisplayName}..."));
                var componentStagingDirectory = Path.Combine(stagingRoot, "components", SanitizePathSegment(component.Id), component.Version);
                ExtractPayload(artifactPath, componentStagingDirectory);
                var targetDirectory = Path.Combine(InstallRoot, SanitizePathSegment(component.Id), component.Version);
                EnsureContainedPath(InstallRoot, targetDirectory);
                if (Directory.Exists(targetDirectory))
                {
                    throw new IOException($"The target directory already exists for '{component.Id}'. Remove the previous prototype deployment before installing a new version.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
                var installed = new ManagedInstalledComponent(component.Id, component.DisplayName, component.Version, component.ArtifactSha256, targetDirectory, transactionId, DateTimeOffset.UtcNow, "installing", null);
                installedThisTransaction.Add(installed);
                ledger.Components.Add(installed);
                SaveLedger(ledger);
                Directory.Move(componentStagingDirectory, targetDirectory);
                installed.State = "installed";
                SaveLedger(ledger);
                log.Write("install", "deployed", $"Deployed {component.DisplayName} to the managed prototype location.", new { component.Id, targetDirectory });
            }

            TryDeleteDirectory(stagingRoot);
            progress?.Report(new DeploymentProgress(preflight.Components.Count, preflight.Components.Count, "Managed deployment completed."));
            log.Write("install", "completed", "Managed deployment transaction completed.", new { transactionId, installed = installedThisTransaction.Select(component => component.ComponentId) });
            return DeploymentExecutionResult.Succeeded($"Installed {installedThisTransaction.Count} managed prototype component(s).", log.FilePath, installedThisTransaction.Count);
        }
        catch (Exception exception)
        {
            log.Write("install", "failed", "Managed deployment failed; rolling back files created by this transaction.", new { transactionId, exception.Message, exception.StackTrace });
            foreach (var installed in installedThisTransaction.AsEnumerable().Reverse())
            {
                TryDeleteManagedComponent(installed, log, "rollback");
                ledger.Components.RemoveAll(component => component.TransactionId == transactionId && component.ComponentId == installed.ComponentId);
            }
            SaveLedger(ledger);
            TryDeleteDirectory(stagingRoot);
            return DeploymentExecutionResult.Failed($"Installation failed and files from this transaction were rolled back: {exception.Message}", log.FilePath);
        }
    }

    public Task<DeploymentExecutionResult> UninstallAllAsync(PrototypeOperationLog log)
    {
        var ledger = LoadLedger();
        var installed = ledger.Components.Where(component => component.State != "removed").ToArray();
        log.Write("uninstall", "started", "Managed prototype uninstall started.", new { count = installed.Length });
        var removed = 0;
        var failures = new List<string>();

        foreach (var component in installed)
        {
            try
            {
                TryDeleteManagedComponent(component, log, "uninstall");
                component.State = "removed";
                component.RemovedAtUtc = DateTimeOffset.UtcNow;
                removed++;
            }
            catch (Exception exception)
            {
                failures.Add($"{component.DisplayName}: {exception.Message}");
                log.Write("uninstall", "failed", $"Could not remove {component.DisplayName}.", new { exception.Message, exception.StackTrace });
            }
        }

        try
        {
            TryDeleteDirectory(StagingRoot);
            log.Write("uninstall", "removed", "Removed app-owned transaction staging.", new { StagingRoot });
        }
        catch (Exception exception)
        {
            failures.Add($"transaction staging: {exception.Message}");
            log.Write("uninstall", "failed", "Could not remove app-owned transaction staging.", new { exception.Message, exception.StackTrace });
        }

        SaveLedger(ledger);
        if (failures.Count > 0)
        {
            return Task.FromResult(DeploymentExecutionResult.Failed($"Removed {removed} component(s), but {failures.Count} removal(s) failed: {string.Join(" ", failures)}", log.FilePath));
        }

        log.Write("uninstall", "completed", "Managed prototype uninstall completed.", new { removed });
        return Task.FromResult(DeploymentExecutionResult.Succeeded($"Removed {removed} managed prototype component(s).", log.FilePath, removed));
    }

    public int GetInstalledComponentCount() => LoadLedger().Components.Count(component => component.State != "removed");

    private async Task DownloadAndVerifyAsync(Uri catalogUri, string artifactSha256, string destinationPath, CancellationToken cancellationToken)
    {
        var artifactUri = new Uri(catalogUri, $"../../objects/sha256/{artifactSha256[..2]}/{artifactSha256}");
        using var response = await _client.GetAsync(artifactUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 128];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
        }

        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actualHash, artifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Artifact digest mismatch. Expected {artifactSha256}, received {actualHash}.");
        }
    }

    private static void ExtractPayload(string artifactPath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(artifactPath);
        var payloadEntries = archive.Entries.Where(entry => entry.FullName.StartsWith("payload/", StringComparison.Ordinal)).ToArray();
        if (payloadEntries.Length == 0)
        {
            throw new InvalidDataException("Component artifact does not contain a payload directory.");
        }

        foreach (var entry in payloadEntries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
            var relativePath = entry.FullName["payload/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            EnsureContainedPath(destinationDirectory, destinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: false);
        }
    }

    private DeploymentLedger LoadLedger()
    {
        if (!File.Exists(LedgerPath)) return new DeploymentLedger(LedgerSchemaVersion, []);
        var ledger = JsonSerializer.Deserialize<DeploymentLedger>(File.ReadAllText(LedgerPath), SerializerOptions);
        if (ledger is null || !string.Equals(ledger.SchemaVersion, LedgerSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The managed deployment ledger is invalid or unsupported. It was not changed.");
        }
        return ledger;
    }

    private void SaveLedger(DeploymentLedger ledger)
    {
        Directory.CreateDirectory(RootDirectory);
        var temporaryPath = LedgerPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(ledger, SerializerOptions));
        File.Move(temporaryPath, LedgerPath, overwrite: true);
    }

    private void TryDeleteManagedComponent(ManagedInstalledComponent component, PrototypeOperationLog log, string phase)
    {
        EnsureContainedPath(InstallRoot, component.TargetDirectory);
        if (Directory.Exists(component.TargetDirectory)) Directory.Delete(component.TargetDirectory, recursive: true);
        TryDeleteEmptyParents(Path.GetDirectoryName(component.TargetDirectory), InstallRoot);
        log.Write(phase, "removed", $"Removed {component.DisplayName} from the managed prototype location.", new { component.ComponentId, component.TargetDirectory });
    }

    private static void TryDeleteDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private static void TryDeleteEmptyParents(string? directory, string stopDirectory)
    {
        var stop = Path.GetFullPath(stopDirectory).TrimEnd(Path.DirectorySeparatorChar);
        while (!string.IsNullOrEmpty(directory))
        {
            var current = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(current, stop, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(current) ||
                Directory.EnumerateFileSystemEntries(current).Any())
            {
                return;
            }

            Directory.Delete(current);
            directory = Path.GetDirectoryName(current);
        }
    }

    private static void EnsureContainedPath(string rootDirectory, string candidatePath)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A deployment path escaped the managed prototype directory.");
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..") throw new InvalidDataException("Catalog component ID is not a valid path segment.");
        return sanitized;
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character));
}

public sealed record ManagedDeploymentCatalog(string SchemaVersion, IReadOnlyList<ManagedDeploymentCatalogComponent> Components);
public sealed record ManagedDeploymentCatalogComponent(string Id, string DisplayName, string Version, string ArtifactSha256, string InstallMode, bool ApprovedForManagedPrototypeInstall);
public sealed record DeploymentPreflightResult(bool IsReady, string Message, Uri CatalogUri, IReadOnlyList<ManagedDeploymentCatalogComponent> Components, IReadOnlyList<string> AlreadyInstalled)
{
    public static DeploymentPreflightResult Blocked(string message, Uri catalogUri) => new(false, message, catalogUri, [], []);
    public static DeploymentPreflightResult Ready(Uri catalogUri, IReadOnlyList<ManagedDeploymentCatalogComponent> components, IReadOnlyList<string> alreadyInstalled) => new(true, "Preflight passed. The managed deployment can run.", catalogUri, components, alreadyInstalled);
}
public sealed record DeploymentProgress(int CompletedComponents, int TotalComponents, string Status);
public sealed record DeploymentExecutionResult(bool IsSuccess, string Message, string LogFilePath, int ChangedComponentCount)
{
    public static DeploymentExecutionResult Succeeded(string message, string logFilePath, int changedComponentCount) => new(true, message, logFilePath, changedComponentCount);
    public static DeploymentExecutionResult Failed(string message, string logFilePath) => new(false, message, logFilePath, 0);
}
public sealed record DeploymentLedger(string SchemaVersion, List<ManagedInstalledComponent> Components);
public sealed class ManagedInstalledComponent
{
    public ManagedInstalledComponent(string componentId, string displayName, string version, string artifactSha256, string targetDirectory, string transactionId, DateTimeOffset installedAtUtc, string state, DateTimeOffset? removedAtUtc)
    {
        ComponentId = componentId;
        DisplayName = displayName;
        Version = version;
        ArtifactSha256 = artifactSha256;
        TargetDirectory = targetDirectory;
        TransactionId = transactionId;
        InstalledAtUtc = installedAtUtc;
        State = state;
        RemovedAtUtc = removedAtUtc;
    }

    public string ComponentId { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string ArtifactSha256 { get; }
    public string TargetDirectory { get; }
    public string TransactionId { get; }
    public DateTimeOffset InstalledAtUtc { get; }
    public string State { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
}
