using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string SchemaVersion = "ni-component-source-v0.1";
var options = ParseArguments(args);
if (options.ShowHelp || string.IsNullOrWhiteSpace(options.Source) || string.IsNullOrWhiteSpace(options.Output))
{
    WriteHelp();
    return options.ShowHelp ? 0 : 2;
}

var source = Path.GetFullPath(options.Source);
var output = Path.GetFullPath(options.Output);
if (!Directory.Exists(source))
{
    Console.Error.WriteLine($"Source directory does not exist: {source}");
    return 2;
}

var packages = Directory.EnumerateFiles(source, "*.nipkg", SearchOption.TopDirectoryOnly)
    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
    .ToList();
if (packages.Count == 0)
{
    Console.Error.WriteLine($"No .nipkg files found in: {source}");
    return 2;
}

var metadataRoot = Path.Combine(output, "metadata", "component-sources");
var objectRoot = Path.Combine(output, "objects", "sha256");
Directory.CreateDirectory(metadataRoot);
if (!options.DryRun)
{
    Directory.CreateDirectory(objectRoot);
}

var results = new List<ComponentSourceResult>();
foreach (var packagePath in packages)
{
    try
    {
        var result = ProcessPackage(packagePath, metadataRoot, objectRoot, options);
        results.Add(result);
        Console.WriteLine($"{result.Status,-10} {result.ComponentId} ({result.PayloadFiles} payload files, {result.ExcludedFiles} excluded)");
    }
    catch (Exception exception)
    {
        var failed = new ComponentSourceResult(
            Path.GetFileNameWithoutExtension(packagePath),
            Path.GetFileName(packagePath),
            "failed",
            null,
            0,
            0,
            0,
            new[] { exception.Message });
        results.Add(failed);
        Console.Error.WriteLine($"FAILED {Path.GetFileName(packagePath)}: {exception.Message}");
        if (!options.ContinueOnError) return 1;
    }
}

var summary = new AssemblySummary(
    SchemaVersion,
    DateTimeOffset.UtcNow,
    source,
    options.DryRun,
    options.IncludeFirmware,
    packages.Count,
    results.Count(result => result.Status == "assembled"),
    results.Count(result => result.Status == "planned"),
    results.Count(result => result.Status == "failed"),
    results);
File.WriteAllText(
    Path.Combine(metadataRoot, "assembly-summary.json"),
    JsonSerializer.Serialize(summary, SerializerSettings.Options));

Console.WriteLine($"Completed: {summary.Assembled} assembled, {summary.Planned} planned, {summary.Failed} failed.");
return summary.Failed == 0 ? 0 : 1;

