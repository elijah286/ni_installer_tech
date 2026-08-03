using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace NIInstallerTech.Services;

public sealed class CleanMachineDeploymentService
{
    private const string LedgerSchemaVersion = "ni-setup-clean-machine-ledger-v0.1";
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string _stateDirectory;

    public CleanMachineDeploymentService(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NISetupPrototype",
            "clean-machine");
    }

    public string StateDirectory => _stateDirectory;

    public string LedgerPath => Path.Combine(StateDirectory, "ledger.json");

    public CleanMachineDeploymentResult Install(CleanMachineDeploymentRequest request, PrototypeOperationLog log)
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var stagingDirectory = Path.Combine(StateDirectory, "staging", transactionId);
        var stagedPayloadDirectory = Path.Combine(stagingDirectory, "payload");
        var targetDirectory = Path.GetFullPath(request.TargetDirectory);
        var ledger = LoadLedger();
        CleanMachineInstalledComponent? installed = null;
        var targetCreated = false;

        try
        {
            ValidateRequest(request, targetDirectory, ledger);
            log.Write("clean-machine-install", "started", "Clean-machine deployment transaction started.", new { transactionId, request.ComponentId, request.Version, targetDirectory });

            var archiveHash = ComputeSha256(request.ArchivePath);
            if (!string.Equals(archiveHash, request.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive digest mismatch. Expected {request.ArchiveSha256}, received {archiveHash}.");
            }

            var captureRoot = request.Version + "/";
            var capture = ReadArchiveRecord<CaptureManifest>(request.ArchivePath, captureRoot + "staging/manifest.json");
            ValidateCapture(request, capture);
            var payloadManifest = ReadArchiveRecord<PayloadManifest>(request.ArchivePath, captureRoot + "staging/payload-manifest.json");
            ValidatePayloadManifest(capture, payloadManifest);

            Directory.CreateDirectory(stagedPayloadDirectory);
            var ownedFiles = ExtractAndVerifyPayload(request, payloadManifest, stagedPayloadDirectory);
            VerifyHealthCheck(stagedPayloadDirectory, request.HealthCheckRelativePath, ownedFiles);

            installed = new CleanMachineInstalledComponent(
                request.ComponentId,
                request.Version,
                request.ArchiveSha256,
                targetDirectory,
                transactionId,
                DateTimeOffset.UtcNow,
                "installing",
                ownedFiles,
                null);
            ledger.Components.Add(installed);
            SaveLedger(ledger);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            Directory.Move(stagedPayloadDirectory, targetDirectory);
            targetCreated = true;
            VerifyHealthCheck(targetDirectory, request.HealthCheckRelativePath, ownedFiles);

            installed.State = "installed";
            SaveLedger(ledger);
            TryDeleteDirectory(stagingDirectory);
            log.Write("clean-machine-install", "completed", "Clean-machine deployment transaction completed.", new { transactionId, request.ComponentId, targetDirectory, files = ownedFiles.Count });
            return CleanMachineDeploymentResult.Succeeded($"Installed {request.ComponentId} to {targetDirectory}.", log.FilePath, ownedFiles.Count);
        }
        catch (Exception exception)
        {
            log.Write("clean-machine-install", "failed", "Clean-machine deployment failed; rolling back files created by this transaction.", new { transactionId, exception.Message, exception.StackTrace });
            if (targetCreated)
            {
                TryDeleteOwnedTarget(targetDirectory, log, "clean-machine-rollback");
            }

            if (installed is not null)
            {
                ledger.Components.RemoveAll(component => component.TransactionId == transactionId);
                SaveLedger(ledger);
            }

            TryDeleteDirectory(stagingDirectory);
            return CleanMachineDeploymentResult.Failed($"Clean-machine deployment failed and was rolled back: {exception.Message}", log.FilePath);
        }
    }

    public CleanMachineDeploymentResult Uninstall(string componentId, PrototypeOperationLog log)
    {
        var ledger = LoadLedger();
        var component = ledger.Components.SingleOrDefault(candidate =>
            string.Equals(candidate.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) && candidate.State == "installed");
        if (component is null)
        {
            return CleanMachineDeploymentResult.Failed($"No installed clean-machine component is owned for '{componentId}'.", log.FilePath);
        }

        try
        {
            foreach (var file in component.Files)
            {
                var filePath = Path.Combine(component.TargetDirectory, file.RelativePath);
                EnsureContainedPath(component.TargetDirectory, filePath);
                if (File.Exists(filePath)) File.Delete(filePath);
            }

            DeleteEmptyDirectories(component.TargetDirectory, component.TargetDirectory);
            if (Directory.Exists(component.TargetDirectory) && Directory.EnumerateFileSystemEntries(component.TargetDirectory).Any())
            {
                throw new IOException("The target directory contains files that are not recorded as owned by this deployment. It was preserved.");
            }

            if (Directory.Exists(component.TargetDirectory)) Directory.Delete(component.TargetDirectory);
            component.State = "removed";
            component.RemovedAtUtc = DateTimeOffset.UtcNow;
            SaveLedger(ledger);
            log.Write("clean-machine-uninstall", "completed", "Removed clean-machine component files recorded in the ownership ledger.", new { component.ComponentId, component.TargetDirectory, files = component.Files.Count });
            return CleanMachineDeploymentResult.Succeeded($"Removed {component.ComponentId}.", log.FilePath, component.Files.Count);
        }
        catch (Exception exception)
        {
            log.Write("clean-machine-uninstall", "failed", "Clean-machine uninstall could not remove all owned files.", new { component.ComponentId, exception.Message, exception.StackTrace });
            return CleanMachineDeploymentResult.Failed($"Clean-machine uninstall failed: {exception.Message}", log.FilePath);
        }
    }

    private static void ValidateRequest(CleanMachineDeploymentRequest request, string targetDirectory, CleanMachineDeploymentLedger ledger)
    {
        if (string.IsNullOrWhiteSpace(request.ComponentId) || string.IsNullOrWhiteSpace(request.Version)) throw new InvalidDataException("Component identity is required.");
        if (!File.Exists(request.ArchivePath)) throw new FileNotFoundException("The clean-machine archive was not found.", request.ArchivePath);
        if (!IsSha256(request.ArchiveSha256)) throw new InvalidDataException("The clean-machine archive digest must be a SHA-256 value.");
        if (string.IsNullOrWhiteSpace(request.PayloadDirectory) || string.IsNullOrWhiteSpace(request.HealthCheckRelativePath)) throw new InvalidDataException("Payload and health-check paths are required.");
        if (Path.GetPathRoot(targetDirectory) == targetDirectory) throw new InvalidDataException("The deployment target cannot be a filesystem root.");
        if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory)) throw new IOException($"The clean-machine target already exists and will not be overwritten: {targetDirectory}.");
        if (ledger.Components.Any(component => string.Equals(component.TargetDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase) && component.State != "removed"))
        {
            throw new InvalidDataException($"The ownership ledger already contains an active deployment for {targetDirectory}.");
        }
    }

    private static void ValidateCapture(CleanMachineDeploymentRequest request, CaptureManifest capture)
    {
        if (!string.Equals(capture.Id, request.ComponentId, StringComparison.OrdinalIgnoreCase) || !string.Equals(capture.Version, request.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The archive capture identity does not match the selected component.");
        }

        if (!capture.PayloadCopied) throw new InvalidDataException("The archive does not contain a copied payload.");
        if (string.Equals(capture.Classification, "reference-derived-poc", StringComparison.OrdinalIgnoreCase) && !request.AllowReferenceDerivedPoc)
        {
            throw new InvalidDataException("Reference-derived POC payloads require explicit clean-machine validation consent.");
        }
    }

    private static void ValidatePayloadManifest(CaptureManifest capture, PayloadManifest manifest)
    {
        if (manifest.Files is null || manifest.Files.Count == 0) throw new InvalidDataException("The archive payload manifest has no files.");
        if (manifest.Files.Count != capture.Files || manifest.Files.Sum(file => file.SizeBytes) != capture.Bytes)
        {
            throw new InvalidDataException("The payload manifest does not match the capture file count or byte count.");
        }

        var duplicates = manifest.Files
            .Select(file => NormalizeRelativePath(file.Destination))
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0) throw new InvalidDataException("The payload manifest contains duplicate destinations.");
        if (manifest.Files.Any(file => !IsSha256(file.Sha256) || file.SizeBytes < 0)) throw new InvalidDataException("The payload manifest contains an invalid file digest or size.");
    }

    private static List<CleanMachineOwnedFile> ExtractAndVerifyPayload(CleanMachineDeploymentRequest request, PayloadManifest manifest, string destinationRoot)
    {
        var payloadDirectory = NormalizeRelativePath(request.PayloadDirectory).TrimEnd('/') + "/";
        var archivePrefix = request.Version + "/payload/" + payloadDirectory;
        var expected = manifest.Files.ToDictionary(
            file =>
            {
                var destination = NormalizeRelativePath(file.Destination);
                if (!destination.StartsWith(payloadDirectory, StringComparison.Ordinal)) throw new InvalidDataException("The payload manifest contains a file outside the selected payload directory.");
                return request.Version + "/payload/" + destination;
            },
            file => new ExpectedPayloadFile(file, NormalizeRelativePath(file.Destination)[payloadDirectory.Length..]),
            StringComparer.Ordinal);
        var extracted = new List<CleanMachineOwnedFile>();

        using var archiveStream = File.OpenRead(request.ArchivePath);
        using var reader = new TarReader(archiveStream, leaveOpen: false);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            if (!entry.Name.StartsWith(archivePrefix, StringComparison.Ordinal)) continue;
            if (entry.EntryType is TarEntryType.Directory) continue;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
            {
                throw new InvalidDataException($"The archive payload contains a non-file entry: {entry.Name}.");
            }

            if (!expected.TryGetValue(entry.Name, out var expectedFile)) throw new InvalidDataException($"The archive contains a payload file absent from its manifest: {entry.Name}.");
            var relativePath = expectedFile.TargetRelativePath;
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            EnsureContainedPath(destinationRoot, destinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            var (length, sha256) = CopyAndHash(entry.DataStream, destinationPath);
            if (length != expectedFile.File.SizeBytes || !string.Equals(sha256, expectedFile.File.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Payload verification failed for '{relativePath}'.");
            }

            extracted.Add(new CleanMachineOwnedFile(relativePath, expectedFile.File.Sha256, expectedFile.File.SizeBytes));
        }

        if (extracted.Count != expected.Count) throw new InvalidDataException("The archive is missing one or more files declared by the payload manifest.");
        return extracted;
    }

    private static void VerifyHealthCheck(string rootDirectory, string relativePath, IReadOnlyList<CleanMachineOwnedFile> files)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        var expected = files.SingleOrDefault(file => string.Equals(file.RelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (expected is null) throw new InvalidDataException("The declared health-check file is absent from the payload manifest.");
        var path = Path.Combine(rootDirectory, normalizedPath);
        EnsureContainedPath(rootDirectory, path);
        if (!File.Exists(path) || !string.Equals(ComputeSha256(path), expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The declared health-check file is missing or failed verification.");
        }
    }

    private static T ReadArchiveRecord<T>(string archivePath, string recordPath)
    {
        using var archiveStream = File.OpenRead(archivePath);
        using var reader = new TarReader(archiveStream, leaveOpen: false);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            if (!string.Equals(entry.Name, recordPath, StringComparison.Ordinal)) continue;
            if (entry.DataStream is null) break;
            return JsonSerializer.Deserialize<T>(entry.DataStream, SerializerOptions) ?? throw new InvalidDataException($"The archive record '{recordPath}' is empty.");
        }

        throw new InvalidDataException($"The archive does not contain '{recordPath}'.");
    }

    private CleanMachineDeploymentLedger LoadLedger()
    {
        if (!File.Exists(LedgerPath)) return new CleanMachineDeploymentLedger(LedgerSchemaVersion, []);
        var ledger = JsonSerializer.Deserialize<CleanMachineDeploymentLedger>(File.ReadAllText(LedgerPath), SerializerOptions);
        if (ledger is null || !string.Equals(ledger.SchemaVersion, LedgerSchemaVersion, StringComparison.Ordinal)) throw new InvalidDataException("The clean-machine ownership ledger is invalid or unsupported.");
        return ledger;
    }

    private void SaveLedger(CleanMachineDeploymentLedger ledger)
    {
        Directory.CreateDirectory(StateDirectory);
        var temporaryPath = LedgerPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(ledger, SerializerOptions));
        File.Move(temporaryPath, LedgerPath, overwrite: true);
    }

    private static (long Length, string Sha256) CopyAndHash(Stream input, string destinationPath)
    {
        using var output = File.Create(destinationPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 128];
        long length = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
            length += read;
        }

        return (length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 128];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void TryDeleteOwnedTarget(string targetDirectory, PrototypeOperationLog log, string phase)
    {
        if (!Directory.Exists(targetDirectory)) return;
        Directory.Delete(targetDirectory, recursive: true);
        log.Write(phase, "removed", "Removed target directory created by the failed clean-machine transaction.", new { targetDirectory });
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void DeleteEmptyDirectories(string directory, string stopDirectory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var child in Directory.EnumerateDirectories(directory)) DeleteEmptyDirectories(child, stopDirectory);
        if (!string.Equals(Path.GetFullPath(directory), Path.GetFullPath(stopDirectory), StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidDataException("The archive contains an invalid relative payload path.");
        return normalized;
    }

    private static void EnsureContainedPath(string rootDirectory, string candidatePath)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A clean-machine deployment path escaped its target directory.");
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character));

    private sealed record CaptureManifest(string SchemaVersion, string Id, string Version, string Classification, bool Redistributable, bool PayloadCopied, int Files, long Bytes);
    private sealed record PayloadManifest(IReadOnlyList<PayloadManifestFile> Files);
    private sealed record PayloadManifestFile(string Destination, string Sha256, long SizeBytes);
    private sealed record ExpectedPayloadFile(PayloadManifestFile File, string TargetRelativePath);
}

