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

    public IEnumerable<string> SelectedComponentNames => Components
        .Where(component => component.IsSelected)
        .Select(component => component.Name);

    public string CompletionMessage => _scenario switch
    {
        SetupScenario.Application => "LabVIEW, NI Measurement & Automation Explorer, and NI-DAQmx are ready. Add more products or hardware support whenever you need them.",
        SetupScenario.Hardware => "Your core NI software and selected instrument or protocol support are ready.",
        _ => "Your core NI software, selected test applications, and hardware support are ready."
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
            "Checking your selected components…",
            "Preparing your workstation…",
            "Adding selected software…",
            "Saving installation details…",
            "Checking the completed setup…"
        };

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
    }

    private void ConfigurePlan(SetupScenario scenario)
    {
        _scenario = scenario;
        Components.Clear();

        AddComponent("LabVIEW", "Create measurement, test, and control applications.", "1.2 GB", false);
        AddComponent("NI Measurement & Automation Explorer", "Discover, configure, and test NI hardware from one place.", "94 MB", false);
        AddComponent("NI-DAQmx", "Measurement APIs and device support for NI data acquisition hardware.", "382 MB", false);
        AddComponent("Examples and documentation", "Locally available examples, reference material, and getting-started content.", "140 MB", true);

        if (scenario == SetupScenario.Application)
        {
            AddComponent("FlexLogger", "Configure and log sensor measurements without programming.", "410 MB", true);
            AddComponent("InstrumentStudio", "Interactively configure and measure with supported PXI instruments.", "560 MB", true);
            AddComponent("TestStand", "Configure and execute automated test sequences.", "920 MB", true);
            AddComponent("DIAdem", "Find, inspect, analyze, and report measurement data.", "730 MB", true);
            AddComponent("NI-VISA", "Instrument communication support for PXI, USB, Ethernet, serial, and GPIB instruments.", "118 MB", true);
            AddComponent("PXI Platform Services", "Platform services for compatible PXI chassis, controllers, and modules.", "86 MB", true);
        }

        if (scenario is SetupScenario.Hardware or SetupScenario.TestSystem)
        {
            AddComponent("NI-VISA", "Instrument communication support for PXI, USB, Ethernet, serial, and GPIB instruments.", "118 MB", false);
            AddComponent("PXI Platform Services", "Platform services for compatible PXI chassis, controllers, and modules.", "86 MB", false);
            AddComponent("NI-FGEN", "Driver support for NI arbitrary waveform generators.", "132 MB", true);
            AddComponent("NI-SCOPE", "Driver support for NI oscilloscopes and digitizers.", "168 MB", true);
            AddComponent("NI-RFSA and NI-RFSG", "Driver support for RF signal analyzers and generators.", "295 MB", true);
            AddComponent("Industrial communications", "Choose OPC UA, EtherNet/IP, EtherCAT, or PROFINET support when your system uses these protocols.", "240 MB", true);
        }

        if (scenario == SetupScenario.TestSystem)
        {
            AddComponent("TestStand", "Configure and execute automated test sequences.", "920 MB", false);
            AddComponent("InstrumentStudio", "Interactively configure and measure with supported PXI instruments.", "560 MB", true);
            AddComponent("FlexLogger", "Configure and log sensor measurements without programming.", "410 MB", true);
            AddComponent("DIAdem", "Find, inspect, analyze, and report measurement data.", "730 MB", true);
            AddComponent("SystemLink client", "Connect this station to test operations and asset-management workflows.", "210 MB", true);
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
            ? "This recommended setup includes DAQ support. It will show any device-driver or restart implications before installation."
            : "This setup adds core NI software and selected support. The final installer checks Windows and device compatibility before making changes.";
        AdminRequirement = "Required for device support";
        RestartRequirement = "Not expected";
        ReviewNotice = scenario == SetupScenario.Application
            ? "Optional products are not installed unless you select them. You can return later to add TestStand, FlexLogger, InstrumentStudio, DIAdem, or additional driver support."
            : "The final installer will show any component that needs a restart or conflicts with installed hardware support before it changes the workstation.";
    }

    private void AddComponent(string name, string description, string size, bool isOptional)
    {
        var component = new SetupComponent(name, description, size, isOptional);
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
    public SetupComponent(string name, string description, string size, bool isOptional)
    {
        Name = name;
        Description = description;
        Size = size;
        IsOptional = isOptional;
        IsSelected = true;
    }

    public string Name { get; }
    public string Description { get; }
    public string Size { get; }
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
