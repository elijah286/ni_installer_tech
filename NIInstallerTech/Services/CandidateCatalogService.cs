using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NIInstallerTech.Services;

public sealed class CandidateCatalogService
{
    private const string SchemaVersion = "ni-setup-candidate-contract-catalog-v0.1";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _rootDirectory;

    public CandidateCatalogService(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NISetupPrototype",
            "candidate-contracts");
    }

    public string CatalogPath => Path.Combine(_rootDirectory, "candidate-contract-catalog.json");
    public string LegacyPackageIndexPath => Path.Combine(_rootDirectory, "legacy-package-index.json");

    public static NativePackageManagerInstallation? DiscoverLocalNativePackageManager(string? programFilesDirectory = null, string? commonApplicationDataDirectory = null)
    {
        if (programFilesDirectory is null && !OperatingSystem.IsWindows()) return null;

        var programFiles = programFilesDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var nipkgPath = Path.Combine(programFiles, "National Instruments", "NI Package Manager", "nipkg.exe");
        if (!File.Exists(nipkgPath)) return null;

        var programData = commonApplicationDataDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var cachePath = Path.Combine(programData, "National Instruments", "NI Package Manager");
        var version = FileVersionInfo.GetVersionInfo(nipkgPath).ProductVersion;
        return new NativePackageManagerInstallation(nipkgPath, cachePath, string.IsNullOrWhiteSpace(version) ? "unknown" : version);
    }

    public async Task<LegacyPackageIndex> LoadLegacyPackageIndexAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LegacyPackageIndexPath)) return new LegacyPackageIndex("ni-setup-legacy-package-index-v0.1", []);

        await using var stream = File.OpenRead(LegacyPackageIndexPath);
        var index = await JsonSerializer.DeserializeAsync<LegacyPackageIndex>(stream, SerializerOptions, cancellationToken);
        return index is null || !string.Equals(index.SchemaVersion, "ni-setup-legacy-package-index-v0.1", StringComparison.Ordinal)
            ? new LegacyPackageIndex("ni-setup-legacy-package-index-v0.1", [])
            : index;
    }

    public async Task<LegacyPackageIndexResult> IndexNativePackageSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new InvalidOperationException("Provide the local or mounted NIPM package-cache path to index.");

        var fullSourcePath = Path.GetFullPath(sourcePath.Trim());
        if (!Directory.Exists(fullSourcePath)) throw new DirectoryNotFoundException($"The NIPM package-cache path was not found: {fullSourcePath}");

        var discovered = new List<LegacyPackageOption>();
        var warnings = new List<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (var packagePath in Directory.EnumerateFiles(fullSourcePath, "*.nipkg", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var metadata = ReadNativePackageMetadata(packagePath);
                    discovered.Add(new LegacyPackageOption(
                        fullSourcePath,
                        metadata.Name,
                        metadata.Version,
                        packagePath,
                        string.Empty,
                        metadata.Dependencies,
                        DateTimeOffset.UtcNow));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    warnings.Add($"Could not index {Path.GetFileName(packagePath)}: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"The NIPM package cache could not be read: {exception.Message}", exception);
        }

        var index = await LoadLegacyPackageIndexAsync(cancellationToken);
        index.Packages.RemoveAll(package => string.Equals(package.SourceRoot, fullSourcePath, StringComparison.OrdinalIgnoreCase));
        index.Packages.AddRange(discovered
            .OrderBy(package => package.PackageName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(package => package.PackageVersion, StringComparer.OrdinalIgnoreCase));
        await SaveLegacyPackageIndexAsync(index, cancellationToken);
        return new LegacyPackageIndexResult(index.Packages, discovered.Count, warnings, LegacyPackageIndexPath);
    }

    public async Task<CandidateCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CatalogPath)) return new CandidateCatalog(SchemaVersion, []);

        await using var stream = File.OpenRead(CatalogPath);
        var catalog = await JsonSerializer.DeserializeAsync<CandidateCatalog>(stream, SerializerOptions, cancellationToken);
        return catalog is null || !string.Equals(catalog.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
            ? new CandidateCatalog(SchemaVersion, [])
            : catalog;
    }

    public async Task<CandidateDiscoveryResult> InspectAndUpsertAsync(CandidateIntakeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SourcePaths.Count == 0) throw new InvalidOperationException("Provide at least one local file or directory to inspect.");

        var catalog = await LoadAsync(cancellationToken);
        var componentId = CreateComponentId(request.ComponentId, request.DisplayName);
        var evidence = new List<CandidateEvidence>();
        var warnings = new List<string>();
        var sourceFiles = 0;

        foreach (var sourcePath in request.SourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(sourcePath.Trim());
            if (File.Exists(fullPath))
            {
                sourceFiles += await InspectFileAsync(fullPath, evidence, warnings, cancellationToken);
                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                warnings.Add($"Source path was not found: {fullPath}");
                continue;
            }

            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (var filePath in Directory.EnumerateFiles(fullPath, "*", options).Where(IsInspectableArtifact))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sourceFiles += await InspectFileAsync(filePath, evidence, warnings, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not fully inspect {fullPath}: {exception.Message}");
            }
        }

        if (evidence.Count == 0) warnings.Add("No supported package, installer, archive, or executable artifacts were found. Add a .nipkg, .msi, .exe, .cab, or .zip source.");
        var packageEvidence = evidence.Where(item => item.PackageName is not null).ToArray();
        var versions = packageEvidence.Select(item => item.PackageVersion).Where(version => !string.IsNullOrWhiteSpace(version)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var dependencies = packageEvidence.SelectMany(item => item.Dependencies).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var packageNames = packageEvidence.Select(item => item.PackageName!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

        if (evidence.Any(item => item.Kind == "msi")) warnings.Add("MSI files are fingerprinted here; Windows MSI table and custom-action capture requires the Windows evidence collector before approval.");
        if (packageEvidence.Length == 0 && evidence.Count > 0) warnings.Add("No native package control metadata was found. Treat dependency and version fields as incomplete until R&D supplies or approves them.");

        var existingIndex = catalog.Components.FindIndex(component => string.Equals(component.Id, componentId, StringComparison.OrdinalIgnoreCase));
        var existing = existingIndex >= 0 ? catalog.Components[existingIndex] : null;
        var candidate = new CandidateComponent(
            componentId,
            string.IsNullOrWhiteSpace(request.DisplayName) ? existing?.DisplayName ?? componentId : request.DisplayName.Trim(),
            existing?.ReviewStatus ?? "awaiting-rd-review",
            existing?.DeclaredInstallMode ?? "undecided",
            versions.Length == 1 ? versions[0]! : "unresolved",
            packageNames,
            dependencies,
            evidence.OrderBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            existing?.RAndDNotes ?? string.Empty,
            existing?.ReviewedBy ?? string.Empty,
            DateTimeOffset.UtcNow);

        if (existingIndex >= 0) catalog.Components[existingIndex] = candidate;
        else catalog.Components.Add(candidate);
        await SaveAsync(catalog, cancellationToken);
        return new CandidateDiscoveryResult(candidate, sourceFiles, CatalogPath);
    }

    public async Task<CandidateComponent> UpdateReviewAsync(string componentId, CandidateReviewUpdate update, CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(cancellationToken);
        var index = catalog.Components.FindIndex(component => string.Equals(component.Id, componentId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException($"Candidate contract '{componentId}' does not exist.");

        var existing = catalog.Components[index];
        var updated = existing with
        {
            DisplayName = string.IsNullOrWhiteSpace(update.DisplayName) ? existing.DisplayName : update.DisplayName.Trim(),
            ReviewStatus = string.IsNullOrWhiteSpace(update.ReviewStatus) ? existing.ReviewStatus : update.ReviewStatus.Trim(),
            DeclaredInstallMode = string.IsNullOrWhiteSpace(update.DeclaredInstallMode) ? existing.DeclaredInstallMode : update.DeclaredInstallMode.Trim(),
            RAndDNotes = update.RAndDNotes?.Trim() ?? string.Empty,
            ReviewedBy = update.ReviewedBy?.Trim() ?? string.Empty
        };
        catalog.Components[index] = updated;
        await SaveAsync(catalog, cancellationToken);
        return updated;
    }

    private async Task SaveAsync(CandidateCatalog catalog, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectory);
        var temporaryPath = CatalogPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, catalog, SerializerOptions, cancellationToken);
        }
        File.Move(temporaryPath, CatalogPath, overwrite: true);
    }

    private async Task SaveLegacyPackageIndexAsync(LegacyPackageIndex index, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectory);
        var temporaryPath = LegacyPackageIndexPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, index, SerializerOptions, cancellationToken);
        }
        File.Move(temporaryPath, LegacyPackageIndexPath, overwrite: true);
    }

    private static async Task<int> InspectFileAsync(string filePath, ICollection<CandidateEvidence> evidence, ICollection<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(filePath);
            await using var stream = File.OpenRead(filePath);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            var kind = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            var package = kind == "nipkg" ? ReadNativePackageMetadata(filePath) : null;
            evidence.Add(new CandidateEvidence(
                filePath,
                Path.GetFileName(filePath),
                kind,
                info.Length,
                digest,
                package?.Name,
                package?.Version,
                package?.Dependencies ?? [],
                package is null ? "Artifact fingerprint captured; semantic installer inspection may still be required." : "Native package control metadata captured."));
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            warnings.Add($"Could not inspect {filePath}: {exception.Message}");
            return 0;
        }
    }

    private static NativePackageMetadata ReadNativePackageMetadata(string packagePath)
    {
        using var controlArchive = OpenArchiveMember(packagePath, "control.tar.gz");
        using var gzip = new GZipStream(controlArchive, CompressionMode.Decompress);
        using var tar = new TarReader(gzip, leaveOpen: false);
        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null)
        {
            if (entry.DataStream is null || !entry.Name.TrimStart('.', '/').Equals("control", StringComparison.Ordinal)) continue;
            using var reader = new StreamReader(entry.DataStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var fields = ParseControl(reader.ReadToEnd());
            if (!fields.TryGetValue("Package", out var name) || !fields.TryGetValue("Version", out var version)) throw new InvalidDataException("Package control metadata is missing Package or Version.");
            return new NativePackageMetadata(name, version, ParseDependencies(fields.GetValueOrDefault("Depends")));
        }
        throw new InvalidDataException("Package control.tar.gz does not contain a control file.");
    }

    private static Stream OpenArchiveMember(string packagePath, string requestedMember)
    {
        using var file = File.OpenRead(packagePath);
        var magic = new byte[8];
        if (file.Read(magic, 0, magic.Length) != magic.Length || Encoding.ASCII.GetString(magic) != "!<arch>\n") throw new InvalidDataException("Package is not an ar archive.");
        while (file.Position < file.Length)
        {
            var header = new byte[60];
            if (file.Read(header, 0, header.Length) != header.Length) break;
            var name = Encoding.ASCII.GetString(header, 0, 16).Trim().TrimEnd('/');
            var sizeText = Encoding.ASCII.GetString(header, 48, 10).Trim();
            if (!long.TryParse(sizeText, out var size) || size < 0 || size > int.MaxValue) throw new InvalidDataException("Package ar member size is invalid.");
            var memberOffset = file.Position;
            if (string.Equals(name, requestedMember, StringComparison.Ordinal))
            {
                var bytes = new byte[size];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = file.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) throw new EndOfStreamException("Package member ended unexpectedly.");
                    offset += read;
                }
                return new MemoryStream(bytes, writable: false);
            }
            file.Position = memberOffset + size + (size % 2);
        }
        throw new InvalidDataException($"Package does not contain {requestedMember}.");
    }

    private static Dictionary<string, string> ParseControl(string control)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        foreach (var line in control.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0) break;
            if (char.IsWhiteSpace(line[0]) && currentKey is not null)
            {
                fields[currentKey] += " " + line.Trim();
                continue;
            }
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            currentKey = line[..separator];
            fields[currentKey] = line[(separator + 1)..].Trim();
        }
        return fields;
    }

    private static IReadOnlyList<string> ParseDependencies(string? dependencies)
    {
        if (string.IsNullOrWhiteSpace(dependencies)) return [];
        return dependencies.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(dependency => dependency.Trim().Split('|', 2)[0].Trim())
            .Select(dependency => dependency.Split([' ', '(', '<', '>', '='], StringSplitOptions.RemoveEmptyEntries)[0])
            .Where(dependency => dependency.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsInspectableArtifact(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".nipkg" or ".msi" or ".exe" or ".cab" or ".zip";

    private static string CreateComponentId(string requestedId, string displayName)
    {
        var source = string.IsNullOrWhiteSpace(requestedId) ? displayName : requestedId;
        var normalized = new string(source.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal)) normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized)) throw new InvalidOperationException("Provide a product name or component ID.");
        return normalized;
    }

    private sealed record NativePackageMetadata(string Name, string Version, IReadOnlyList<string> Dependencies);
}

