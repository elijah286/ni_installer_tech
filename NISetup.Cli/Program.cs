using System.Security.Principal;
using System.Text.Json;
using NIInstallerTech.Services;

const int Success = 0;
const int InvalidArguments = 2;
const int PolicyViolation = 4;
const int UnsupportedOperation = 6;
const string LabviewReferencePocArchiveSha256 = "8a2f6f00f13ff9c8083f694b4ec2fdf81b71577aac2af7d26ac0f3c2ae822a91";

var arguments = args.ToList();
var command = arguments.FirstOrDefault() ?? "help";
if (arguments.Count > 0)
{
    arguments.RemoveAt(0);
}

var options = ParseOptions(arguments, out var positionalArguments, out var parseError);
if (parseError is not null)
{
    WriteError(parseError, InvalidArguments, options);
    return InvalidArguments;
}

var format = options.GetValueOrDefault("format", "text");
if (format is not ("json" or "text"))
{
    WriteError("--format must be 'json' or 'text'.", InvalidArguments, options);
    return InvalidArguments;
}

if (options.ContainsKey("help") || command is "help" or "--help" or "-h")
{
    WriteHelp();
    return Success;
}

var profile = options.GetValueOrDefault("profile", "recommended");
var source = options.GetValueOrDefault("source", "ni");
var platform = options.GetValueOrDefault("platform", "linux-x64");
var labviewRelease = options.GetValueOrDefault("labview-release", "2026-q3");
var nonInteractive = options.ContainsKey("non-interactive");
var simulation = options.ContainsKey("simulate");

if (profile is not ("recommended" or "hardware" or "test-system"))
{
    WriteError("--profile must be 'recommended', 'hardware', or 'test-system'.", InvalidArguments, options);
    return InvalidArguments;
}

if (source is not ("ni" or "offline" or "repository"))
{
    WriteError("--source must be 'ni', 'offline', or 'repository'.", InvalidArguments, options);
    return InvalidArguments;
}

if (source == "repository" && !options.ContainsKey("repository"))
{
    WriteError("--repository is required when --source repository is selected.", InvalidArguments, options);
    return InvalidArguments;
}

if (labviewRelease is not ("2026-q1" or "2026-q3"))
{
    WriteError("--labview-release must be '2026-q1' or '2026-q3'.", InvalidArguments, options);
    return InvalidArguments;
}

var plan = CreatePlan(profile, source, platform, labviewRelease, options.GetValueOrDefault("repository"));

switch (command)
{
    case "plan":
        WriteResult(new CliResult("planned", "A non-mutating component plan was created.", plan), format);
        return Success;

    case "bundle":
        if (positionalArguments.SingleOrDefault() is not "create")
        {
            WriteError("Use 'bundle create' to request an offline bundle.", InvalidArguments, options);
            return InvalidArguments;
        }

        if (!simulation)
        {
            WriteError("Offline bundle creation is not implemented in this prototype. Add --simulate to validate the resolved bundle plan without retrieving payloads.", UnsupportedOperation, options);
            return UnsupportedOperation;
        }

        var bundlePlan = new BundlePlan(
            "offline-bundle",
            plan,
            "The final bundle will contain the complete selected artifact closure, manifests, catalog subset, SBOMs, and provenance.",
            "Activation, entitlement, customer configuration, credentials, and raw Driver Store content are excluded.");
        WriteResult(new CliResult("simulated", "A portable offline-bundle plan was validated; no payloads were downloaded or written.", bundlePlan), format);
        return Success;

    case "install":
        if (!nonInteractive)
        {
            WriteError("Headless installation requires --non-interactive so automation cannot pause for prompts.", PolicyViolation, options);
            return PolicyViolation;
        }

        if (!simulation)
        {
            WriteError("The deployment engine is not implemented in this prototype. Add --simulate to validate an installation plan without changing the container or host.", UnsupportedOperation, options);
            return UnsupportedOperation;
        }

        WriteResult(new CliResult("simulated", "The non-interactive installation plan was validated; no machine state, drivers, firmware, licensing, or activation state was changed.", plan), format);
        return Success;

    case "install-reference-poc":
        if (!nonInteractive)
        {
            WriteError("Clean-machine installation requires --non-interactive.", PolicyViolation, options);
            return PolicyViolation;
        }

        if (!options.ContainsKey("acknowledge-reference-poc"))
        {
            WriteError("Clean-machine installation of a reference-derived POC requires --acknowledge-reference-poc.", PolicyViolation, options);
            return PolicyViolation;
        }

        if (!OperatingSystem.IsWindows())
        {
            WriteError("Clean-machine LabVIEW deployment is supported only on Windows x64.", UnsupportedOperation, options);
            return UnsupportedOperation;
        }

        if (!IsWindowsAdministrator())
        {
            WriteError("Clean-machine LabVIEW deployment must run from an elevated Windows console.", PolicyViolation, options);
            return PolicyViolation;
        }

        if (!options.TryGetValue("archive", out var archivePath))
        {
            WriteError("--archive is required for install-reference-poc.", InvalidArguments, options);
            return InvalidArguments;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            WriteError("The Windows Program Files location could not be resolved.", UnsupportedOperation, options);
            return UnsupportedOperation;
        }

        var stateDirectory = options.GetValueOrDefault("state-directory");
        var deploymentService = new CleanMachineDeploymentService(stateDirectory);
        var operationLog = new PrototypeOperationLog(deploymentService.StateDirectory);
        var deployment = deploymentService.Install(
            new CleanMachineDeploymentRequest(
                "labview.application.2026-q3.x64",
                "26.30.49792",
                archivePath,
                LabviewReferencePocArchiveSha256,
                Path.Combine(programFiles, "National Instruments", "LabVIEW 2026"),
                "labview-application",
                "LabVIEW.exe",
                true),
            operationLog);
        WriteResult(deployment, format);
        return deployment.IsSuccess ? Success : PolicyViolation;

    default:
        WriteError($"Unknown command '{command}'.", InvalidArguments, options);
        WriteHelp();
        return InvalidArguments;
}

