# NI Platform Setup — installation-model prototype

This repository is an R&D prototype for a new NI software installation model and its customer-facing experience. It explores how customers can install, update, repair, and remove only the applications, APIs, configuration tools, hardware-family support, drivers, and firmware they need.

The repository is intentionally documentation-first. It is the reviewable record R&D teams use to converge on a build-output contract that can serve the eventual component model. A design conclusion, an evidence-backed change to that contract, or a new required build output must be documented here before it is treated as a prototype requirement.

## Goals

- Provide a simple, progressive-disclosure setup experience that makes scope and consequences clear.
- Separate application/API-only use from configuration, hardware support, kernel drivers, and firmware.
- Make build-agent and container-oriented plans possible without silently installing device support.
- Update the smallest compatible component closure and explicitly preserve unrelated working components.
- Support customer-controlled repositories, channels, and baselines without requiring customers to understand package internals.
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
| [Reference-component POC capture](docs/reference-component-poc-capture.md) | Controlled use of reference-machine components; safety, exclusions, provenance, and initial scope. | Planned implementation |
| [Component repository and packaging architecture](docs/component-repository-and-packaging-architecture.md) | Artifact, catalog, channel, repository, and upgrade design. | Design hypothesis |
| [NI-DAQmx cut-point hypothesis](docs/ni-daqmx-cut-point-hypothesis.md) | Evidence-based starting boundaries and tests for NI-DAQmx. | Evidence-informed hypothesis |
| [Portfolio audit and deployment model](docs/public-portfolio-audit-and-deployment-model.md) | Broad portfolio, OS, resource, and transaction model. | Public-information hypothesis |
| [Reference inventory](reference-inventory/README.md) | Read-only evidence captured from the Windows reference machine. | Observed evidence |

Status labels are deliberate: a **draft** asks for review, a **hypothesis** needs validation, **observed evidence** is a recorded fact with provenance, and an **approved contract** may be relied on by build teams. No document is an authorization to redistribute software or change licensing behavior.

## Build-team starting point

The proposed output is not a replacement installer format yet. It is a small, additive component descriptor plus a deterministic payload manifest, resource claims, compatibility metadata, health checks, SBOM/provenance, and an explicit licensing/activation declaration. Build systems should retain their current installers and activation integration while producing these reviewable inputs alongside them.

See the [build-output contract](docs/build-output-contract.md) for the exact v0.1 structure, required fields, and adoption sequence. The contract is designed to evolve as this prototype measures real component boundaries; it must not be treated as final until marked approved.

## Safety and licensing boundary

- Do not collect, copy, expose, migrate, or alter activation records, license files, entitlement secrets, certificates, credentials, customer data, or machine-specific configuration.
- Do not raw-copy or reconstruct Windows drivers. Drivers remain original signed INF/CAT/SYS packages and are activated only through supported Windows mechanisms in a future implementation.
- A reference-derived POC artifact is internal experimental material, not a redistributable product package or clean-machine installer.
- The eventual installer can query existing approved licensing/activation tooling through a narrow integration boundary, but it must not decide entitlement itself or weaken access protections.

## Technology

The prototype uses Avalonia and C# on .NET 10. Avalonia enables one native desktop UI codebase for Windows, Linux, and macOS. A production deployment engine remains platform-specific where necessary: Windows Driver Store/PnP versus Linux package, kernel-module, `udev`, and `systemd` management.

## Run the UI prototype

From [NIInstallerTech](NIInstallerTech), run `dotnet run` using a supported .NET SDK. On Windows, the app opens as a native desktop window; on macOS, it runs locally for UX iteration.
