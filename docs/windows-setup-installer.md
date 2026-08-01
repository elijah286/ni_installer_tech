# Windows setup installer

## Purpose

`NI Setup` is a small, self-contained Windows doorway that resolves and installs
the selected NI software plan. It is not a monolithic payload bundle and does
not replace NI Package Manager, product installers, activation, or licensing.

The Windows release publishes `NI-Platform-Setup-win-x64.msi`. The MSI contains
only the self-contained launcher and its embedded native rendering dependencies.
Selected product content is still downloaded and verified by the application.

## Windows behavior

- Installs the launcher to `C:\Program Files\NI Setup`.
- Creates an `NI Setup` Start Menu entry.
- Registers repair, uninstall, and major-upgrade behavior in Windows Apps &
  Features.
- Requests UAC for the MSI installation or upgrade because it writes to Program
  Files. The launcher itself remains unelevated until a selected NI operation
  requires elevation.
- New launcher versions download, SHA-256 verify, and invoke a newer MSI through
  Windows Installer after the running application exits.
- A ZIP release remains available temporarily so earlier ZIP-installed clients
  can move forward through their existing update path.

## Build and release

The WiX project is [NIInstallerTech.Installer](../NIInstallerTech.Installer).
The release workflow publishes the self-contained launcher first, then binds it
into an MSI on `windows-latest`. CI performs the same Windows MSI build before a
release is created.

```powershell
dotnet publish NIInstallerTech/NIInstallerTech.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output publish
dotnet build NIInstallerTech.Installer/NIInstallerTech.Installer.wixproj --configuration Release -p:ProductVersion=0.0.1 -p:PublishDirectory="$((Resolve-Path publish).Path)"
```

## Signing gate

The MSI packaging contract is complete, but a customer-facing release still
requires Authenticode signing for both the launcher and MSI with an NI-managed
certificate. The signing key must be held by the approved release system, never
in this repository or in GitHub release assets. Until that release credential is
configured, this remains a prototype distribution path and Windows may present
reputation or publisher warnings.