public sealed record CleanMachineDeploymentRequest(
    string ComponentId,
    string Version,
    string ArchivePath,
    string ArchiveSha256,
    string TargetDirectory,
    string PayloadDirectory,
    string HealthCheckRelativePath,
    bool AllowReferenceDerivedPoc);

public sealed record CleanMachineDeploymentResult(bool IsSuccess, string Message, string LogFilePath, int ChangedFileCount)
{
    public static CleanMachineDeploymentResult Succeeded(string message, string logFilePath, int changedFileCount) => new(true, message, logFilePath, changedFileCount);
    public static CleanMachineDeploymentResult Failed(string message, string logFilePath) => new(false, message, logFilePath, 0);
}

public sealed record CleanMachineDeploymentLedger(string SchemaVersion, List<CleanMachineInstalledComponent> Components);

public sealed class CleanMachineInstalledComponent
{
    public CleanMachineInstalledComponent(string componentId, string version, string archiveSha256, string targetDirectory, string transactionId, DateTimeOffset installedAtUtc, string state, List<CleanMachineOwnedFile> files, DateTimeOffset? removedAtUtc)
    {
        ComponentId = componentId;
        Version = version;
        ArchiveSha256 = archiveSha256;
        TargetDirectory = targetDirectory;
        TransactionId = transactionId;
        InstalledAtUtc = installedAtUtc;
        State = state;
        Files = files;
        RemovedAtUtc = removedAtUtc;
    }

    public string ComponentId { get; }
    public string Version { get; }
    public string ArchiveSha256 { get; }
    public string TargetDirectory { get; }
    public string TransactionId { get; }
    public DateTimeOffset InstalledAtUtc { get; }
    public string State { get; set; }
    public List<CleanMachineOwnedFile> Files { get; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
}

public sealed record CleanMachineOwnedFile(string RelativePath, string Sha256, long SizeBytes);