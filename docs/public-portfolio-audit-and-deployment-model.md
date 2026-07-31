# NI 64-bit portfolio audit and deployment data model

**Status:** public-information hypothesis — not an authoritative compatibility catalog.  
**Scope:** 64-bit Windows and Linux delivery, with NI Linux Real-Time noted separately where it materially changes the deployment model.  
**Purpose:** establish a catalog and machine-state model that can be verified against the reference Windows machine before becoming an installer contract.

## Executive summary

NI software is not one dependency tree. It is a set of overlapping graphs:

1. **Customer capabilities** — for example, acquire DAQ data, control a PXI waveform generator, execute a TestStand sequence, or log without programming.
2. **Products and add-ons** — LabVIEW, TestStand, FlexLogger, InstrumentStudio, DIAdem, SystemLink, LabVIEW modules/toolkits, and language APIs.
3. **Platform and driver families** — NI-DAQmx, NI-VISA, NI-488.2, NI-SCOPE, NI-FGEN, NI-RFSA, NI-RFSG, PXI Platform Services, RIO/CompactRIO support, Vision Acquisition Software, Motion, Switch, DCPower, and industrial communication products.
4. **Deployable platform fragments** — user-mode libraries, language bindings, device services, firmware tools, Windows Driver Store packages / Linux kernel modules, configuration databases, certificates, registries, and shared third-party prerequisites.
5. **Machine resources** — files, device bindings, services, package-manager records, registry keys, COM registrations, ACLs, environment variables, firewall rules, and user/machine configuration.

The future installer must resolve and record all five. A product name or an existing MSI feature is too coarse to be the ownership boundary.

## Public portfolio map

This list is intentionally organized by what a customer asks for, rather than by legacy installer media.

| Capability family | Representative products / components | Typical payload roles | Linux posture to validate |
|---|---|---|---|
| Core application development | LabVIEW; LabWindows/CVI; Measurement Studio; G Web Development Software; C/.NET/Python application APIs | IDE/runtime, compiler/toolchain integrations, language bindings, examples, documentation, shared runtime | Do not assume parity with Windows. Treat desktop Linux and NI Linux Real-Time as separate platform targets and determine support per version/product.
| Test sequencing and deployment | TestStand; deployment/debug licenses; custom operator interfaces; adapters for LabVIEW, C/C++, .NET, Python | sequence editor/runtime, adapters, report engines, deployment content, database connectors, user configuration | Product/version-specific. Model a Linux test-execution target separately from a Windows authoring station.
| Interactive measurement/configuration | NI Measurement & Automation Explorer (MAX); FlexLogger; InstrumentStudio; NI Package Manager legacy state; device configuration utilities | user-mode UI, device discovery, configuration storage, transport/service dependencies | MAX is Windows-specific. Equivalent Linux configuration workflow must be modeled as a separate capability rather than a false equivalent.
| Data management and analysis | DIAdem; SystemLink clients/services; data plugins; reporting | application/runtime, database/network connectors, certificates, scheduled/background services, local data/configuration | Product/version-specific. SystemLink agents and server/client roles require explicit OS support and service ownership data.
| Data acquisition and signal conditioning | NI-DAQmx; device-specific support; SCXI / legacy hardware support where applicable | user-mode API, language bindings, firmware/update tools, device service(s), PnP or kernel driver, configuration | Strong candidate for a Linux driver family, but support is hardware-, distribution-, kernel-, architecture-, and release-specific.
| Instrument I/O | NI-VISA; NI-488.2; GPIB; serial; USB; Ethernet; PXI resource management | VISA API/runtime, transport back ends, USB/GPIB/PXI drivers, device discovery/configuration | NI-VISA and NI-488.2 have historically had Linux variants; verify supported distro/kernel/architecture per release.
| PXI platform | PXI Platform Services; NI-VISA; chassis/controller support; PXI remote/management components | platform services, bus/resource management, PnP/kernel drivers, firmware/configuration, APIs | Treat as hardware-family-specific; Linux support cannot be inferred from Windows PXI support.
| Modular instruments | NI-SCOPE; NI-FGEN; NI-DCPower; NI-SWITCH; NI-DMM; NI-HSDIO; NI-TClk; NI-RFSA; NI-RFSG; RFmx | public APIs, IVI/VISA dependencies, device drivers, firmware, calibration/configuration, hardware-specific services | Each family needs independent platform support and ABI rules; do not create one generic “instrument driver” package.
| RF and wireless test | NI-RFSA; NI-RFSG; RFmx; PXI platform dependencies | driver/API, analysis/personality libraries, hardware firmware, calibration artifacts, PXI/VISA dependencies | Support is likely narrower than Windows; represent unsupported Linux variants explicitly rather than omitting them.
| RIO, control, embedded | NI-RIO; CompactRIO; LabVIEW Real-Time Module; LabVIEW FPGA Module; hardware support; target images | host authoring/runtime, toolchains, target feeds/images, FPGA compilation assets, network deployment service | Distinguish desktop Linux host, Windows host, and NI Linux Real-Time target. A target feed/image is not a desktop package.
| Vision | Vision Development Module; Vision Acquisition Software; camera support | image-processing library, acquisition API, camera driver/transport, examples | Verify vendor/camera and operating-system coverage separately.
| Motion and industrial communication | NI-Motion; OPC UA; EtherNet/IP; EtherCAT; PROFINET; Modbus and related toolkit/driver products | protocol libraries, gateways/services, fieldbus interfaces, configuration tools, kernel/transport drivers when hardware requires them | Protocol software can have different host/target support from its associated hardware interfaces; model each separately.
| Suites and bundles | LabVIEW+ Suite; HIL suite; Multi-IDE Bundle; software platform bundles | entitlement/selection layer that resolves concrete products; not a deployable resource owner | Bundles should be catalog aliases/curated profiles, not opaque monolithic installation units.

