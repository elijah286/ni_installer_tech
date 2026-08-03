using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NIInstallerTech.Services;

public sealed class CleanMachinePackageService
{
    private readonly string _downloadDirectory;
    private readonly HttpClient _client;

    public CleanMachinePackageService(string? downloadDirectory = null, HttpClient? client = null)
    {
        _downloadDirectory = downloadDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NISetupPrototype",
            "clean-machine-downloads");
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    public string DownloadDirectory => _downloadDirectory;

    public async Task<CleanMachineStagedPackage> StageFromFileAsync(CleanMachinePackage package, string sourcePath, IProgress<CleanMachinePackageProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(sourcePath);
        return await StageAsync(package, source, progress, cancellationToken);
    }

    public async Task<CleanMachineStagedPackage> StageFromUriAsync(CleanMachinePackage package, Uri sourceUri, IProgress<CleanMachinePackageProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await StageAsync(package, source, progress, cancellationToken, response.Content.Headers.ContentLength);
    }

    private async Task<CleanMachineStagedPackage> StageAsync(CleanMachinePackage package, Stream source, IProgress<CleanMachinePackageProgress>? progress, CancellationToken cancellationToken, long? totalBytes = null)
    {
        if (!IsSha256(package.ArchiveSha256)) throw new InvalidDataException("The package archive digest must be a SHA-256 value.");
        Directory.CreateDirectory(DownloadDirectory);
        var destinationPath = Path.Combine(DownloadDirectory, package.ArchiveFileName);
        EnsureContainedPath(DownloadDirectory, destinationPath);
        if (File.Exists(destinationPath) && string.Equals(ComputeSha256(destinationPath), package.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new CleanMachinePackageProgress(new FileInfo(destinationPath).Length, new FileInfo(destinationPath).Length, "Verified an existing local package."));
            return new CleanMachineStagedPackage(package, destinationPath);
        }

        var temporaryPath = destinationPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var destination = File.Create(temporaryPath);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 128];
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                copied += read;
                progress?.Report(new CleanMachinePackageProgress(copied, totalBytes, "Downloading and verifying the selected package..."));
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actualHash, package.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Package digest mismatch. Expected {package.ArchiveSha256}, received {actualHash}.");
            }

            await destination.FlushAsync(cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: true);
            progress?.Report(new CleanMachinePackageProgress(copied, totalBytes ?? copied, "Verified the selected package."));
            return new CleanMachineStagedPackage(package, destinationPath);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
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

    private static void EnsureContainedPath(string rootDirectory, string candidatePath)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(candidatePath).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The local package path escaped the staging directory.");
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character));
}

public sealed record CleanMachinePackage(string ComponentId, string Version, string ArchiveFileName, string ArchiveSha256, string ArchiveRelativePath, string PayloadDirectory, string HealthCheckRelativePath);
public sealed record CleanMachineStagedPackage(CleanMachinePackage Package, string ArchivePath);
public sealed record CleanMachinePackageProgress(long BytesTransferred, long? TotalBytes, string Status);