static ComponentSourceResult ProcessPackage(string packagePath, string metadataRoot, string objectRoot, AssemblyOptions options)
{
    var packageName = Path.GetFileName(packagePath);
    var packageStem = Path.GetFileNameWithoutExtension(packagePath);
    var componentId = $"source.{NormalizeId(packageStem)}";
    var role = InferRole(packageName);
    var packageHash = ComputeSha256(packagePath);
    var excluded = new List<string>();
    var payloads = new List<PayloadFile>();
    var tempArtifact = Path.Combine(Path.GetTempPath(), $"ni-setup-{Guid.NewGuid():N}.zip");

    try
    {
        using var dataArchive = OpenDataArchive(packagePath);
        using var tar = new TarReader(dataArchive, leaveOpen: false);
        using var zipStream = options.DryRun ? Stream.Null : File.Create(tempArtifact);
        using var zip = options.DryRun ? null : new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null)
        {
            if (entry.DataStream is null || string.IsNullOrWhiteSpace(entry.Name)) continue;

            var normalizedPath = entry.Name.Replace('\\', '/').TrimStart('/');
            if (ShouldExclude(normalizedPath, packageName, options, out var reason))
            {
                excluded.Add($"{normalizedPath}: {reason}");
                continue;
            }

            var payloadPath = $"payload/{normalizedPath}";
            if (!options.DryRun)
            {
                var zipEntry = zip!.CreateEntry(payloadPath, CompressionLevel.Optimal);
                zipEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var destination = zipEntry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var bytes = CopyAndHash(entry.DataStream, destination, hash);
                payloads.Add(new PayloadFile(normalizedPath, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), bytes));
            }
            else
            {
                var bytes = Drain(entry.DataStream);
                payloads.Add(new PayloadFile(normalizedPath, "dry-run", bytes));
            }
        }

        var manifest = new ComponentSourceManifest(
            SchemaVersion,
            componentId,
            packageName,
            packageHash,
            role,
            "candidate-source-component",
            false,
            "The payload is newly assembled from a preserved original package. It is not a directly installable legacy package.",
            payloads,
            excluded,
            new[]
            {
                "Resolve a complete dependency closure before creating a customer plan.",
                "Validate resource ownership, health checks, and side-by-side policy.",
                "Do not activate drivers, firmware, services, or licensing material from this artifact."
            });

        if (!options.DryRun)
        {
            var manifestEntry = zip!.CreateEntry("component.json", CompressionLevel.Optimal);
            manifestEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var manifestStream = manifestEntry.Open();
            JsonSerializer.Serialize(manifestStream, manifest, SerializerSettings.Options);
        }

        string? artifactHash = null;
        if (!options.DryRun)
        {
            zip?.Dispose();
            zipStream.Dispose();
            artifactHash = ComputeSha256(tempArtifact);
            var destinationDirectory = Path.Combine(objectRoot, artifactHash[..2]);
            Directory.CreateDirectory(destinationDirectory);
            var destination = Path.Combine(destinationDirectory, artifactHash);
            if (!File.Exists(destination)) File.Move(tempArtifact, destination);
        }

        File.WriteAllText(
            Path.Combine(metadataRoot, $"{componentId}.json"),
            JsonSerializer.Serialize(manifest with { ArtifactSha256 = artifactHash }, SerializerSettings.Options));

        return new ComponentSourceResult(componentId, packageName, options.DryRun ? "planned" : "assembled", artifactHash, payloads.Count, excluded.Count, payloads.Sum(payload => payload.SizeBytes), excluded);
    }
    finally
    {
        if (File.Exists(tempArtifact)) File.Delete(tempArtifact);
    }
}

static Stream OpenDataArchive(string packagePath)
{
    using var file = File.OpenRead(packagePath);
    var header = new byte[8];
    if (file.Read(header, 0, header.Length) != header.Length || Encoding.ASCII.GetString(header) != "!<arch>\n")
    {
        throw new InvalidDataException("Package is not an ar archive.");
    }

    while (file.Position < file.Length)
    {
        var entryHeader = new byte[60];
        if (file.Read(entryHeader, 0, entryHeader.Length) != entryHeader.Length) break;
        var name = Encoding.ASCII.GetString(entryHeader, 0, 16).Trim().TrimEnd('/');
        var sizeText = Encoding.ASCII.GetString(entryHeader, 48, 10).Trim();
        if (!long.TryParse(sizeText, out var size) || size < 0) throw new InvalidDataException("Invalid ar entry size.");
        var offset = file.Position;
        if (name == "data.tar.gz")
        {
            var bytes = new byte[size];
            var read = 0;
            while (read < bytes.Length)
            {
                var count = file.Read(bytes, read, bytes.Length - read);
                if (count == 0) throw new EndOfStreamException("Unexpected end of package data archive.");
                read += count;
            }
            return new GZipStream(new MemoryStream(bytes, writable: false), CompressionMode.Decompress);
        }
        file.Position = offset + size + (size % 2);
    }

    throw new InvalidDataException("Package does not contain data.tar.gz.");
}

static bool ShouldExclude(string path, string packageName, AssemblyOptions options, out string reason)
{
    var lowerPath = path.ToLowerInvariant();
    var lowerPackage = packageName.ToLowerInvariant();
    if (!options.IncludeFirmware && lowerPackage.Contains("firmware", StringComparison.Ordinal))
    {
        reason = "firmware package excluded by policy";
        return true;
    }
    if (lowerPath.EndsWith(".sys", StringComparison.Ordinal) || lowerPath.EndsWith(".inf", StringComparison.Ordinal) || lowerPath.EndsWith(".cat", StringComparison.Ordinal))
    {
        reason = "kernel driver or driver-signing material excluded by policy";
        return true;
    }
    if (lowerPath.Contains("driverstore", StringComparison.Ordinal) || lowerPath.Contains("/system32/drivers/", StringComparison.Ordinal))
    {
        reason = "Driver Store/kernel path excluded by policy";
        return true;
    }
    if (lowerPath.Contains("activation", StringComparison.Ordinal) || lowerPath.Contains("entitlement", StringComparison.Ordinal) || lowerPath.Contains("credential", StringComparison.Ordinal) || lowerPath.Contains("privatekey", StringComparison.Ordinal))
    {
        reason = "licensing or secret-related path excluded by policy";
        return true;
    }
    reason = string.Empty;
    return false;
}