Public NI pages support the high-level portfolio structure: NI lists LabVIEW, LabWindows/CVI, Measurement Studio, G Web, suites, modules, TestStand, DIAdem, and SystemLink; its driver download page identifies NI-DAQmx, NI-VISA, and NI-488.2 as principal driver families. NI also describes LabVIEW applications as requiring the appropriate instrument driver and TestStand as composing LabVIEW, C/C++, .NET, and Python modules. These statements support graph modeling but do **not** establish version-level compatibility.

## Operating-system model

### Windows x64

Windows has the fullest likely surface area and the most stateful deployment path. A package may affect:

- application directories and shared NI directories;
- Windows Driver Store (INF, CAT, driver package identity, published INF);
- Plug and Play hardware binding by hardware/compatible ID;
- kernel driver service and user-mode service definitions;
- firmware staging/update tools;
- machine and user registry state;
- COM registration and type libraries, when present;
- App Paths, file associations, environment variables, PATH, and application configuration;
- certificates / catalog signature evidence;
- Windows firewall or network service policy;
- scheduled tasks, WMI providers, performance counters, event sources, and diagnostics;
- VC++ runtimes, .NET runtimes, MSXML, SQL/database or other third-party prerequisites;
- licenses/activation and user data, which must be classified as **not transferable by discovery**.

Windows driver packages must be kept intact and managed with supported Driver Store/PnP mechanisms. The new installer must never emulate a driver installation through raw copies into Windows directories.

### Desktop Linux x64

Linux cannot be modeled as “Windows minus the registry.” It should have a native model per supported distribution and release:

- signed or repository-managed RPM/DEB packages, or an NI package format that integrates with them;
- package-manager ownership and dependency metadata;
- `udev` rules, device groups, ACLs, and permissions;
- kernel modules, their signing/secure-boot requirements, and kernel-version ABI compatibility;
- `systemd` units, socket activation, daemons, logs, and service users;
- shared libraries, ABI sonames, linker cache/paths, Python wheels, and language bindings;
- firmware blobs and device configuration;
- desktop integration only when a desktop product is actually supported;
- container eligibility, host-driver/container-user-mode compatibility, and device pass-through policy.

The catalog must make a component `supported`, `unsupported`, or `not-evaluated` for each `(distribution, release, architecture, kernel, secure-boot state)` tuple. Absence from a current catalog must never be interpreted as safe to install.

### NI Linux Real-Time

NI Linux Real-Time is a target operating environment, not a substitute name for desktop Linux. Model it as a distinct target type with potentially different package feeds, image compatibility, deployment channels, hardware support, reboot semantics, and recovery workflows. Host tools, target runtimes, and target images must have separate identities and explicit host-to-target compatibility edges.

