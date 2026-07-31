using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NIInstallerTech.Services;

public sealed class HttpRepositoryService
{
    private const string ExpectedRepositoryId = "ni-setup-prototype-smb";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<RepositoryAccessResult> ConnectAndVerifyAsync(string repositoryUrl, CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryMetadataUris(repositoryUrl, out var metadataUri, out var nestedMetadataUri, out var validationError))
        {
            return RepositoryAccessResult.Failed(validationError);
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await Client.GetAsync(metadataUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                response = await Client.GetAsync(nestedMetadataUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                metadataUri = nestedMetadataUri;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return RepositoryAccessResult.Failed("The web source rejected this request. Configure the local web server to allow read-only access to repository metadata, or use its supported authentication mechanism.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return RepositoryAccessResult.Failed($"The web source responded, but {metadataUri.AbsolutePath} was not found. Serve the NISetupPrototypeRepository folder itself, not only its objects subfolder.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return RepositoryAccessResult.Failed($"The web source returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var state = root.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
            if (!string.Equals(id, ExpectedRepositoryId, StringComparison.Ordinal))
            {
                return RepositoryAccessResult.ConnectedButNotReady(
                    "Connected to the web source, but the repository identity is not recognized.",
                    "The installer will not consume source content from an endpoint that does not present the approved prototype repository identity.");
            }

            if (!string.Equals(state, "ready", StringComparison.OrdinalIgnoreCase))
            {
                return RepositoryAccessResult.ConnectedButNotReady(
                    "Connected to the NI Setup web source.",
                    $"Verified {metadataUri.AbsolutePath}. Repository state: {state ?? "unknown"}. A reviewed catalog and supported deployment executor are still required before installation can begin.");
            }

            return RepositoryAccessResult.Ready(
                "Connected to the NI Setup web source.",
                $"Verified {metadataUri.AbsolutePath}. The repository identity and ready state were verified. The connection is ready for a future deployment executor.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RepositoryAccessResult.Failed("The web source did not respond within 10 seconds. Verify the URL, port, local firewall, and that the server is listening on the LAN interface.");
        }
        catch (HttpRequestException exception)
        {
            return RepositoryAccessResult.Failed($"Windows could not reach the web source: {exception.Message}");
        }
        catch (JsonException)
        {
            return RepositoryAccessResult.ConnectedButNotReady(
                "Connected to the web source, but repository metadata is invalid.",
                "The installer will not consume source content until metadata is repaired and reviewed.");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static bool TryGetRepositoryMetadataUris(string repositoryUrl, out Uri metadataUri, out Uri nestedMetadataUri, out string error)
    {
        metadataUri = null!;
        nestedMetadataUri = null!;
        error = string.Empty;
        if (!Uri.TryCreate(repositoryUrl.Trim(), UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https"))
        {
            error = "Enter a complete HTTP or HTTPS repository URL, for example http://192.168.68.125:8080/.";
            return false;
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo))
        {
            error = "Do not put credentials in the repository URL.";
            return false;
        }

        var normalizedBase = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? baseUri : new Uri(baseUri.AbsoluteUri + "/");
        metadataUri = new Uri(normalizedBase, "metadata/repository.json");
        nestedMetadataUri = new Uri(normalizedBase, "NISetupPrototypeRepository/metadata/repository.json");
        return true;
    }
}
