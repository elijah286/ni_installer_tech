using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NIInstallerTech.Services;

namespace NIInstallerTech.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private SetupScenario _scenario;
    private readonly SmbRepositoryService _repositoryService = new();

    public MainViewModel()
    {
        Components = new ObservableCollection<SetupComponent>();
        ConfigurePlan(SetupScenario.Application);
    }

    public ObservableCollection<SetupComponent> Components { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoalsStep))]
    [NotifyPropertyChangedFor(nameof(IsPlanStep))]
    [NotifyPropertyChangedFor(nameof(IsReviewStep))]
    [NotifyPropertyChangedFor(nameof(IsInstallingStep))]
    [NotifyPropertyChangedFor(nameof(IsCompleteStep))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    private int _currentStep;

    [ObservableProperty]
    private string _planName = string.Empty;

    [ObservableProperty]
    private string _planIntroduction = string.Empty;

    [ObservableProperty]
    private string _planSummary = string.Empty;

    [ObservableProperty]
    private string _downloadSize = string.Empty;

    [ObservableProperty]
    private string _installSize = string.Empty;

    [ObservableProperty]
    private string _impactMessage = string.Empty;

    [ObservableProperty]
    private string _adminRequirement = string.Empty;

    [ObservableProperty]
    private string _restartRequirement = string.Empty;

    [ObservableProperty]
    private string _reviewNotice = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNiHostedDelivery))]
    [NotifyPropertyChangedFor(nameof(IsOfflineBundleDelivery))]
    [NotifyPropertyChangedFor(nameof(DeliveryTitle))]
    [NotifyPropertyChangedFor(nameof(DeliveryDescription))]
    [NotifyPropertyChangedFor(nameof(DeliveryDetail))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionLabel))]
    [NotifyPropertyChangedFor(nameof(CompletionHeading))]
    private DeliveryMode _selectedDeliveryMode = DeliveryMode.NiHosted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLabVIEWQ1Selected))]
    [NotifyPropertyChangedFor(nameof(IsLabVIEWQ3Selected))]
    [NotifyPropertyChangedFor(nameof(SelectedLabVIEWReleaseLabel))]
    private LabVIEWRelease _selectedLabVIEWRelease = LabVIEWRelease.Q3;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string _installationStatus = string.Empty;

    [ObservableProperty]
    private string _repositoryPath = @"\\192.168.68.125\Files\NISetupPrototypeRepository";

    [ObservableProperty]
    private string _repositoryUserName = string.Empty;

    [ObservableProperty]
    private string _repositoryPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepositoryStatusColor))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionLabel))]
    private bool _repositoryIsConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryActionLabel))]
    private bool _repositoryIsReadyForInstallation;

    [ObservableProperty]
    private string _repositoryStatus = "Not connected to the source repository.";

    [ObservableProperty]
    private string _repositoryDetails = "Use your current Windows sign-in first, or provide an SMB account with read-only access. Credentials are used only for this connection and are never saved.";

    public bool IsGoalsStep => CurrentStep == 0;
    public bool IsPlanStep => CurrentStep == 1;
    public bool IsReviewStep => CurrentStep == 2;
    public bool IsInstallingStep => CurrentStep == 3;
    public bool IsCompleteStep => CurrentStep == 4;

    public string StepLabel => CurrentStep switch
    {
        0 => "Choose your goals",
        1 => "Review your plan",
        2 => "Confirm the change",
        3 => "Applying your plan",
        _ => "Setup complete"
    };

    public bool IsNiHostedDelivery => SelectedDeliveryMode == DeliveryMode.NiHosted;
    public bool IsOfflineBundleDelivery => SelectedDeliveryMode == DeliveryMode.OfflineBundle;
    public bool IsInstallationExecutorAvailable => false;
    public string RepositoryStatusColor => RepositoryIsConnected ? "#197449" : "#A52A2A";

    public string DeliveryTitle => IsNiHostedDelivery
        ? "Download from NI"
        : "Create an offline installer";

    public string DeliveryDescription => IsNiHostedDelivery
        ? "A small setup app retrieves only the selected, validated components from the NI-hosted catalog."
        : "Download the complete selected plan now and create one portable installer for disconnected systems.";

    public string DeliveryDetail => IsNiHostedDelivery
        ? "Default • NI-hosted catalog • downloads only what this workstation needs"
        : "Portable bundle • includes selected source components • no network needed on the destination";

    public string PrimaryActionLabel => !RepositoryIsConnected
        ? "Connect source to continue"
        : !RepositoryIsReadyForInstallation
            ? "Source requires catalog approval"
            : "Deployment executor unavailable";
    public string CompletionHeading => IsNiHostedDelivery ? "Your setup is ready" : "Your offline installer is ready";

    public bool IsLabVIEWQ1Selected => SelectedLabVIEWRelease == LabVIEWRelease.Q1;
    public bool IsLabVIEWQ3Selected => SelectedLabVIEWRelease == LabVIEWRelease.Q3;
    public string SelectedLabVIEWReleaseLabel => SelectedLabVIEWRelease == LabVIEWRelease.Q1 ? "LabVIEW 2026 Q1 x64" : "LabVIEW 2026 Q3 x64";
    public int SelectedComponentCount => Components.Count(component => component.IsSelected);
    public string SelectedDownloadSize => FormatSize(Components.Where(component => component.IsSelected).Sum(component => component.SizeMb), "download");
    public string SelectedInstallSize => FormatSize((int)(Components.Where(component => component.IsSelected).Sum(component => component.SizeMb) * 1.25), "installed");
    public string SelectedAdminRequirement => Components.Where(component => component.IsSelected).Any(component => component.RequiresElevation)
        ? "Required only for the selected driver, service, or firmware boundary"
        : "Not expected for this user-mode plan";
    public string SelectedRestartRequirement => Components.Where(component => component.IsSelected).Any(component => component.MayRequireRestart)
        ? "Possible — driver or firmware activation is selected"
        : "Not expected";
    public string CoexistenceSummary => "One LabVIEW release is selected. User-mode adapters can coexist when explicitly compatible; drivers, services, configuration schemas, and firmware remain one active version.";

    public IEnumerable<string> SelectedComponentNames => Components
        .Where(component => component.IsSelected)
        .Select(component => component.Name);

    public string CompletionMessage => _scenario switch
    {
        SetupScenario.Application => IsNiHostedDelivery
            ? "LabVIEW, NI Measurement & Automation Explorer, and the selected NI-DAQmx planes are ready. Hardware, firmware, and optional applications remain separate until you select them."
            : "A portable offline installer contains the selected LabVIEW, MAX, and NI-DAQmx component planes. It can be taken to a disconnected system without carrying licenses or machine configuration.",
        SetupScenario.Hardware => IsNiHostedDelivery
            ? "Your core NI software and selected family-level hardware support are ready. Driver and firmware boundaries were reviewed separately."
            : "A portable offline installer contains the selected core and hardware-family support. The destination still reviews driver or firmware activation boundaries.",
        _ => IsNiHostedDelivery
            ? "Your core NI software, selected test applications, and chosen hardware support are ready."
            : "A portable offline installer contains the selected automated-test workstation plan."
    };

    [RelayCommand]
    private void ChooseApiOnly()
    {
        ConfigurePlan(SetupScenario.Application);
        CurrentStep = 1;
    }

    [RelayCommand]
    private void ChooseDeviceSupport()
    {
        ConfigurePlan(SetupScenario.Hardware);
        CurrentStep = 1;
    }

    [RelayCommand]
    private void ChooseTestSystem()
    {
        ConfigurePlan(SetupScenario.TestSystem);
        CurrentStep = 1;
    }

    [RelayCommand]
    private void ChooseNiHostedDelivery() => SelectedDeliveryMode = DeliveryMode.NiHosted;

    [RelayCommand]
    private void ChooseOfflineBundle() => SelectedDeliveryMode = DeliveryMode.OfflineBundle;

    [RelayCommand]
    private async Task ConnectRepository()
    {
        RepositoryStatus = "Connecting to the source repository…";
        RepositoryDetails = "Windows is attempting the configured SMB connection.";
        RepositoryIsConnected = false;
        RepositoryIsReadyForInstallation = false;

        var password = RepositoryPassword;
        try
        {
            var result = await Task.Run(() => _repositoryService.ConnectAndVerify(RepositoryPath, RepositoryUserName, password));
            RepositoryIsConnected = result.IsConnected;
            RepositoryIsReadyForInstallation = result.IsReadyForInstallation;
            RepositoryStatus = result.Status;
            RepositoryDetails = result.Details;
        }
        finally
        {
            RepositoryPassword = string.Empty;
        }
    }

    [RelayCommand]
    private void SelectLabVIEWQ1()
    {
        if (SelectedLabVIEWRelease == LabVIEWRelease.Q1) return;
        SelectedLabVIEWRelease = LabVIEWRelease.Q1;
        ConfigurePlan(_scenario);
    }

    [RelayCommand]
    private void SelectLabVIEWQ3()
    {
        if (SelectedLabVIEWRelease == LabVIEWRelease.Q3) return;
        SelectedLabVIEWRelease = LabVIEWRelease.Q3;
        ConfigurePlan(_scenario);
    }

    [RelayCommand]
    private void Continue() => CurrentStep = 2;

    [RelayCommand]
    private void Back() => CurrentStep = CurrentStep == 2 ? 1 : 0;

    [RelayCommand]
    private void StartInstall()
    {
        if (!RepositoryIsConnected)
        {
            RepositoryStatus = "Connect to the source repository before starting a plan.";
            RepositoryDetails = "Open Organization-approved repository, verify \\192.168.68.125\\Files, and connect with the current Windows sign-in or an approved SMB account.";
            return;
        }

        if (!RepositoryIsReadyForInstallation)
        {
            RepositoryStatus = "The source is connected but is not approved for installation.";
            RepositoryDetails = "No reviewed catalog and supported deployment executor are currently available. No files were copied and no machine state was changed.";
            return;
        }

        RepositoryStatus = "The source is connected and catalog-ready, but no Windows deployment executor has been released.";
        RepositoryDetails = "No files were copied and no machine state was changed. A supported executor must validate component signatures, dependencies, ownership, activation boundaries, and health checks before an actual installation can run.";
    }

    partial void OnRepositoryPathChanged(string value)
    {
        RepositoryIsConnected = false;
        RepositoryIsReadyForInstallation = false;
        RepositoryStatus = "Not connected to the source repository.";
        RepositoryDetails = "Verify the configured UNC path before continuing.";
    }

    [RelayCommand]
    private void StartOver()
    {
        CurrentStep = 0;
        InstallProgress = 0;
        InstallationStatus = string.Empty;
        SelectedDeliveryMode = DeliveryMode.NiHosted;
    }

    private void ConfigurePlan(SetupScenario scenario)
    {
        _scenario = scenario;
        Components.Clear();

        var release = SelectedLabVIEWRelease == LabVIEWRelease.Q1 ? "2026 Q1" : "2026 Q3";
        AddComponent($"LabVIEW {release} x64", "Selected application release observed on the reference system. One LabVIEW release is selected for this plan.", "1.2 GB", "Application", "User-mode; one release selected", false, "labview-core", "One selected release");
        AddComponent("NI Measurement & Automation Explorer 26.5", "Configuration and discovery plane. Current device configuration is never copied into the plan.", "94 MB", "Configuration", "One active configuration schema", false, "max-configuration", "Singleton");
        AddComponent("NI-DAQmx 26.0 user-mode runtime", "API/runtime plane for NI data acquisition. It remains separate from hardware, driver, and firmware activation.", "124 MB", "API runtime", "User-mode; revision-compatible", false, "daqmx-user-mode", "Side-by-side when compatible");
        AddComponent($"NI-DAQmx LabVIEW {release} adapter", "LabVIEW API, palettes, and integration bound to the selected LabVIEW ABI.", "42 MB", "Language adapter", "User-mode; bound to selected release", false, $"daqmx-labview-{release.Replace(" ", "-").ToLowerInvariant()}", "Side-by-side when compatible");
        AddComponent("NI-DAQmx documentation and examples", "Optional local help and examples; removable without changing runtime or device support.", "140 MB", "Optional content", "No machine impact", true);

        if (scenario == SetupScenario.Application)
        {
            AddComponent("NI-VISA runtime", "Instrument communication support for PXI, USB, Ethernet, serial, and GPIB instruments.", "118 MB", "API runtime", "User-mode only", true);
            AddComponent("TestStand runtime", "Execute automated test sequences; stays separate from LabVIEW and driver updates when compatible.", "920 MB", "Application", "User-mode only", true);
            AddComponent("InstrumentStudio", "Interactively configure and measure with supported PXI instruments.", "560 MB", "Application", "User-mode only", true);
            AddComponent("FlexLogger", "Configure and log sensor measurements without programming.", "410 MB", "Application", "User-mode only", true);
            AddComponent("DIAdem", "Find, inspect, analyze, and report measurement data.", "730 MB", "Application", "User-mode only", true);
        }

        if (scenario is SetupScenario.Hardware or SetupScenario.TestSystem)
        {
            AddComponent("NI-VISA runtime", "Instrument communication support for PXI, USB, Ethernet, serial, and GPIB instruments.", "118 MB", "API runtime", "User-mode only", false);
            AddComponent("PXI Platform Services", "Platform services for compatible PXI chassis, controllers, and modules.", "86 MB", "Platform service", "One active service version; may require elevation", true, "pxi-platform-service", "Singleton");
            AddComponent("DAQmx local MIO support", "Family-level support for local PCI/PCIe and USB DAQ. The final plan separately stages signed driver packages.", "210 MB", "Hardware family", "One active driver domain", true, "daqmx-local-mio-driver", "Singleton");
            AddComponent("CompactDAQ / FieldDAQ support", "Family-level Ethernet and CompactDAQ support. Firmware is not included by default.", "260 MB", "Hardware family", "One active driver domain", true, "daqmx-cdaq-driver", "Singleton");
            AddComponent("PXI instrument support", "Select only the instrument families used in this chassis; platform and device drivers remain explicit.", "295 MB", "Hardware family", "One active driver domain", true, "pxi-instrument-driver", "Singleton");
            AddComponent("CompactDAQ firmware", "Optional firmware plane for eligible devices. Requires a separate device-specific confirmation.", "240 MB", "Firmware", "One active device firmware revision; explicit approval", true, "cdaq-firmware", "Singleton");
        }

        if (scenario == SetupScenario.TestSystem)
        {
            AddComponent("TestStand runtime", "Configure and execute automated test sequences.", "920 MB", "Application", "User-mode only", false);
            AddComponent("InstrumentStudio", "Interactively configure and measure with supported PXI instruments.", "560 MB", "Application", "User-mode only", true);
            AddComponent("SystemLink client", "Connect this station to test operations and asset-management workflows.", "210 MB", "Platform service", "Service boundary", true);
        }

        PlanName = scenario switch
        {
            SetupScenario.Application => "Recommended NI setup",
            SetupScenario.Hardware => "Core NI software + selected support",
            _ => "Automated test workstation"
        };
        PlanIntroduction = scenario switch
        {
            SetupScenario.Application => "The best starting point for most NI developers and measurement users.",
            SetupScenario.Hardware => "Start with the core NI setup, then select the instrument, RF, or industrial communication support you need.",
            _ => "A complete starting point for PXI, DAQ, and automated test workflows."
        };
        PlanSummary = scenario switch
        {
            SetupScenario.Application => "Includes LabVIEW, NI Measurement & Automation Explorer, and NI-DAQmx. Add applications and hardware support only when useful.",
            SetupScenario.Hardware => "Includes the core NI setup plus support for instruments, PXI, RF, or industrial protocols.",
            _ => "Includes the core NI setup, TestStand, and selected hardware support for an automated test station."
        };
        RefreshPlanMetrics();
        ImpactMessage = scenario == SetupScenario.Application
            ? "The default foundation is user-mode only. Device support, signed drivers, and firmware stay outside the plan until you select a hardware family."
            : "Hardware-family packs are separate from APIs and applications. The final installer checks Windows, device, driver, and firmware compatibility before activation.";
        AdminRequirement = scenario == SetupScenario.Application ? "Not expected for this user-mode plan" : "Only if selected hardware support activates";
        RestartRequirement = "Not expected";
        ReviewNotice = scenario == SetupScenario.Application
            ? "Optional applications are not installed unless you select them. Current licensing and activation tooling remains unchanged and no license or machine configuration is placed in an offline bundle."
            : "Hardware, signed driver, service, and firmware items are deliberate boundaries. The final installer will require explicit review if a selected item needs elevation, restart, or device activation.";
    }

    private void AddComponent(string name, string description, string size, string plane, string changeBoundary, bool isOptional, string upgradeDomain = "user-mode", string coexistencePolicy = "Side-by-side when compatible")
    {
        var component = new SetupComponent(name, description, size, plane, changeBoundary, isOptional, upgradeDomain, coexistencePolicy);
        component.PropertyChanged += ComponentChanged;
        Components.Add(component);
    }

    private void ComponentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupComponent.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedComponentNames));
            RefreshPlanMetrics();
        }
    }

    private void RefreshPlanMetrics()
    {
        DownloadSize = SelectedDownloadSize;
        InstallSize = SelectedInstallSize;
        AdminRequirement = SelectedAdminRequirement;
        RestartRequirement = SelectedRestartRequirement;
        OnPropertyChanged(nameof(SelectedComponentCount));
        OnPropertyChanged(nameof(SelectedDownloadSize));
        OnPropertyChanged(nameof(SelectedInstallSize));
        OnPropertyChanged(nameof(SelectedAdminRequirement));
        OnPropertyChanged(nameof(SelectedRestartRequirement));
        OnPropertyChanged(nameof(CoexistenceSummary));
    }

    private static string FormatSize(int megabytes, string suffix)
        => megabytes >= 1024 ? $"{megabytes / 1024d:0.0} GB {suffix}" : $"{megabytes} MB {suffix}";
}

