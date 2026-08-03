[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$MsiPath,

    [string]$RepositoryRoot = '/Volumes/Files/NISetupPrototypeRepository',

    [string]$ReleaseNotes = ''
)

$ErrorActionPreference = 'Stop'
$msi = Get-Item -LiteralPath $MsiPath
if ($msi.Extension -ne '.msi') {
    throw 'MsiPath must name a Windows Installer (.msi) package.'
}

$updatesDirectory = Join-Path $RepositoryRoot 'updates'
$packagesDirectory = Join-Path $updatesDirectory 'packages'
New-Item -ItemType Directory -Force -Path $packagesDirectory | Out-Null

$packageName = "NI-Platform-Setup-win-x64-$Version.msi"
$packagePath = Join-Path $packagesDirectory $packageName
Copy-Item -LiteralPath $msi.FullName -Destination $packagePath -Force

$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumName = "$packageName.sha256"
$checksumPath = Join-Path $packagesDirectory $checksumName
Set-Content -LiteralPath $checksumPath -Value "$hash  $packageName" -NoNewline

$manifest = [ordered]@{
    version = $Version
    packageUrl = "packages/$packageName"
    checksumUrl = "packages/$checksumName"
    notes = $ReleaseNotes
}
$temporaryManifestPath = Join-Path $updatesDirectory 'latest.json.tmp'
$manifestPath = Join-Path $updatesDirectory 'latest.json'
$manifest | ConvertTo-Json | Set-Content -LiteralPath $temporaryManifestPath -NoNewline
Move-Item -LiteralPath $temporaryManifestPath -Destination $manifestPath -Force

Write-Host "Published NI Setup $Version to $manifestPath"