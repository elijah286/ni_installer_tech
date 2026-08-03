using System.Net;
using System.Net.Http;
using System.Text;
using NIInstallerTech.Services;
using Xunit;

namespace NIInstallerTech.Tests;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNewerReleaseWithWindowsAssets()
    {
        using var client = new HttpClient(new StaticResponseHandler("""
            {
                            "tag_name": "v0.0.2",
                            "draft": false,
                            "prerelease": false,
                            "body": "Improved update experience.",
                            "assets": [
                                { "name": "NI-Platform-Setup-win-x64.msi", "browser_download_url": "https://example.test/setup.msi" },
                                { "name": "NI-Platform-Setup-win-x64.msi.sha256", "browser_download_url": "https://example.test/setup.msi.sha256" }
                            ]
            }
            """));
        var service = new GitHubReleaseUpdateService(client);

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal("0.0.2", update.Version);
        Assert.Equal("https://example.test/setup.msi", update.DownloadUri.AbsoluteUri);
        Assert.Equal("Improved update experience.", update.Notes);
    }

    [Fact]
    public async Task CheckForUpdateAsync_IgnoresCurrentRelease()
    {
        using var client = new HttpClient(new StaticResponseHandler("""
            {
              "tag_name": "v0.0.1",
              "draft": false,
              "prerelease": false,
              "body": "",
              "assets": []
            }
            """));
        var service = new GitHubReleaseUpdateService(client);

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
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

}