public partial class SetupComponent : ObservableObject
{
    public SetupComponent(string name, string description, string size, string plane, string changeBoundary, bool isOptional, string upgradeDomain, string coexistencePolicy)
    {
        Name = name;
        Description = description;
        Size = size;
        Plane = plane;
        ChangeBoundary = changeBoundary;
        IsOptional = isOptional;
        UpgradeDomain = upgradeDomain;
        CoexistencePolicy = coexistencePolicy;
        SizeMb = ParseSizeMb(size);
        IsSelected = true;
    }

    public string Name { get; }
    public string Description { get; }
    public string Size { get; }
    public string Plane { get; }
    public string ChangeBoundary { get; }
    public bool IsOptional { get; }
    public bool IsRequired => !IsOptional;
    public int SizeMb { get; }
    public string UpgradeDomain { get; }
    public string CoexistencePolicy { get; }
    public bool IsSingleton => CoexistencePolicy == "Singleton" || CoexistencePolicy == "One selected release";
    public bool RequiresElevation => ChangeBoundary.Contains("elevation", StringComparison.OrdinalIgnoreCase) || ChangeBoundary.Contains("driver", StringComparison.OrdinalIgnoreCase) || ChangeBoundary.Contains("firmware", StringComparison.OrdinalIgnoreCase);
    public bool MayRequireRestart => ChangeBoundary.Contains("driver", StringComparison.OrdinalIgnoreCase) || ChangeBoundary.Contains("firmware", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _isSelected;

    private static int ParseSizeMb(string size)
    {
        var parts = size.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var value = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        return parts[1] == "GB" ? (int)(value * 1024) : (int)value;
    }
}

public enum SetupScenario
{
    Application,
    Hardware,
    TestSystem
}

public enum DeliveryMode
{
    NiHosted,
    OfflineBundle
}

public enum LabVIEWRelease
{
    Q1,
    Q3
}
