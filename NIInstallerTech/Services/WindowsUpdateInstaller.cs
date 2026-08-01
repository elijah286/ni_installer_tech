using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NIInstallerTech.Services;

public static class WindowsUpdateInstaller
{
    public static void Schedule(string archivePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Automatic updates are currently packaged for Windows only.");
        }
        if (!string.Equals(Path.GetExtension(archivePath), ".msi", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The verified update must be a Windows Installer package.");
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"ni-setup-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, """
param([int]$TargetProcessId, [string]$PackagePath, [string]$ScriptPath)
while (Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 250 }
try {
    Start-Process -FilePath "msiexec.exe" -ArgumentList @("/i", $PackagePath) -Wait
}
finally {
    Remove-Item -LiteralPath $PackagePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $ScriptPath -Force -ErrorAction SilentlyContinue
}
""");

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-TargetProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add("-ScriptPath");
        startInfo.ArgumentList.Add(scriptPath);
        Process.Start(startInfo);
    }
}