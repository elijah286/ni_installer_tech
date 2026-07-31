<#
.SYNOPSIS
  Read-only inventory collector for an NI Windows reference machine.
.DESCRIPTION
  Writes a redacted JSON evidence file. It collects operating-system facts,
  NI-related installed-product records, present NI devices, installed NI drivers,
  NI-related services, and selected configuration evidence. It does not modify
  installed software, device state, licensing, or configuration.
#>
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $env:USERPROFILE 'NIReferenceInventory.json')
)

$ErrorActionPreference = 'Continue'

function Convert-RegistryProperties {
    param([string]$Path)

    if (-not (Test-Path $Path)) { return @() }

    Get-ChildItem -Path $Path -ErrorAction SilentlyContinue | ForEach-Object {
        $item = $_
        $properties = Get-ItemProperty -Path $item.PSPath -ErrorAction SilentlyContinue
        [pscustomobject]@{
            Key = $item.PSChildName
            DisplayName = $properties.DisplayName
            DisplayVersion = $properties.DisplayVersion
            Publisher = $properties.Publisher
            InstallLocation = $properties.InstallLocation
            UninstallString = $null # intentionally excluded: may include sensitive custom arguments
            WindowsInstaller = $properties.WindowsInstaller
        }
    } | Where-Object {
        $_.DisplayName -match '(?i)national instruments|\bni[- ]|labview|teststand|flexlogger|instrumentstudio|diadem|systemlink|measurement[ ]*&[ ]*automation|daqmx|visa|pxi|rfsa|rfsg|ni-scope|ni-fgen|ni-rio|ni-488'
    }
}

$uninstallRoots = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
)

$niServices = Get-CimInstance Win32_Service -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -match '(?i)^ni|national instruments|daq|visa|pxi|labview|systemlink|teststand' -or
        $_.DisplayName -match '(?i)national instruments|\bni[- ]|daq|visa|pxi|labview|systemlink|teststand'
    } |
    Select-Object Name, DisplayName, State, StartMode, StartName, PathName

$niDevices = Get-PnpDevice -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Manufacturer -match '(?i)national instruments|\bni\b' -or
        $_.FriendlyName -match '(?i)national instruments|\bni\b|compactdaq|compactrio|pxi'
    } |
    ForEach-Object {
        $device = $_
        [pscustomobject]@{
            Status = $device.Status
            Class = $device.Class
            FriendlyName = $device.FriendlyName
            InstanceId = $device.InstanceId
            Manufacturer = $device.Manufacturer
            HardwareIds = @(Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_HardwareIds' -ErrorAction SilentlyContinue).Data
            CompatibleIds = @(Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_CompatibleIds' -ErrorAction SilentlyContinue).Data
            DriverProvider = (Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_DriverProvider' -ErrorAction SilentlyContinue).Data
            DriverVersion = (Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_DriverVersion' -ErrorAction SilentlyContinue).Data
            DriverInfPath = (Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_DriverInfPath' -ErrorAction SilentlyContinue).Data
        }
    }

$niDrivers = Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Manufacturer -match '(?i)national instruments|\bni\b' -or
        $_.DriverProviderName -match '(?i)national instruments|\bni\b' -or
        $_.DeviceName -match '(?i)national instruments|\bni\b|compactdaq|compactrio|pxi'
    } |
    Select-Object DeviceName, DeviceID, Manufacturer, DriverProviderName, DriverVersion, DriverDate, InfName, DriverName, IsSigned

$niDirectories = @(
    "$env:ProgramFiles\National Instruments",
    "${env:ProgramFiles(x86)}\National Instruments",
    "$env:ProgramData\National Instruments",
    "$env:PUBLIC\Documents\National Instruments"
) | Where-Object { $_ -and (Test-Path $_) } | ForEach-Object {
    $path = $_
    [pscustomobject]@{
        Path = $path
        FileCount = @(Get-ChildItem -Path $path -Recurse -File -ErrorAction SilentlyContinue).Count
        TotalBytes = [long](@(Get-ChildItem -Path $path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum)
    }
}

$computer = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem

$inventory = [ordered]@{
    schemaVersion = '0.1'
    collectedAtUtc = [DateTime]::UtcNow.ToString('o')
    collector = [ordered]@{ name = 'Collect-NIReferenceInventory'; mode = 'read-only'; redaction = 'No credentials, license files, uninstall command lines, or user data collected.' }
    machine = [ordered]@{
        name = $env:COMPUTERNAME
        architecture = $env:PROCESSOR_ARCHITECTURE
        domainOrWorkgroup = $computer.Domain
        manufacturer = $computer.Manufacturer
        model = $computer.Model
        osCaption = $os.Caption
        osVersion = $os.Version
        osBuild = $os.BuildNumber
    }
    installedProducts = @($uninstallRoots | ForEach-Object { Convert-RegistryProperties $_ })
    niDirectories = @($niDirectories)
    niServices = @($niServices)
    niDevices = @($niDevices)
    niSignedDrivers = @($niDrivers)
    pendingRebootIndicators = [ordered]@{
        componentBasedServicing = Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'
        windowsUpdate = Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
        pendingFileRenameOperations = $null -ne (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue)
    }
}

$inventory | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding utf8
Write-Host "Wrote read-only inventory: $OutputPath"
