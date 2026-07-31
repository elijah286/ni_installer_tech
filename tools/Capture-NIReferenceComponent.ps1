<#
.SYNOPSIS
  Stages a reviewed, allowlisted reference-machine component capture for the NI Setup POC.

.DESCRIPTION
  This tool is designed to run on the Windows reference machine or a controlled Windows
  staging host with access to its approved source paths. It is read-only with respect to
  the source. It defaults to discovery mode: it writes manifests only. Copying payload
  requires -CopyPayload and writes only to the configured POC repository.

  It is not a production packager and does not make captured content redistributable.
  It refuses activation/licensing/entitlement material, customer data, ProgramData,
  raw Driver Store content, registry exports, and common secret locations.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$ManifestPath,

    [Parameter(Mandatory)]
    [string]$OutputRoot,

    [switch]$CopyPayload,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$prohibitedPathPatterns = @(
    '(?i)(^|\\)programdata(\\|$)',
    '(?i)(^|\\)driverstore(\\|$)',
    '(?i)(^|\\)appdata(\\|$)',
    '(?i)(^|\\)users(\\|$)',
    '(?i)(^|\\)(activation|license|licensing|entitlement|credential|secret|password|private.?key|ssh)(\\|$)',
    '(?i)\.(reg|dat|pfx|p12|pem|key)$'
)

function Test-ProhibitedPath {
    param([Parameter(Mandatory)][string]$Path)
    foreach ($pattern in $prohibitedPathPatterns) {
        if ($Path -match $pattern) { return $true }
    }
    return $false
}

function Write-JsonFile {
    param([Parameter(Mandatory)]$Value, [Parameter(Mandatory)][string]$Path)
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-RelativeDestination {
    param([Parameter(Mandatory)][string]$SourceRoot, [Parameter(Mandatory)][string]$FullName)
    $relative = [System.IO.Path]::GetRelativePath($SourceRoot, $FullName)
    if ($relative.StartsWith('..')) { throw "Source path escapes its reviewed root: $FullName" }
    return $relative.Replace('\', '/')
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
foreach ($required in 'schemaVersion', 'componentId', 'version', 'review', 'sources') {
    if ($null -eq $manifest.$required) { throw "Manifest is missing '$required'." }
}
if ($manifest.review.status -ne 'approved-for-poc-capture') {
    throw "Capture requires review.status 'approved-for-poc-capture'."
}
if ([string]::IsNullOrWhiteSpace($manifest.review.approvedBy)) {
    throw 'Capture requires review.approvedBy.'
}
if ($manifest.sources.Count -eq 0) { throw 'Capture requires at least one reviewed source.' }

$componentRoot = Join-Path $OutputRoot (Join-Path 'incoming-reference-captures' (Join-Path $manifest.componentId $manifest.version))
if ((Test-Path $componentRoot) -and -not $Force) {
    throw "Output already exists: $componentRoot. Use -Force only after review."
}

$stagingRoot = Join-Path $componentRoot 'staging'
$payloadRoot = Join-Path $componentRoot 'payload'
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
if ($CopyPayload) { New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null }

$files = [System.Collections.Generic.List[object]]::new()
$exclusions = [System.Collections.Generic.List[object]]::new()
foreach ($source in $manifest.sources) {
    if ([string]::IsNullOrWhiteSpace($source.path) -or [string]::IsNullOrWhiteSpace($source.role)) {
        throw 'Every source requires path and role.'
    }
    if (Test-ProhibitedPath $source.path) {
        throw "Reviewed source is prohibited by repository policy: $($source.path)"
    }
    if (-not (Test-Path -LiteralPath $source.path)) {
        throw "Reviewed source does not exist: $($source.path)"
    }

    $sourceItem = Get-Item -LiteralPath $source.path
    $sourceRoot = if ($sourceItem.PSIsContainer) { $sourceItem.FullName } else { Split-Path -Parent $sourceItem.FullName }
    $candidates = if ($sourceItem.PSIsContainer) {
        Get-ChildItem -LiteralPath $sourceItem.FullName -File -Recurse
    } else {
        @($sourceItem)
    }

    foreach ($file in $candidates) {
        if (Test-ProhibitedPath $file.FullName) {
            $exclusions.Add([ordered]@{ sourcePath = $file.FullName; reason = 'Matches prohibited repository policy' })
            continue
        }

        $destination = Join-Path $source.role (Get-RelativeDestination $sourceRoot $file.FullName)
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $files.Add([ordered]@{
            sourcePath = $file.FullName
            destination = $destination.Replace('\', '/')
            sha256 = $hash
            sizeBytes = $file.Length
            sourceRole = $source.role
        })

        if ($CopyPayload) {
            $target = Join-Path $payloadRoot $destination
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
}

$totalBytes = ($files | Measure-Object -Property sizeBytes -Sum).Sum
$timestamp = [DateTime]::UtcNow.ToString('o')
Write-JsonFile ([ordered]@{
    schemaVersion = 'reference-poc-manifest-v0.1'
    id = $manifest.componentId
    version = $manifest.version
    classification = 'reference-derived-poc'
    redistributable = $false
    payloadCopied = [bool]$CopyPayload
    files = $files.Count
    bytes = $totalBytes
}) (Join-Path $stagingRoot 'manifest.json')
Write-JsonFile ([ordered]@{ schemaVersion = 'payload-manifest-v0.1'; files = $files; totalBytes = $totalBytes }) (Join-Path $stagingRoot 'payload-manifest.json')
Write-JsonFile ([ordered]@{
    schemaVersion = 'origin-evidence-v0.1'
    capturedAtUtc = $timestamp
    machine = $env:COMPUTERNAME
    operatingSystem = (Get-CimInstance Win32_OperatingSystem).Caption
    sources = $manifest.sources
    evidenceReferences = $manifest.evidenceReferences
}) (Join-Path $stagingRoot 'origin-evidence.json')
Write-JsonFile ([ordered]@{ schemaVersion = 'exclusions-v0.1'; excludedFiles = $exclusions; mandatoryExclusions = @('activation', 'licenses', 'entitlements', 'credentials', 'customer data', 'machine configuration', 'raw Driver Store') }) (Join-Path $stagingRoot 'exclusions.json')
Write-JsonFile ([ordered]@{ schemaVersion = 'resource-claims-v0.1'; confidence = 'candidate'; claims = $manifest.resourceClaims }) (Join-Path $stagingRoot 'resource-claims.json')
Write-JsonFile ([ordered]@{ schemaVersion = 'provenance-v0.1'; tool = 'Capture-NIReferenceComponent.ps1'; capturedAtUtc = $timestamp; mode = if ($CopyPayload) { 'copy-approved-payload' } else { 'dry-run-manifests-only' }; review = $manifest.review }) (Join-Path $stagingRoot 'provenance.json')
Write-JsonFile ([ordered]@{ schemaVersion = 'health-check-v0.1'; status = 'placeholder'; check = 'Define a non-destructive component health check before activation.' }) (Join-Path $stagingRoot 'health-check.json')

Write-Host "Capture manifest complete: $stagingRoot"
Write-Host "Files: $($files.Count); bytes: $totalBytes; payload copied: $([bool]$CopyPayload)"