## Proposed catalog model

Use immutable versioned records. The primary keys below are conceptual; JSON, protobuf, or relational storage are all viable.

```text
Capability
  id, title, audience, platform_scope, selection_rules

Product
  id, title, family, licensing_identity, documentation_identity

Release
  product_id, version, channel, lifecycle_status, released_at

Component
  id, release_id, role, scope, architecture, platform_selector,
  capability_provides[], api_contracts[], privilege_class,
  restart_class, container_eligibility

Artifact
  digest, uri, format, signature, sbom, provenance, size

ResourceClaim
  component_id, kind, identity, owner_scope, mutability,
  install_action, activation_action, rollback_action, removal_action

DependencyEdge
  from_component, relation, to_component, version_constraint,
  platform_selector, condition, explanation

CompatibilityRule
  subjects[], platform_selector, result, severity, explanation, evidence

DeviceRule
  hardware_id / compatible_id / bus / product_id / firmware_range,
  candidate_components[], activation_policy

InstallationState
  machine_id, transaction_id, resolved_components[], resource_claims[],
  activation_states[], health_checks[], reboot_state
```

### Component roles

A component has one primary role, with optional secondary roles:

| Role | Example | Important separation |
|---|---|---|
| `application` | LabVIEW, TestStand, FlexLogger, InstrumentStudio, DIAdem | Can often update independently of a driver once ABI compatibility permits. |
| `api-runtime` | NI-DAQmx C API, NI-VISA runtime, .NET/Python bindings | Must specify ABI/API contracts and whether it is container eligible. |
| `device-driver` | signed Windows INF/CAT/SYS; Linux kernel module | Machine privileged; bound to hardware identity; may be singleton/one-active-version. |
| `platform-service` | PXI resource management, discovery/configuration service | Must declare service ownership, account, startup policy, port/firewall side effects. |
| `firmware-tool` / `firmware` | hardware firmware updater and image | Firmware is a device transaction with separate recovery and compatibility rules. |
| `configuration-ui` | MAX-like hardware configuration surface | Must own only its configuration schema; not infer ownership of all device state. |
| `toolkit` / `module` | LabVIEW FPGA/Real-Time/Vision/industrial toolkits | Explicitly depends on host version/edition and may deploy separately to a target. |
| `target-feed` / `target-image` | NI Linux RT target packages/images | Separate from host installation. |
| `third-party-prerequisite` | VC++ runtime, .NET runtime, system library | Preserve supplier, license, CVE/SBOM, and sharing/servicing rules. |
| `documentation` / `examples` | optional local help, examples | Always removable independently from core runtime. |

### Dependency relations

Do not use only `dependsOn`. Required relations include:

- `requires` — hard prerequisite;
- `recommends` — selected by default for a capability but removable;
- `provides` — capability/virtual API fulfilled by a component;
- `conflictsWith` — cannot coexist;
- `replaces` / `supersedes` — service/update lineage;
- `compatibleWith` / `incompatibleWith` — version or platform compatibility;
- `requiresSameMajor` / `requiresExactBuild` — common shared-runtime and toolchain rules;
- `hostSupportsTarget` — host authoring version can deploy to a target/image;
- `bindsDevice` — driver/component can bind one or more hardware identities;
- `usesService`, `usesFirmware`, `usesConfigurationSchema` — non-file coupling;
- `migratesFrom` — legacy MSI or previous component state;
- `sharesResourceWith` — requires ownership arbitration;
- `requiresRebootBeforeActivation` and `requiresRebootAfterRemoval`.

Each edge must contain a human explanation that is suitable for the UI, for example: “NI-SCOPE requires this NI-VISA ABI range to communicate with this PXI digitizer.”

## Resource and transaction model

The installer must plan from declared resources—not scripts that can arbitrarily mutate a machine.