static long CopyAndHash(Stream source, Stream destination, IncrementalHash hash)
{
    var buffer = new byte[1024 * 128];
    long total = 0;
    int read;
    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
    {
        destination.Write(buffer, 0, read);
        hash.AppendData(buffer, 0, read);
        total += read;
    }
    return total;
}

static long Drain(Stream source)
{
    var buffer = new byte[1024 * 128];
    long total = 0;
    int read;
    while ((read = source.Read(buffer, 0, buffer.Length)) > 0) total += read;
    return total;
}

static string ComputeSha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static string NormalizeId(string value)
{
    var builder = new StringBuilder(value.Length);
    foreach (var character in value.ToLowerInvariant())
    {
        builder.Append(char.IsLetterOrDigit(character) ? character : '-');
    }
    return builder.ToString().Trim('-');
}

static string InferRole(string packageName)
{
    var name = packageName.ToLowerInvariant();
    if (name.Contains("documentation") || name.Contains("manual") || name.Contains("help") || name.Contains("docs")) return "documentation";
    if (name.Contains("example")) return "examples";
    if (name.Contains("labview-support") || name.Contains("dotnet") || name.Contains("python") || name.Contains("c-support")) return "language-adapter";
    if (name.Contains("runtime")) return "api-runtime";
    if (name.Contains("max")) return "configuration-candidate";
    return "application-or-shared-runtime-candidate";
}

static AssemblyOptions ParseArguments(string[] args)
{
    var result = new AssemblyOptions();
    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--source": result.Source = args[++index]; break;
            case "--output": result.Output = args[++index]; break;
            case "--dry-run": result.DryRun = true; break;
            case "--include-firmware": result.IncludeFirmware = true; break;
            case "--continue-on-error": result.ContinueOnError = true; break;
            case "--help": case "-h": result.ShowHelp = true; break;
            default: throw new ArgumentException($"Unknown argument: {args[index]}");
        }
    }
    return result;
}

static void WriteHelp() => Console.WriteLine("""
NI Setup Component Assembler

Transforms original .nipkg intake packages into new, content-addressed candidate source artifacts.
It never installs content. Kernel drivers (.sys/.inf/.cat), Driver Store paths, and licensing/secret paths are excluded.

Usage:
  ni-setup-component-assembler --source <nipkg-directory> --output <prototype-repository> [--dry-run] [--continue-on-error]
""");

sealed class AssemblyOptions
{
    public string? Source { get; set; }
    public string? Output { get; set; }
    public bool DryRun { get; set; }
    public bool IncludeFirmware { get; set; }
    public bool ContinueOnError { get; set; }
    public bool ShowHelp { get; set; }
}

static class SerializerSettings
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}

sealed record PayloadFile(string Path, string Sha256, long SizeBytes);
sealed record ComponentSourceManifest(
    string SchemaVersion,
    string Id,
    string OriginalPackage,
    string OriginalPackageSha256,
    string Role,
    string Classification,
    bool Redistributable,
    string Notes,
    IReadOnlyList<PayloadFile> Payload,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> NextRequiredWork,
    string? ArtifactSha256 = null);
sealed record ComponentSourceResult(string ComponentId, string OriginalPackage, string Status, string? ArtifactSha256, int PayloadFiles, int ExcludedFiles, long PayloadBytes, IReadOnlyList<string> Exclusions);
sealed record AssemblySummary(string SchemaVersion, DateTimeOffset AssembledAtUtc, string Source, bool DryRun, bool IncludeFirmware, int InputPackages, int Assembled, int Planned, int Failed, IReadOnlyList<ComponentSourceResult> Components);
