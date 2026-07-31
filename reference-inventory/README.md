# Windows reference inventory — DEMODOG

Collected read-only on 2026-07-31 from `DEMODOG`, a Dell Latitude 5450 running Windows 11 Enterprise x64 (build `26200`). The source evidence is [windows-demodog-inventory.json](windows-demodog-inventory.json).

## Collection boundary

The collector read installed-product registration evidence, NI-related services, present NI PnP devices, installed signed-driver bindings, selected NI directory sizes, and reboot indicators. It did **not** read license files, credentials, user data, activation data, uninstall command lines, or modify the machine.

## Initial verified observations

- The collection returned **1,228** NI-related registered product fragments, **34** NI-related services, one present NI PnP device, and one observed active signed driver binding.
- The present driver/device is `NI Ethernet Device Enumerator`, bound through `oem6.inf`, provider `National Instruments`, version `26.0.0.49299`.
- This installed software set includes a broad mix of platform/application content: LabVIEW 2025 and 2026 Q1/Q3/Q5 fragments, TestStand 2026 Q1, InstrumentStudio 26.0, DIAdem 2026, NI Measurement & Automation Explorer 26.5, SystemLink services, PXI Platform Services 26.0, NI-VISA, NI-DCPower, NI-DMM, NI-FGEN, NI-SCOPE, NI-SWITCH, NI-RIO, IMAQ/IMAQdx, OPC, Modbus, and LabVIEW Real-Time/FPGA content.
- Installed content includes both 32-bit and 64-bit support fragments. A 64-bit installer model cannot discard x86 entries blindly; it must classify whether a product has a required 32-bit compatibility bridge, legacy runtime, or optional support payload.
- The same host includes multiple LabVIEW release lines and NI Linux Real-Time base-image/System Image artifacts. Those are host-side authoring/deployment artifacts; they must not be confused with a desktop-Linux installation or a deployed real-time target.
- Active services show that a “product” can own multiple shared machine-level resources. Observed examples include NI Configuration Manager (`mxssvr`), NI GPIB Hardware Enumeration, NI Network Discovery, NI LXI Discovery, NI mDNS Responder, PXI Resource Manager, PXI Chassis Management, NI Application Web Server, NI SystemLink services, NI TestStand Service, NI-RIO Server, NI-XNET IP Stack, OPC services, Citadel services, and NI Variable Engine.
- NI Package Manager reports **988 installed packages**. This is the strongest machine-local evidence of the existing logical package graph, although its package names are still implementation-level fragments rather than customer-facing selections.
- The Driver Store contains **82** National Instruments packages, including staged drivers for PXI, GPIB, serial/USB, DAQ, modular instruments, RIO, timing/synchronization, and system devices. This validates that Driver Store state is materially broader than the one currently bound NI device.
- Driver Store examples include `nicpciek.inf`, `niede.inf`, `niserpport.inf`, `niseruport.inf`, `nisyncbus.inf`, `ni488.inf`, `ni5110k.inf`, `ni5164k.inf`, `ni5170k.inf`, `ni5433k.inf`, device-specific DMM/DCPower INFs, and PXI trigger-router INFs. The observed packages are signed through Microsoft’s Windows Hardware Compatibility Publisher, except for some legacy entries.
- NI Package Manager dependency resolution produced the first verified dependency closures: NI-DAQmx (56 resolved package entries), InstrumentStudio (163), TestStand runtime (71), PXI Platform Services (43), NI-VISA runtime (37), SystemLink Client (32), and the selected LabVIEW VI library root (5). These closures contain shared installation infrastructure, third-party runtimes, services, APIs, language/architecture bridges, platform libraries, and product-specific payloads.
- The closures specifically confirm shared packages such as `ni-metauninstaller`, `ni-euladepot`, `ni-msiproperties`, error-reporting packages, NI Microsoft Visual C++ runtime delivery, .NET 8 Desktop Runtime delivery, .NET Framework 4.8, TDMS, XML/crypto/compression libraries, PXI routing/platform libraries, and NI licensing client packages. A new installer needs an explicit shared-prerequisite component class and ownership/refcount or content-addressing policy.
- SystemLink’s resolved set includes service-oriented and cryptographic dependencies, including forwarding/offline-store functionality, OpenSSL, RPC, and both x86/x64 client artifacts. TestStand and InstrumentStudio closures include x86 compatibility/support packages on this x64 host. Therefore package `architecture` alone is insufficient; the new catalog must distinguish native, compatibility, toolchain, and target roles.

