using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NIInstallerTech.Services;

public sealed class GitHubReleaseUpdateService
{
    private const string ReleaseAssetName = "NI-Platform-Setup-win-x64.msi";
    private static readonly Uri DefaultUpdateFeedUri = new("http://192.168.68.125:8081/Files/NISetupPrototypeRepository/updates/latest.json");
    private readonly HttpClient _httpClient;
    private readonly Uri _updateFeedUri;

    public GitHubReleaseUpdateService(HttpClient? httpClient = null, Uri? updateFeedUri = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _updateFeedUri = updateFeedUri ?? DefaultUpdateFeedUri;
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NI-Platform-Setup", AppVersion.Display));
        }
    }

    public async Task<UpdateRelease?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(_updateFeedUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UpdateFeedUnavailableException("The approved update feed is not published yet. Install the latest NI Setup MSI once manually, then updates will use the internal repository.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var release = document.RootElement;

        var version = ReadRequiredString(release, "version").TrimStart('v');
        if (string.IsNullOrWhiteSpace(version) || !IsNewer(version, AppVersion.Display))
        {
            return null;
        }

        var packageUri = ResolveFeedUri(ReadRequiredString(release, "packageUrl"));
        var checksumUri = ResolveFeedUri(ReadRequiredString(release, "checksumUrl"));

        return new UpdateRelease(
            version,
            packageUri,
            checksumUri,
            release.TryGetProperty("notes", out var notes) ? notes.GetString() ?? string.Empty : string.Empty);
    }

    public async Task<string> DownloadAndVerifyAsync(
        UpdateRelease update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var expectedChecksum = await DownloadChecksumAsync(update.ChecksumUri, cancellationToken);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"ni-setup-{update.Version}-{Guid.NewGuid():N}.msi");

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

    private Uri ResolveFeedUri(string value)
    {
        if (!Uri.TryCreate(_updateFeedUri, value, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException($"The update feed contains an invalid URL for {ReleaseAssetName}.");
        }

        return uri;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"The update feed is missing '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static bool IsNewer(string candidate, string current)
    {
        return Version.TryParse(candidate, out var candidateVersion)
            && Version.TryParse(current, out var currentVersion)
            && candidateVersion > currentVersion;
    }
}

public sealed class UpdateFeedUnavailableException(string message) : InvalidOperationException(message);