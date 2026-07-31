# NI Platform Setup — installation-model prototype

[<img src="https://img.shields.io/badge/Run%20Installers-Download%20Windows%20Prototype-005B8E?style=for-the-badge&logo=windows&logoColor=white" alt="Run Installers — Download Windows Prototype" height="54">](https://github.com/elijah286/ni_installer_tech/releases/download/v0.1.3-prototype/NI-Platform-Setup-Prototype-win-x64.zip)

**Windows:** download the ZIP, extract it, and double-click **NI Platform Setup Prototype.exe**. No .NET SDK, terminal, or source checkout is required. The UI takes you through the installation workflow preview; it does not yet install NI software, drivers, firmware, or licensing content.

This repository is an R&D prototype for a new NI software installation model and its customer-facing experience. It explores how customers can install, update, repair, and remove only the applications, APIs, configuration tools, hardware-family support, drivers, and firmware they need.

The repository is intentionally documentation-first. It is the reviewable record R&D teams use to converge on a build-output contract that can serve the eventual component model. A design conclusion, an evidence-backed change to that contract, or a new required build output must be documented here before it is treated as a prototype requirement.

## Goals

- Provide a simple, progressive-disclosure setup experience that makes scope and consequences clear.
- Separate application/API-only use from configuration, hardware support, kernel drivers, and firmware.
- Make build-agent and container-oriented plans possible without silently installing device support.
- Provide a non-interactive CLI with JSON output and explicit exit codes for headless containers and build agents.
- Update the smallest compatible component closure and explicitly preserve unrelated working components.
- Support customer-controlled repositories, channels, and baselines without requiring customers to understand package internals.
- Deliver a small NI-hosted setup application by default, with an explicit option to assemble the selected plan into one portable offline installer for disconnected systems.
- Preserve current NI activation and licensing tooling and protections. This work must not copy, bypass, alter, or replace entitlement, activation, licensing, or access-control behavior.
- Base the proof of concept on controlled, read-only evidence and selected final-state components from the Windows reference machine while retaining the distinction between a POC capture and an authorized production artifact.

## Current prototype

[NIInstallerTech](NIInstallerTech) is a cross-platform Avalonia UI prototype. It does **not** deliver packages, install drivers, elevate privileges, change licensing/activation, or modify a machine. Its mocked catalog and plan flow are the integration seam for the catalog, solver, repository, and platform-specific deployment engine proposed by this research.

The current experience tests:

1. A recommended foundation: LabVIEW, NI Measurement & Automation Explorer, and NI-DAQmx.
2. Specialized hardware/protocol and automated-test-workstation paths as secondary choices.
3. A clear plan that exposes optional choices, download/disk size, privilege, restart, driver, and firmware impact.
4. A future upgrade view that distinguishes changed, retained, unselected, blocked, and explicit-boundary components.

## Documentation and review index

| Document | Audience and purpose | Status |
|---|---|---|
| [Documentation governance](docs/documentation-governance.md) | The mandatory documentation rule, evidence statuses, review expectations, and change process. | Active |
| [Build-output contract](docs/build-output-contract.md) | The versioned, evolving specification R&D build teams can use to emit component-ready outputs. | Draft v0.1 |
| [CLI interface](docs/cli-interface.md) | Headless/container commands, JSON contract, exit codes, image boundary, and path to a functional executor. | Draft v0.1 |
| [Coexistence and revision policy](docs/coexistence-and-revision-policy.md) | One selected primary release, catalog-controlled user-mode coexistence, and singleton driver/service/firmware domains. | Evidence-informed prototype policy |
| [Driver modernization and API decoupling](docs/driver-modernization-and-api-decoupling.md) | Separate API/runtime delivery from deliberately rebuilt, individually signed hardware-driver packages. | Recommended architecture direction |
| [Source-component assembly pipeline](docs/source-component-assembly.md) | Transforms original package intake into new content-addressed API/application source artifacts while excluding kernel driver content. | Executable prototype pipeline |
| [Reference-component POC capture](docs/reference-component-poc-capture.md) | Controlled use of reference-machine components; safety, exclusions, provenance, and initial scope. | Planned implementation |
| [SMB prototype repository](docs/nas-prototype-repository.md) | Chosen internal payload location, repository shape, exclusions, and clean-machine validation criterion. | Infrastructure ready; payloads pending review |
| [Windows prototype source connection](docs/windows-smb-source-connection.md) | Connect the Windows UI to a local HTTP/HTTPS repository endpoint, with SMB retained as an optional fallback. | Implemented prototype access flow |
| [Component repository and packaging architecture](docs/component-repository-and-packaging-architecture.md) | Artifact, catalog, channel, repository, and upgrade design. | Design hypothesis |
| [NI-DAQmx cut-point hypothesis](docs/ni-daqmx-cut-point-hypothesis.md) | Evidence-based starting boundaries and tests for NI-DAQmx. | Evidence-informed hypothesis |
| [Portfolio audit and deployment model](docs/public-portfolio-audit-and-deployment-model.md) | Broad portfolio, OS, resource, and transaction model. | Public-information hypothesis |
| [Reference inventory](reference-inventory/README.md) | Read-only evidence captured from the Windows reference machine. | Observed evidence |

Status labels are deliberate: a **draft** asks for review, a **hypothesis** needs validation, **observed evidence** is a recorded fact with provenance, and an **approved contract** may be relied on by build teams. No document is an authorization to redistribute software or change licensing behavior.

## Build-team starting point

The proposed output is not a replacement installer format yet. It is a small, additive component descriptor plus a deterministic payload manifest, resource claims, compatibility metadata, health checks, SBOM/provenance, and an explicit licensing/activation declaration. Build systems should retain their current installers and activation integration while producing these reviewable inputs alongside them.

See the [build-output contract](docs/build-output-contract.md) for the exact v0.1 structure, required fields, and adoption sequence. The contract is designed to evolve as this prototype measures real component boundaries; it must not be treated as final until marked approved.

## Delivery defaults

The normal experience begins with a small setup application and the NI-hosted catalog. It resolves and downloads only the selected components. An organization-approved repository/baseline is an advanced policy option, not the default customer decision.

For disconnected systems, the same selected plan can create one portable offline installer. It retrieves, verifies, and bundles the selected component artifacts and metadata before transport. The destination evaluates the normal compatibility, privilege, driver/firmware, and existing licensing/activation boundaries; an offline bundle never carries activation records, entitlement data, customer configuration, or raw Driver Store content.

The current internal prototype payload store is the SMB repository documented in [SMB prototype repository](docs/nas-prototype-repository.md). Its payload bytes remain outside GitHub; GitHub retains the reviewable catalog, manifests, tools, documentation, and evidence.

## Safety and licensing boundary

- Do not collect, copy, expose, migrate, or alter activation records, license files, entitlement secrets, certificates, credentials, customer data, or machine-specific configuration.
- Do not raw-copy or reconstruct Windows drivers. Drivers remain original signed INF/CAT/SYS packages and are activated only through supported Windows mechanisms in a future implementation.
- A reference-derived POC artifact is internal experimental material, not a redistributable product package or clean-machine installer.
- The eventual installer can query existing approved licensing/activation tooling through a narrow integration boundary, but it must not decide entitlement itself or weaken access protections.

## Technology

The prototype uses Avalonia and C# on .NET 10. Avalonia enables one native desktop UI codebase for Windows, Linux, and macOS. A production deployment engine remains platform-specific where necessary: Windows Driver Store/PnP versus Linux package, kernel-module, `udev`, and `systemd` management.

## Run the UI prototype

From [NIInstallerTech](NIInstallerTech), run `dotnet run` using a supported .NET SDK. On Windows, the app opens as a native desktop window; on macOS, it runs locally for UX iteration.

## Run the headless prototype

The [NISetup.Cli](NISetup.Cli) project implements the documented non-mutating CLI contract. From the repository root, run `dotnet run --project NISetup.Cli -- plan --profile recommended --source ni --format json`. Its `install` and `bundle create` command shapes require `--simulate` until an approved deployment engine is implemented. The rootless container definition is [NISetup.Cli/Dockerfile](NISetup.Cli/Dockerfile).

Use `--labview-release 2026-q1` or `--labview-release 2026-q3` to select the observed release line. The UI has the same choice and defaults to Q3. See [coexistence and revision policy](docs/coexistence-and-revision-policy.md).

## Build source components

[NISetup.ComponentAssembler](NISetup.ComponentAssembler) transforms original package intake on the internal SMB share into newly assembled content-addressed source artifacts. It excludes kernel-driver/signature material by default and records every exclusion in metadata. See [source-component assembly pipeline](docs/source-component-assembly.md).