static Dictionary<string, string> ParseOptions(IReadOnlyList<string> arguments, out List<string> positionalArguments, out string? error)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    positionalArguments = [];
    error = null;

    for (var index = 0; index < arguments.Count; index++)
    {
        var argument = arguments[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            positionalArguments.Add(argument);
            continue;
        }

        var key = argument[2..];
        if (key is "help" or "non-interactive" or "simulate" or "acknowledge-reference-poc")
        {
            options[key] = "true";
            continue;
        }

        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = $"{argument} requires a value.";
            return options;
        }

        options[key] = arguments[++index];
    }

    return options;
}

static bool IsWindowsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

static InstallationPlan CreatePlan(string profile, string source, string platform, string labviewRelease, string? repository)
{
    var labviewLabel = labviewRelease == "2026-q1" ? "LabVIEW 2026 Q1 x64" : "LabVIEW 2026 Q3 x64";
    var components = new List<PlanComponent>
    {
        new($"labview.core.{labviewRelease}.x64", labviewLabel, "application", "one selected platform release", "pending-validation", "labview-core", "one-selected-release"),
        new("max.configuration", "NI Measurement & Automation Explorer 26.5", "configuration", "one active configuration schema; machine configuration excluded", "ineligible", "max-configuration", "singleton"),
        new("daqmx.runtime.user-mode", "NI-DAQmx 26.0 user-mode runtime", "api-runtime", "user-mode only", "pending-validation", "daqmx-user-mode", "side-by-side-when-compatible"),
        new($"daqmx.labview-adapter.{labviewRelease}.x64", $"NI-DAQmx LabVIEW {labviewRelease} adapter", "language-adapter", "bound to selected LabVIEW ABI", "pending-validation", $"daqmx-labview-{labviewRelease}", "side-by-side-when-compatible")
    };

    if (profile == "hardware")
    {
        components.Add(new("daqmx.local-mio-support", "DAQmx local MIO support", "hardware-family-support", "driver boundary; activation requires a supported host", "ineligible", "daqmx-local-mio-driver", "singleton"));
        components.Add(new("daqmx.compactdaq-firmware", "CompactDAQ firmware", "firmware", "explicit device-specific approval", "ineligible", "cdaq-firmware", "singleton"));
    }

    if (profile == "test-system")
    {
        components.Add(new("teststand.runtime", "TestStand runtime", "application", "user-mode only", "pending-validation", "teststand-runtime", "side-by-side-when-compatible"));
        components.Add(new("ni-visa.runtime", "NI-VISA runtime", "api-runtime", "user-mode only", "pending-validation", "ni-visa-runtime", "side-by-side-when-compatible"));
    }

    return new InstallationPlan(
        "prototype-plan-v0.1",
        profile,
        platform,
        labviewRelease,
        source,
        repository,
        "existing-ni-activation-tooling",
        "unchanged",
        components,
        "This plan is non-mutating. Hardware, driver, firmware, entitlement, and activation boundaries remain explicit.");
}

static void WriteResult(object result, string format)
{
    if (format == "json")
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    Console.Out.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}

static void WriteError(string message, int exitCode, IReadOnlyDictionary<string, string> options)
{
    if (options.GetValueOrDefault("format") == "json")
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "error", exitCode, message }));
        return;
    }

    Console.Error.WriteLine($"error: {message}");
}

static void WriteHelp()
{
    Console.Out.WriteLine("""
NI Setup CLI — headless installation-model prototype

Usage:
    ni-setup plan [--profile recommended|hardware|test-system] [--labview-release 2026-q1|2026-q3] [--source ni|offline|repository] [--repository URL] [--platform OS-ARCH] [--format json]
    ni-setup bundle create --simulate [--profile ...] [--labview-release ...] [--source ni|repository] [--format json]
    ni-setup install --non-interactive --simulate [--profile ...] [--labview-release ...] [--source ni|offline|repository] [--format json]
    ni-setup install-reference-poc --non-interactive --acknowledge-reference-poc --archive PATH [--state-directory PATH] [--format json]

The CLI is safe for containers: this prototype never installs software, drivers, firmware, or licensing material.
Use --simulate for bundle/install because the deployment engine is not implemented yet.
install-reference-poc is an elevated Windows-only clean-machine validation command for the exact internal LabVIEW 2026 Q3 POC archive. It does not change NI activation or licensing.
""");
}

internal sealed record CliResult(string Status, string Message, object Plan);
internal sealed record BundlePlan(string Type, InstallationPlan Plan, string Contents, string Exclusions);
internal sealed record InstallationPlan(
    string SchemaVersion,
    string Profile,
    string Platform,
    string LabVIEWRelease,
    string Source,
    string? Repository,
    string LicensingIntegration,
    string LicensingBehavior,
    IReadOnlyList<PlanComponent> Components,
    string Notes);
internal sealed record PlanComponent(string Id, string DisplayName, string Role, string Boundary, string ContainerExecutionEligibility, string UpgradeDomain, string CoexistencePolicy);
