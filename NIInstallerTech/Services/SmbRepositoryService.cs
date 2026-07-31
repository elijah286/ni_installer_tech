using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NIInstallerTech.Services;

public sealed class SmbRepositoryService
{
    private const string ExpectedRepositoryId = "ni-setup-prototype-smb";

    public RepositoryAccessResult ConnectAndVerify(string repositoryPath, string? userName, string? password)
    {
        if (!TryGetShareRoot(repositoryPath, out var shareRoot, out var validationError))
        {
            return RepositoryAccessResult.Failed(validationError);
        }

        if (!OperatingSystem.IsWindows())
        {
            return RepositoryAccessResult.Failed("The downloadable prototype supports SMB connection verification on Windows only.");
        }

        var connectionAttempted = false;
        var nativeError = 0;
        if (!CanRead(repositoryPath))
        {
            connectionAttempted = true;
            nativeError = Connect(shareRoot, userName, password);
            if (nativeError != 0)
            {
                return RepositoryAccessResult.Failed(DescribeWindowsNetworkError(nativeError));
            }
        }

        if (!CanRead(repositoryPath))
        {
            var detail = connectionAttempted
                ? "Windows established the SMB session but the repository path is not readable. Confirm the share and repository folder permissions."
                : "The repository path is not readable with the current Windows sign-in. Enter an SMB account that has read access, then connect again.";
            return RepositoryAccessResult.Failed(detail);
        }

        var repositoryMetadataPath = Path.Combine(repositoryPath, "metadata", "repository.json");
        if (!File.Exists(repositoryMetadataPath))
        {
            return RepositoryAccessResult.ConnectedButNotReady(
                "Connected to the SMB share, but this is not an NI Setup prototype repository.",
                "metadata\\repository.json was not found. The installer will not consume raw package files from an arbitrary share.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(repositoryMetadataPath));
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var state = root.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
            if (!string.Equals(id, ExpectedRepositoryId, StringComparison.Ordinal))
            {
                return RepositoryAccessResult.ConnectedButNotReady(
                    "Connected to the SMB share, but the repository identity is not recognized.",
                    "The installer will not consume unverified source content.");
            }

            var objectRoot = Path.Combine(repositoryPath, "objects", "sha256");
            var objectCount = Directory.Exists(objectRoot)
                ? Directory.EnumerateFiles(objectRoot, "*", SearchOption.AllDirectories).Count()
                : 0;
            var catalogRoot = Path.Combine(repositoryPath, "metadata", "catalogs");
            var catalogCount = Directory.Exists(catalogRoot)
                ? Directory.EnumerateFiles(catalogRoot, "*.json", SearchOption.TopDirectoryOnly).Count()
                : 0;

            if (!string.Equals(state, "ready", StringComparison.OrdinalIgnoreCase) || catalogCount == 0)
            {
                return RepositoryAccessResult.ConnectedButNotReady(
                    "Connected to the NI Setup source repository.",
                    $"Repository state: {state ?? "unknown"}. Found {objectCount} source object(s) and {catalogCount} approved catalog(s). A reviewed catalog and deployment executor are still required before installation can begin.");
            }

            return RepositoryAccessResult.Ready(
                "Connected to the NI Setup source repository.",
                $"Found {objectCount} source object(s) and {catalogCount} approved catalog(s). The connection is ready for a future deployment executor.");
        }
        catch (JsonException)
        {
            return RepositoryAccessResult.ConnectedButNotReady(
                "Connected to the SMB share, but repository metadata is invalid.",
                "The installer will not consume source content until repository metadata is repaired and reviewed.");
        }
        catch (IOException exception)
        {
            return RepositoryAccessResult.ConnectedButNotReady(
                "Connected to the SMB share, but repository metadata could not be read.",
                exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return RepositoryAccessResult.ConnectedButNotReady(
                "Connected to the SMB share, but the repository contents are not readable.",
                "Use an SMB account with read access to the NISetupPrototypeRepository folder.");
        }
    }

    private static bool CanRead(string repositoryPath)
    {
        try
        {
            return Directory.Exists(repositoryPath);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryGetShareRoot(string repositoryPath, out string shareRoot, out string error)
    {
        shareRoot = string.Empty;
        error = string.Empty;
        var path = repositoryPath.Trim().TrimEnd('\\', '/');
        if (!path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            error = "Use a Windows UNC path, for example \\192.168.68.125\\Files\\NISetupPrototypeRepository.";
            return false;
        }

        var segments = path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            error = "The repository path must include both the SMB server and share name.";
            return false;
        }

        if (!string.Equals(segments[0], "192.168.68.125", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[1], "Files", StringComparison.OrdinalIgnoreCase))
        {
            error = "This prototype is configured only for \\192.168.68.125\\Files. Do not connect it to an unreviewed share.";
            return false;
        }

        shareRoot = $"\\\\{segments[0]}\\{segments[1]}";
        return true;
    }

    private static int Connect(string shareRoot, string? userName, string? password)
    {
        var resource = new NetResource
        {
            Scope = 0,
            Type = 1,
            DisplayType = 0,
            Usage = 0,
            LocalName = null,
            RemoteName = shareRoot,
            Comment = null,
            Provider = null
        };
        return WNetAddConnection2(ref resource, string.IsNullOrWhiteSpace(password) ? null : password, string.IsNullOrWhiteSpace(userName) ? null : userName, 4);
    }

    private static string DescribeWindowsNetworkError(int errorCode) => errorCode switch
    {
        5 => "Windows denied access to the SMB share. Enter an account with read access to \\192.168.68.125\\Files.",
        53 => "Windows cannot find 192.168.68.125. Confirm this computer is on the same LAN and that SMB is reachable.",
        64 => "The SMB connection was interrupted. Check the network connection and try again.",
        67 => "Windows cannot find the Files share on 192.168.68.125.",
        86 => "Windows rejected the SMB password. Enter the password again and retry.",
        1219 => "Windows already has an SMB session to 192.168.68.125 under another account. Disconnect that existing session in Windows, then retry with one account.",
        1231 => "Windows cannot reach the SMB network. Confirm that this computer is on the LAN and that VPN/firewall policy permits SMB.",
        1326 => "Windows rejected the SMB user name or password. Use the NAS account name in the form required by the NAS.",
        _ => $"Windows could not connect to the SMB share (error {errorCode})."
    };

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? userName, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }
}

public sealed record RepositoryAccessResult(bool IsConnected, bool IsReadyForInstallation, string Status, string Details)
{
    public static RepositoryAccessResult Failed(string details) => new(false, false, "Unable to connect to the source repository.", details);
    public static RepositoryAccessResult ConnectedButNotReady(string status, string details) => new(true, false, status, details);
    public static RepositoryAccessResult Ready(string status, string details) => new(true, true, status, details);
}
