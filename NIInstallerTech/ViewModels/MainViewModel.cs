using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NIInstallerTech.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private SetupScenario _scenario;

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
    private double _installProgress;

    [ObservableProperty]
    private string _installationStatus = string.Empty;

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

    public string DeliveryTitle => IsNiHostedDelivery
        ? "Download from NI"
        : "Create an offline installer";

    public string DeliveryDescription => IsNiHostedDelivery
        ? "A small setup app retrieves only the selected, validated components from the NI-hosted catalog."
        : "Download the complete selected plan now and create one portable installer for disconnected systems.";

    public string DeliveryDetail => IsNiHostedDelivery
        ? "Default • NI-hosted catalog • downloads only what this workstation needs"
        : "Portable bundle • includes selected source components • no network needed on the destination";

    public string PrimaryActionLabel => IsNiHostedDelivery ? "Install selected setup" : "Create offline installer";
    public string CompletionHeading => IsNiHostedDelivery ? "Your setup is ready" : "Your offline installer is ready";

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
    private void Continue() => CurrentStep = 2;

    [RelayCommand]
    private void Back() => CurrentStep = CurrentStep == 2 ? 1 : 0;

    [RelayCommand]
    private async Task StartInstall()
    {
        CurrentStep = 3;
        InstallProgress = 0;

        var stages = new[]
            {
                "Checking the selected component plan…",
                "Retrieving selected components from the NI catalog…",
                "Preparing your workstation…",
                "Adding selected software planes…",
                "Recording the completed component state…"
            };

        if (IsOfflineBundleDelivery)
        {
            stages =
            [
                "Checking the selected component plan…",
                "Retrieving selected components from the NI catalog…",
                "Verifying component digests and source evidence…",
                "Creating one portable offline installer…",
                "Recording the bundle contents and destination requirements…"
            ];
        }

        for (var stage = 0; stage < stages.Length; stage++)
        {
            InstallationStatus = stages[stage];
            await Task.Delay(650);
            InstallProgress = (stage + 1) * 20;
        }

        await Task.Delay(450);
        CurrentStep = 4;
        OnPropertyChanged(nameof(CompletionMessage));
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

        AddComponent("LabVIEW 2026 Q3 x64", "Application plane observed on the reference system. Create measurement, test, and control applications.", "1.2 GB", "Application", "User-mode only", false);
        AddComponent("NI Measurement & Automation Explorer 26.5", "Configuration and discovery plane. Current device configuration is never copied into the plan.", "94 MB", "Configuration", "User-mode only", false);
        AddComponent("NI-DAQmx 26.0 user-mode runtime", "API/runtime plane for NI data acquisition. It remains separate from hardware, driver, and firmware activation.", "124 MB", "API runtime", "User-mode only", false);
        AddComponent("NI-DAQmx LabVIEW adapter", "LabVIEW API, palettes, and integration for the selected LabVIEW ABI.", "42 MB", "Language adapter", "User-mode only", false);
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
            AddComponent("PXI Platform Services", "Platform services for compatible PXI chassis, controllers, and modules.", "86 MB", "Platform service", "May require elevation", true);
            AddComponent("DAQmx local MIO support", "Family-level support for local PCI/PCIe and USB DAQ. The final plan separately stages signed driver packages.", "210 MB", "Hardware family", "Driver boundary", true);
            AddComponent("CompactDAQ / FieldDAQ support", "Family-level Ethernet and CompactDAQ support. Firmware is not included by default.", "260 MB", "Hardware family", "Driver boundary", true);
            AddComponent("PXI instrument support", "Select only the instrument families used in this chassis; platform and device drivers remain explicit.", "295 MB", "Hardware family", "Driver boundary", true);
            AddComponent("CompactDAQ firmware", "Optional firmware plane for eligible devices. Requires a separate device-specific confirmation.", "240 MB", "Firmware", "Explicit approval", true);
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
        DownloadSize = scenario switch
        {
            SetupScenario.Application => "1.8 GB download",
            SetupScenario.Hardware => "2.5 GB download",
            _ => "3.8 GB download"
        };
        InstallSize = scenario switch
        {
            SetupScenario.Application => "2.6 GB",
            SetupScenario.Hardware => "3.7 GB",
            _ => "5.7 GB"
        };
        ImpactMessage = scenario == SetupScenario.Application
            ? "The default foundation is user-mode only. Device support, signed drivers, and firmware stay outside the plan until you select a hardware family."
            : "Hardware-family packs are separate from APIs and applications. The final installer checks Windows, device, driver, and firmware compatibility before activation.";
        AdminRequirement = scenario == SetupScenario.Application ? "Not expected for this user-mode plan" : "Only if selected hardware support activates";
        RestartRequirement = "Not expected";
        ReviewNotice = scenario == SetupScenario.Application
            ? "Optional applications are not installed unless you select them. Current licensing and activation tooling remains unchanged and no license or machine configuration is placed in an offline bundle."
            : "Hardware, signed driver, service, and firmware items are deliberate boundaries. The final installer will require explicit review if a selected item needs elevation, restart, or device activation.";
    }

    private void AddComponent(string name, string description, string size, string plane, string changeBoundary, bool isOptional)
    {
        var component = new SetupComponent(name, description, size, plane, changeBoundary, isOptional);
        component.PropertyChanged += ComponentChanged;
        Components.Add(component);
    }

    private void ComponentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupComponent.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedComponentNames));
        }
    }
}

public partial class SetupComponent : ObservableObject
{
    public SetupComponent(string name, string description, string size, string plane, string changeBoundary, bool isOptional)
    {
        Name = name;
        Description = description;
        Size = size;
        Plane = plane;
        ChangeBoundary = changeBoundary;
        IsOptional = isOptional;
        IsSelected = true;
    }

    public string Name { get; }
    public string Description { get; }
    public string Size { get; }
    public string Plane { get; }
    public string ChangeBoundary { get; }
    public bool IsOptional { get; }
    public bool IsRequired => !IsOptional;

    [ObservableProperty]
    private bool _isSelected;
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
