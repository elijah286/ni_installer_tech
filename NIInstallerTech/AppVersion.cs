using System.Reflection;

namespace NIInstallerTech;

public static class AppVersion
{
    public static string Display { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? "0.0.1"
            : informationalVersion.Split('+')[0];
    }
}