| Resource type | Windows management | Linux management | Required ledger fields |
|---|---|---|---|
| Files/directories | protected location ACLs, side-by-side/shared runtime strategy | package file ownership, filesystem permissions | path, hash, owner component, shared/refcount policy, previous state |
| Registry / settings | key/value, hive, view (`x64`/`x86`), ACL, user vs machine | config file/schema or dconf-like store | identity, prior value, merge/migration policy, owner |
| Driver package | Driver Store identity, INF, CAT, signature, PnP binding | kernel module/package, signer, module params, `udev` rule | hardware match, version, signer, staged/active state, rollback path |
| Service / daemon | service account, startup, recovery actions, dependencies | systemd unit/user/socket, enablement, dependencies | service identity, binary hash, config, start state, owner |
| Firmware | Windows device update workflow / vendor mechanism | device update mechanism | current/target version, device serial scope, recovery policy, audit evidence |
| Shared library/API | DLL ABI, COM/type library, runtime loader behavior | ELF soname, loader path, language ABI | contract id/version, consumers, coexistence policy |
| Certificate/signing | certificate store and driver catalog verification | keyring/repository key/module signature | issuer, thumbprint/key ID, scope, expiry/revocation policy |
| Network / firewall | firewall rule, endpoint, proxy policy | firewall service rules, port/socket policy | endpoint, policy owner, removal rule |
| Package-system record | legacy MSI/MSIX/NIPM evidence and new ledger | RPM/DEB / NI feed evidence and new ledger | external identity, component mapping, migration state |
| License/user content | detect but do not copy by default | detect but do not copy by default | classification, redaction policy, user-consent/migration rule |

### Transaction requirements

1. Solve the complete compatible component graph before mutation.
2. Show the user a capability-oriented plan, not raw dependencies by default.
3. Verify artifact digest, provenance, signature, and policy before privileged actions.
4. Stage payloads and drivers before activation where the platform permits.
5. Write a durable transaction journal before each external side effect.
6. Use idempotent, declared operations; record actual observed post-state.
7. On interruption, resume to a known checkpoint or roll back to the last verified healthy state.
8. Treat firmware and singleton kernel upgrades as explicit, separately confirmed transaction boundaries.
9. On removal, release only resources exclusively owned by the selected component graph.
10. Retain diagnostic evidence sufficient to explain the result without retaining credentials, licenses, or customer data.

## Compatibility solver inputs

The resolver should consume these facts before making recommendations:

- OS family, edition/distribution, version, architecture, kernel, secure-boot/code-integrity state;
- hardware inventory: PnP hardware IDs, compatible IDs, PCI/USB topology, firmware, chassis/controller, serial scope where permitted;
- existing new-platform ledger state and detected legacy-product state;
- requested customer capabilities and desired language/toolchain;
- enterprise channel/repository policy, pinned versions, offline content availability;
- licenses/entitlements as a policy result only—not copied secrets;
- current driver binding, Driver Store/kernel-module state, active service state;
- target systems (for RT/FPGA/controller deployment) and their compatible images/feeds;
- reboot and downtime constraints.

The resolver output must include selections, rejected alternatives, exact reasons, changes to shared/singleton resources, download/disk/restart implications, and a user-readable explanation.

## Initial 64-bit catalog seeds

These are **family seeds**, not claims that every product is available on every target.

```yaml
capabilities:
  - id: measurement-development
    defaultProfile: windows-core-ni
  - id: daq-acquisition
  - id: pxI-instrument-control
  - id: rf-test
  - id: automated-test-execution
  - id: interactive-data-logging
  - id: measurement-data-analysis
  - id: vision-acquisition
  - id: industrial-communication
  - id: rio-embedded-target-development

profiles:
  - id: windows-core-ni
    contains: [labview-x64, ni-max-x64, ni-daqmx-x64]
    note: "Recommended UX starting point; exact release compatibility is catalog-driven."
  - id: windows-automated-test
    requires: [windows-core-ni, teststand-x64]
    recommends: [instrumentstudio-x64, flexlogger-x64, diadem-x64, systemlink-client-x64]
  - id: linux-daq-host
    note: "Only resolve after distro/kernel/hardware validation."
  - id: nilrt-target
    note: "Target feed/image profile, not a desktop host profile."

driverFamilies:
  - ni-daqmx
  - ni-visa
  - ni-4882
  - pxi-platform-services
  - ni-scope
  - ni-fgen
  - ni-dcpower
  - ni-switch
  - ni-dmm
  - ni-hsdio
  - ni-rfsa
  - ni-rfsg
  - ni-rio
  - vision-acquisition
  - ni-motion
  - industrial-communications
```