## Adjustments to the data model

The initial public audit is updated by these observed facts:

1. **Registration fragment is not a product or component boundary.** The catalog needs `registrationEvidence[]` records that map many legacy entries to a curated component identity, instead of importing one catalog component for every entry.
2. **Architecture is multi-valued.** Add `architectureRole` on a component/resource: `native`, `compatibility`, `toolchain`, `target`, or `unknown`. This prevents an x86 bridge from being treated as an unwanted duplicate x64 payload.
3. **Release lineage is independent by component family.** The host simultaneously contains 2025 and 2026 product lines. The solver needs per-family coexistence and supersedence rules, not one global “NI platform version.”
4. **Host/target role is mandatory.** Add `executionLocation`: `windows-host`, `desktop-linux-host`, `nilrt-target`, `hardware-firmware`, or `container`. LabVIEW Real-Time images and host toolkits must resolve across a host-to-target compatibility edge.
5. **Service claims need first-class dependency and activation data.** A service resource must include service name, executable hash, startup mode, account, dependencies, ports/firewall policy, configuration schema, shared-owner policy, and activation health check.
6. **Installed driver package and active device binding are separate states.** The current collection found many installed software fragments but only one active NI binding. A future expanded collector must enumerate Driver Store packages independently of present PnP bindings.

## Next verification iterations

1. Enumerate the full Driver Store through supported `pnputil`/SetupAPI evidence, including staged but inactive NI packages and INF/CAT signature identity.
2. Map services to binaries, package registration evidence, ports, service dependencies, and configuration locations.
3. Extract NI Package Manager package metadata and dependency records where available; this is likely the most direct machine-local evidence of the existing logical package graph.
4. Inventory product directories and selected file manifests by curated family, then map only known resources to candidate component ownership.
5. Capture a second machine with actual PXI/DAQ/RF/industrial hardware attached, because this laptop does not exercise the primary hardware binding matrix.
6. Repeat equivalent evidence collection on a supported Linux host and a NI Linux Real-Time target.

## Package and Driver Store evidence

The raw, read-only exports are retained in [windows-demodog-package-evidence](windows-demodog-package-evidence):

- [NIReference-nipkg-list.txt](windows-demodog-package-evidence/NIReference-nipkg-list.txt) — the NI Package Manager installed-package list.
- [NIReference-pnputil-drivers.txt](windows-demodog-package-evidence/NIReference-pnputil-drivers.txt) — full Driver Store enumeration from `pnputil /enum-drivers`.

No packages, drivers, services, or machine configuration were modified while collecting this evidence.

## Verified dependency subgraphs

Read-only NI Package Manager dependency exports are retained in [windows-demodog-dependency-evidence](windows-demodog-dependency-evidence). They are solver output from the currently installed catalog and should be treated as evidence of the legacy package graph—not as final component boundaries:

| Selected package root | Resolved entries | Initial modeling implication |
|---|---:|---|
| `ni-daqmx` | 56 | DAQ is a graph spanning application APIs, configuration, firmware, platform libraries, prerequisites, and device support. |
| `ni-instrumentstudio` | 163 | An interactive application integrates with many instrument-driver, TestStand, licensing, and platform surfaces. |
| `ni-teststand-2026-runtime` | 71 | The test runtime includes language adapters, automation/COM compatibility, reporting/data, and shared development infrastructure. |
| `ni-pxiplatformservices` | 43 | PXI requires platform/routing libraries and privileged services beyond a device DLL. |
| `ni-visa-runtime` | 37 | VISA is a transport/runtime graph rather than one communication DLL. |
| `ni-systemlink-client` | 32 | System operations include service, offline forwarding, encryption, RPC, and multi-architecture client concerns. |
| `ni-labview-2026-vilib` | 5 | This selected VI-library fragment is narrow and is not a valid proxy for the full LabVIEW product graph. |
