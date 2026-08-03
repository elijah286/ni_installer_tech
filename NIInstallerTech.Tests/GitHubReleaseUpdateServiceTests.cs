using System.Net;
using System.Net.Http;
using System.Text;
using NIInstallerTech.Services;
using Xunit;

namespace NIInstallerTech.Tests;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNewerReleaseFromApprovedFeed()
    {
        using var client = new HttpClient(new StaticResponseHandler("""
            {
              "version": "0.0.2",
              "packageUrl": "NI-Platform-Setup-win-x64.msi",
              "checksumUrl": "NI-Platform-Setup-win-x64.msi.sha256",
              "notes": "Improved update experience."
            }
            """));
        var service = new GitHubReleaseUpdateService(client, new Uri("https://example.test/updates/latest.json"));

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal("0.0.2", update.Version);
        Assert.Equal("https://example.test/updates/NI-Platform-Setup-win-x64.msi", update.DownloadUri.AbsoluteUri);
        Assert.Equal("Improved update experience.", update.Notes);
    }

    [Fact]
    public async Task CheckForUpdateAsync_IgnoresCurrentRelease()
    {
        using var client = new HttpClient(new StaticResponseHandler("""
            {
              "version": "0.0.1",
              "packageUrl": "NI-Platform-Setup-win-x64.msi",
              "checksumUrl": "NI-Platform-Setup-win-x64.msi.sha256"
            }
            """));
        var service = new GitHubReleaseUpdateService(client, new Uri("https://example.test/updates/latest.json"));

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ExplainsWhenApprovedFeedIsMissing()
    {
        using var client = new HttpClient(new StatusResponseHandler(HttpStatusCode.NotFound));
        var service = new GitHubReleaseUpdateService(client, new Uri("https://example.test/updates/latest.json"));

        var exception = await Assert.ThrowsAsync<UpdateFeedUnavailableException>(() => service.CheckForUpdateAsync());

        Assert.Contains("not published", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StaticResponseHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StatusResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
    }
}