## Reference-machine verification plan

The public model has deliberate uncertainty. Once SSH access is available, collect facts **read-only** first and attach evidence to catalog claims.

### Windows reference collection

1. OS/version/architecture, Secure Boot, Device Guard/code-integrity policy, pending reboot indicators.
2. Installed NI products from supported registration/package evidence, plus file version/hash inventories for NI roots.
3. Windows Driver Store enumeration: INF/CAT/signature/provider/version, published INF identity, and PnP hardware bindings.
4. Present and non-present NI devices, hardware/compatible IDs, device problem codes, driver services, firmware/version evidence where safely readable.
5. Windows services, driver services, scheduled tasks, event sources, firewall rules, certificates, COM/type-library state, and relevant environment variables.
6. Selected registry evidence, explicitly classified by component ownership confidence.
7. SetupAPI log and relevant event-log snippets for historic device/driver installation behavior.
8. Language/API evidence: DLL dependencies, .NET assemblies, Python package or binding locations, supported examples/health checks.
9. A redacted inventory report; never collect activation credentials, license files, customer data, SSH keys, or secret configuration.

### Linux verification when a reference is available

1. Distribution, release, architecture, kernel, Secure Boot/module-signing state.
2. RPM/DEB/package ownership and repositories.
3. Kernel modules, `modinfo`, module signing, loaded state, `udev` rules, device permissions/groups.
4. systemd services, logs, config files, network policy, and firmware assets.
5. shared-library sonames/linker state, language binding metadata, and container/device passthrough facts.
6. Real device enumeration and driver-to-device bindings.

### Decisions after evidence collection

- Split every family seed into actual component boundaries and ownership claims.
- Identify true singleton upgrade domains.
- Establish API/driver/firmware compatibility ranges from shipped evidence and engineering ownership—not heuristics alone.
- Map legacy MSI/NIPM records into migration rules.
- Build automated clean-install, upgrade, repair, interruption, and selective-removal tests on physical hardware and disposable VMs.

## Source notes

Public sources consulted on 2026-07-31:

- NI Software Portfolio: https://www.ni.com/en/shop/software-portfolio.html
- NI Driver Downloads: https://www.ni.com/en/support/downloads/drivers.html
- What is NI LabVIEW?: https://www.ni.com/en/shop/labview.html
- What is NI TestStand?: https://www.ni.com/en/shop/electronic-test-instrumentation/application-software-for-electronic-test-and-instrumentation-category/what-is-teststand.html
- NI hardware/software compatibility and supported-OS links are published from the NI Software Portfolio page and should be consulted for every concrete release selection.

Public marketing and documentation establish product scope, not an installation contract. Any package, OS-support, dependency, driver, or compatibility assertion must be marked as verified only after direct product metadata and reference-machine evidence are captured.

## First reference-machine evidence

The first read-only Windows reference collection is available in [windows-demodog-inventory.json](../reference-inventory/windows-demodog-inventory.json), with its collection boundary, verified observations, and resulting model adjustments in [reference-inventory/README.md](../reference-inventory/README.md). It confirms that legacy installed-product registration contains many more fragments than customer-facing products, that 32-bit and 64-bit support artifacts coexist on an x64 host, that component release lineages coexist, and that host-side NI Linux Real-Time assets must be distinct from target deployment state.

NI Package Manager dependency closures for NI-DAQmx, InstrumentStudio, TestStand runtime, PXI Platform Services, NI-VISA runtime, SystemLink Client, and a LabVIEW VI-library root are retained in [windows-demodog-dependency-evidence](../reference-inventory/windows-demodog-dependency-evidence). They verify shared prerequisites, multi-architecture compatibility artifacts, service-oriented SystemLink dependencies, platform libraries, and legacy installer infrastructure. These are evidence inputs to the new catalog; they must be normalized into deliberate capability and resource boundaries rather than copied as the new user-facing installation model.

The first evidence-informed proposal for splitting NI-DAQmx into API, language adapter, user-mode runtime, hardware-family, kernel/bus, firmware, and configuration planes is in [ni-daqmx-cut-point-hypothesis.md](ni-daqmx-cut-point-hypothesis.md). It identifies the existing package boundaries that can seed the work, as well as the runtime/resource tests required before declaring any API-only or device-minimal configuration supported.
