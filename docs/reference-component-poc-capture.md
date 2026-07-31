# Reference-component POC capture

**Status:** planned implementation.  
**Purpose:** define how the prototype can learn from and stage selected components from the Windows reference machine without modifying it, altering licensing/activation, or treating installed state as a production release source.

## POC boundary

The reference machine is a valuable source of final-state topology, file/resource evidence, installed versions, and narrow internal prototype payloads. It is not an authorized production artifact source.

Any captured material is:

- internal, access-controlled, and clearly labelled `reference-derived-poc`;
- read/copy-only from the reference machine;
- hashed with source path, observed version, collection time, and exclusion record;
- excluded from customer distribution, clean-machine installation claims, and production signing;
- used to validate the catalog, component boundaries, plan UX, cache/repository structure, and update-isolation model.

Production components must later be generated from authorized build outputs and original signed payloads as defined by the [build-output contract](build-output-contract.md).

## Initial controlled scope

Start narrowly with evidence already observed on `DEMODOG`:

1. LabVIEW 2026 Q3 x64 core or a smaller validated runtime/VI-library subset.
2. NI Measurement & Automation Explorer 26.5 configuration/UI evidence, excluding current machine configuration.
3. NI-DAQmx 26.0 user-mode/API candidate.
4. NI-DAQmx LabVIEW support candidate.
5. NI-DAQmx documentation/examples as optional content.

Hardware-family support, kernel drivers, Device Monitor, DAQ Assistant, and CompactDAQ firmware may be modeled in metadata initially. They must not be raw-copied or activated by this POC.

## Required capture record

Each captured component must produce:

```text
reference-poc/<component-id>/<version>/
  manifest.json                 # identity, source classification, scope
  payload-manifest.json         # file hashes and byte counts
  origin-evidence.json          # host/OS/tool version, source paths, observed package links
  exclusions.json               # excluded paths/categories and reasons
  resource-claims.json          # candidate ownership; confidence level
  compatibility.json            # proposed constraints; evidence status
  health-check.json             # non-destructive POC test
  payload/                      # only explicitly allowed files
```

`origin-evidence.json` must retain no secrets and should identify the evidence file in [reference-inventory](../reference-inventory/README.md) that supports its assertions.

## Capture tool

[Capture-NIReferenceComponent.ps1](../tools/Capture-NIReferenceComponent.ps1) is the initial allowlist-driven capture implementation. It runs on a controlled Windows staging/reference host, defaults to manifest-only discovery, and copies payload only when `-CopyPayload` is explicitly supplied. The input manifest must have `review.status` set to `approved-for-poc-capture`; the committed [template](../tools/manifests/daqmx-user-mode.capture.template.json) is intentionally non-runnable until a component owner fills an approved source allowlist.

The tool writes a component staging record under `incoming-reference-captures/<component-id>/<version>/` in the selected repository root. It produces payload hashes, origin evidence, exclusions, candidate resource claims, provenance, and a health-check placeholder. It rejects ProgramData, raw Driver Store, user data, registry exports, and common activation/licensing/secret paths.

## Mandatory exclusions

Never capture:

- NI activation, licensing, entitlement, serial, certificate, or account data;
- customer/user files, logs, calibration data, project data, database content, or existing MAX configuration;
- credentials, SSH material, browser data, service secrets, or private keys;
- raw `ProgramData` as generic payload;
- raw Windows Driver Store directories or copied INF/CAT/SYS content;
- registry exports as portable configuration payload.

No capture tool may change files, services, registry values, driver bindings, firmware, package state, activation state, or network configuration on the reference machine.

## Licensing/activation handling

Reference capture deliberately does not observe or package activation artifacts. The POC preserves existing access protection by leaving licensing and activation tooling unchanged. If a captured payload requires activation to run, the prototype records only the declaration `existing-ni-activation-tooling / unchanged`; it does not attempt to make the payload runnable outside the approved existing model.

## Capture workflow

1. Define a reviewed allowlist of source directories/files and their intended component ID.
2. Run a read-only inventory/hashing pass and generate a proposed manifest plus exclusion report.
3. Review file types, ownership confidence, size, and policy exclusions before copying any payload.
4. Copy only the approved allowlist to an access-controlled local POC staging area.
5. Rehash copied bytes, emit provenance, and validate that the manifest is deterministic.
6. Attach the result to a catalog entry marked `reference-derived-poc` and `not-redistributable`.
7. Use only non-destructive health checks and simulated activation in the UI prototype.

## Validation questions

- Can a user-mode/API capture be planned without selecting hardware support?
- Can the LabVIEW adapter be represented separately from the user-mode runtime?
- Does an update to the runtime preserve a compatible adapter and leave hardware/firmware unselected?
- Are the collected resource claims sufficient to explain which changes need elevation, restart, or explicit approval?
- Can the POC identify all content that must remain outside the artifact due to licensing, machine state, or customer data?

The capture becomes a `validated prototype result` only after these questions are tested and documented under [documentation governance](documentation-governance.md).