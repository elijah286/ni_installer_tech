# Internal update feed

NI Setup reads this manifest from the approved internal repository:

```text
http://192.168.68.125:8081/Files/NISetupPrototypeRepository/updates/latest.json
```

The manifest contains a semantic version, MSI URL, checksum URL, and optional release notes. URLs may be relative to the manifest:

```json
{
  "version": "0.0.6",
  "packageUrl": "packages/NI-Platform-Setup-win-x64-0.0.6.msi",
  "checksumUrl": "packages/NI-Platform-Setup-win-x64-0.0.6.msi.sha256",
  "notes": "Internal update feed enabled."
}
```

After GitHub Actions publishes a Windows release, download its MSI using an authorized maintainer account and publish it to the internal share:

```powershell
pwsh ./tools/Publish-NISetupUpdateFeed.ps1 `
  -Version 0.0.6 `
  -MsiPath ./NI-Platform-Setup-win-x64.msi `
  -ReleaseNotes 'Internal update feed enabled.'
```

The script copies the MSI to `updates/packages`, computes its SHA-256 checksum, and atomically replaces `updates/latest.json`. GitHub-hosted Actions cannot directly publish to the private LAN share, so this trusted mirror step is required for each release.