using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

namespace NIInstallerTech.Services;

public static class CleanMachineInstallerWorker
{
    public const string WorkerArgument = "--clean-machine-worker";
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static bool IsWorkerInvocation(string[] args) => args.Length > 0 && string.Equals(args[0], WorkerArgument, StringComparison.Ordinal);

    public static int Run(string[] args)
    {
        var options = ParseOptions(args.Skip(1).ToArray());
        if (!TryGetRequiredOption(options, "archive", out var archivePath) || !TryGetRequiredOption(options, "result", out var resultPath)) return 2;
        if (!OperatingSystem.IsWindows() || !IsWindowsAdministrator())
        {
            WriteResult(resultPath, new CleanMachineWorkerResult(false, "Installation requires Windows administrator approval.", string.Empty));
            return 4;
        }

        try
        {
            var package = PublishedCleanMachinePackages.Labview2026Q3;
            var targetDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "National Instruments", "LabVIEW 2026");
            var service = new CleanMachineDeploymentService();
            var log = new PrototypeOperationLog(service.StateDirectory);
            var result = service.Install(
                new CleanMachineDeploymentRequest(
                    package.ComponentId,
                    package.Version,
                    archivePath,
                    package.ArchiveSha256,
                    targetDirectory,
                    package.PayloadDirectory,
                    package.HealthCheckRelativePath,
                    true),
                log);
            WriteResult(resultPath, new CleanMachineWorkerResult(result.IsSuccess, result.Message, result.LogFilePath));
            return result.IsSuccess ? 0 : 1;
        }
        catch (Exception exception)
        {
            WriteResult(resultPath, new CleanMachineWorkerResult(false, exception.Message, string.Empty));
            return 1;
        }
    }

    public static async Task<CleanMachineWorkerResult> RunElevatedAsync(CleanMachineStagedPackage package, IProgress<CleanMachineInstallerProgress>? progress = null)
    {
        if (!OperatingSystem.IsWindows()) return new CleanMachineWorkerResult(false, "Clean-machine installation is supported only on Windows x64.", string.Empty);

        var resultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NISetupPrototype", "clean-machine-results");
        Directory.CreateDirectory(resultDirectory);
        var resultPath = Path.Combine(resultDirectory, Guid.NewGuid().ToString("N") + ".json");
        try
        {
            progress?.Report(new CleanMachineInstallerProgress(3, "Waiting for administrator approval", "Windows is displaying an approval request before files are changed."));
            var startInfo = CreateWorkerStartInfo(package.ArchivePath, resultPath);
            using var process = Process.Start(startInfo);
            if (process is null) return new CleanMachineWorkerResult(false, "Windows could not start the elevated installer worker.", string.Empty);
            progress?.Report(new CleanMachineInstallerProgress(4, "Installing LabVIEW", "Windows approved the request and the installer is now applying the verified package."));
            await process.WaitForExitAsync();
            if (!File.Exists(resultPath)) return new CleanMachineWorkerResult(false, "The elevated installer ended without reporting a result.", string.Empty);
            var result = JsonSerializer.Deserialize<CleanMachineWorkerResult>(await File.ReadAllTextAsync(resultPath), SerializerOptions);
            return result ?? new CleanMachineWorkerResult(false, "The elevated installer returned an invalid result.", string.Empty);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new CleanMachineWorkerResult(false, "Administrator approval was cancelled. No installation was started.", string.Empty);
        }
        catch (Exception exception)
        {
            return new CleanMachineWorkerResult(false, $"Windows could not start the elevated installer: {exception.Message}", string.Empty);
        }
        finally
        {
            if (File.Exists(resultPath)) File.Delete(resultPath);
        }
    }

    private static ProcessStartInfo CreateWorkerStartInfo(string archivePath, string resultPath)
    {
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The setup executable path could not be determined.");
        var arguments = $"{WorkerArgument} --archive {Quote(archivePath)} --result {Quote(resultPath)}";
        if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "NIInstallerTech.dll");
            if (!File.Exists(assemblyPath)) throw new InvalidOperationException("The setup assembly path could not be determined.");
            arguments = $"{Quote(assemblyPath)} {arguments}";
        }

        return new ProcessStartInfo(executablePath, arguments)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
    }

    private static Dictionary<string, string> ParseOptions(IReadOnlyList<string> arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < arguments.Count; index += 2)
        {
            if (arguments[index].StartsWith("--", StringComparison.Ordinal)) options[arguments[index][2..]] = arguments[index + 1];
        }
        return options;
    }

    private static bool TryGetRequiredOption(IReadOnlyDictionary<string, string> options, string key, out string value)
        => options.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void WriteResult(string resultPath, CleanMachineWorkerResult result)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(resultPath));
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("The installer result path is invalid.");
        Directory.CreateDirectory(directory);
        var temporaryPath = resultPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(result));
        File.Move(temporaryPath, resultPath, overwrite: true);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}

public static class PublishedCleanMachinePackages
{
    public static readonly CleanMachinePackage Labview2026Q3 = new(
        "labview.application.2026-q3.x64",
        "26.30.49792",
        "labview.application.2026-q3.x64-26.30.49792.reference-derived-poc.tar",
        "8a2f6f00f13ff9c8083f694b4ec2fdf81b71577aac2af7d26ac0f3c2ae822a91",
        "incoming-reference-captures/labview.application.2026-q3.x64/labview.application.2026-q3.x64-26.30.49792.reference-derived-poc.tar",
        "labview-application",
        "LabVIEW.exe");
}

public sealed record CleanMachineWorkerResult(bool IsSuccess, string Message, string LogFilePath);
public sealed record CleanMachineInstallerProgress(int PhaseIndex, string Status, string Detail);