public sealed record CandidateCatalog(string SchemaVersion, List<CandidateComponent> Components);

public sealed record LegacyPackageIndex(string SchemaVersion, List<LegacyPackageOption> Packages);

public sealed record NativePackageManagerInstallation(string NipkgPath, string PackageCachePath, string Version);

public sealed record LegacyPackageOption(
    string SourceRoot,
    string PackageName,
    string PackageVersion,
    string PackagePath,
    string Sha256,
    IReadOnlyList<string> Dependencies,
    DateTimeOffset IndexedAtUtc)
{
    public string SelectionLabel => $"{PackageName} {PackageVersion}";
}

public sealed record LegacyPackageIndexResult(
    IReadOnlyList<LegacyPackageOption> Packages,
    int IndexedPackageCount,
    IReadOnlyList<string> Warnings,
    string IndexPath);

public sealed record CandidateComponent(
    string Id,
    string DisplayName,
    string ReviewStatus,
    string DeclaredInstallMode,
    string ObservedVersion,
    IReadOnlyList<string> LegacyPackageNames,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<CandidateEvidence> Evidence,
    IReadOnlyList<string> Warnings,
    string RAndDNotes,
    string ReviewedBy,
    DateTimeOffset LastDiscoveredAtUtc);

public sealed record CandidateEvidence(
    string SourcePath,
    string FileName,
    string Kind,
    long SizeBytes,
    string Sha256,
    string? PackageName,
    string? PackageVersion,
    IReadOnlyList<string> Dependencies,
    string Observation);

public sealed record CandidateIntakeRequest(string DisplayName, string ComponentId, IReadOnlyList<string> SourcePaths);

public sealed record CandidateReviewUpdate(string? DisplayName, string? ReviewStatus, string? DeclaredInstallMode, string? RAndDNotes, string? ReviewedBy);

public sealed record CandidateDiscoveryResult(CandidateComponent Candidate, int SourceFilesScanned, string CatalogPath);