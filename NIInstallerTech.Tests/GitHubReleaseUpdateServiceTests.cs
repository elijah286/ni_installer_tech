using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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

    [Fact]
    public async Task DownloadAndVerifyAsync_ClosesThePackageBeforeReturning()
    {
        var package = Encoding.UTF8.GetBytes("verified update package");
        var checksum = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
        using var client = new HttpClient(new RouteResponseHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/setup.msi" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) },
            "/setup.msi.sha256" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{checksum}  setup.msi") },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        var service = new GitHubReleaseUpdateService(client);
        var update = new UpdateRelease("0.0.2", new Uri("https://example.test/setup.msi"), new Uri("https://example.test/setup.msi.sha256"), string.Empty);

        var packagePath = await service.DownloadAndVerifyAsync(update);
        try
        {
            await using var lockProbe = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(package.Length, lockProbe.Length);
        }
        finally
        {
            File.Delete(packagePath);
        }
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

    private sealed class RouteResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

}