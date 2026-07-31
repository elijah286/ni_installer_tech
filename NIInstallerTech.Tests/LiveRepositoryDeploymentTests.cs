using System.Net.Http;
using NIInstallerTech.Services;
using Xunit;

namespace NIInstallerTech.Tests;

public sealed class LiveRepositoryDeploymentTests
{
    [Fact]
    [Trait("Category", "LiveRepository")]
    public async Task ManagedDeploymentAndUninstall_RoundTripAgainstConfiguredPrototypeRepository()
    {
        var repositoryUrl = Environment.GetEnvironmentVariable("NI_SETUP_LIVE_REPOSITORY_URL");
        if (string.IsNullOrWhiteSpace(repositoryUrl)) return;

        var rootDirectory = Path.Combine(Path.GetTempPath(), "NIInstallerTech.LiveRepositoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var service = new ManagedDeploymentService(rootDirectory, client);
            var log = new PrototypeOperationLog(rootDirectory);
            var componentIds = new[] { "max.configuration", "daqmx.runtime.user-mode", "daqmx.documentation" };

            var preflight = await service.PreflightAsync(new Uri(repositoryUrl), componentIds, log);
            Assert.True(preflight.IsReady, preflight.Message);

            var install = await service.InstallAsync(preflight, log);
            Assert.True(install.IsSuccess, install.Message);
            Assert.Equal(componentIds.Length, service.GetInstalledComponentCount());
            Assert.All(preflight.Components, component => Assert.True(Directory.Exists(Path.Combine(service.InstallRoot, component.Id, component.Version))));

            var uninstall = await service.UninstallAllAsync(log);
            Assert.True(uninstall.IsSuccess, uninstall.Message);
            Assert.Equal(0, service.GetInstalledComponentCount());
            Assert.Empty(Directory.EnumerateDirectories(service.InstallRoot));
        }
        finally
        {
            if (Directory.Exists(rootDirectory)) Directory.Delete(rootDirectory, recursive: true);
        }
    }
}
