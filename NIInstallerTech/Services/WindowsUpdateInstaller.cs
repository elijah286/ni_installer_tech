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

        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var executableName = Path.GetFileName(Environment.ProcessPath ?? "NI Setup.exe");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ni-setup-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, """
param([int]$TargetProcessId, [string]$ArchivePath, [string]$InstallDirectory, [string]$ExecutableName, [string]$ScriptPath)
while (Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 250 }
$stageDirectory = Join-Path $env:TEMP ("ni-setup-stage-" + [guid]::NewGuid().ToString("N"))
$backupDirectory = $InstallDirectory + ".previous-" + [guid]::NewGuid().ToString("N")
$previousInstallMoved = $false
try {
    New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $stageDirectory -Force
    $contents = @(Get-ChildItem -LiteralPath $stageDirectory -Force)
    if ($contents.Count -ne 1 -or -not $contents[0].PSIsContainer) { throw "The update archive has an invalid layout." }
    Move-Item -LiteralPath $InstallDirectory -Destination $backupDirectory -Force
    $previousInstallMoved = $true
    Move-Item -LiteralPath $contents[0].FullName -Destination $InstallDirectory -Force
    Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    $previousInstallMoved = $false
    Start-Process -FilePath (Join-Path $InstallDirectory $ExecutableName)
}
catch {
    if ($previousInstallMoved -and -not (Test-Path -LiteralPath $InstallDirectory) -and (Test-Path -LiteralPath $backupDirectory)) {
        Move-Item -LiteralPath $backupDirectory -Destination $InstallDirectory -Force
    }
    throw
}
finally {
    Remove-Item -LiteralPath $stageDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue
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
        startInfo.ArgumentList.Add("-ArchivePath");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add("-InstallDirectory");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("-ExecutableName");
        startInfo.ArgumentList.Add(executableName);
        startInfo.ArgumentList.Add("-ScriptPath");
        startInfo.ArgumentList.Add(scriptPath);
        Process.Start(startInfo);
    }
}