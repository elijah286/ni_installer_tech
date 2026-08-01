using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NIInstallerTech.Services;

public sealed class GitHubReleaseUpdateService
{
    private const string ReleaseAssetName = "NI-Platform-Setup-win-x64.zip";
    private const string LatestReleaseUrl = "https://api.github.com/repos/elijah286/ni_installer_tech/releases/latest";
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NI-Platform-Setup", AppVersion.Display));
        }
    }

    public async Task<UpdateRelease?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var release = document.RootElement;

        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
        {
            return null;
        }

        var version = release.GetProperty("tag_name").GetString()?.TrimStart('v');
        if (string.IsNullOrWhiteSpace(version) || !IsNewer(version, AppVersion.Display))
        {
            return null;
        }

        Uri? packageUri = null;
        Uri? checksumUri = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (string.Equals(name, ReleaseAssetName, StringComparison.Ordinal))
            {
                packageUri = new Uri(url);
            }
            else if (string.Equals(name, $"{ReleaseAssetName}.sha256", StringComparison.Ordinal))
            {
                checksumUri = new Uri(url);
            }
        }

        if (packageUri is null || checksumUri is null)
        {
            throw new InvalidDataException("The latest release is missing its Windows update package or checksum.");
        }

        return new UpdateRelease(
            version,
            packageUri,
            checksumUri,
            release.GetProperty("body").GetString() ?? string.Empty);
    }

    public async Task<string> DownloadAndVerifyAsync(
        UpdateRelease update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var expectedChecksum = await DownloadChecksumAsync(update.ChecksumUri, cancellationToken);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"ni-setup-{update.Version}-{Guid.NewGuid():N}.zip");

        try
        {
            using var response = await _httpClient.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true);
            var buffer = new byte[131072];
            long downloadedBytes = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                if (totalBytes is > 0)
                {
                    progress?.Report((double)downloadedBytes / totalBytes.Value);
                }
            }

            await destination.FlushAsync(cancellationToken);
            await using var file = File.OpenRead(destinationPath);
            var actualChecksum = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedChecksum),
                    Convert.FromHexString(actualChecksum)))
            {
                throw new InvalidDataException("The downloaded update did not match the published SHA-256 checksum.");
            }

            progress?.Report(1);
            return destinationPath;
        }
        catch
        {
            File.Delete(destinationPath);
            throw;
        }
    }

    private async Task<string> DownloadChecksumAsync(Uri checksumUri, CancellationToken cancellationToken)
    {
        var checksumFile = await _httpClient.GetStringAsync(checksumUri, cancellationToken);
        var checksum = checksumFile.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (checksum is null || checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The published update checksum is invalid.");
        }

        return checksum.ToLowerInvariant();
    }

    private static bool IsNewer(string candidate, string current)
    {
        return Version.TryParse(candidate, out var candidateVersion)
            && Version.TryParse(current, out var currentVersion)
            && candidateVersion > currentVersion;
    }
}