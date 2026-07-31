# SMB prototype component repository

**Status:** validated prototype infrastructure decision.
**Owners:** installer prototype / build-output research.
**Decision date:** 2026-07-31.

## Decision

The source-component store for the working prototype is the internal SMB share mounted on the development host as `/Volumes/Files/NISetupPrototypeRepository`.

GitHub remains the source of truth for code, schemas, catalogs, manifests, evidence, tooling, tests, and documentation. The SMB share stores large component objects, controlled reference-machine captures, and generated offline bundles. Payload bytes are not committed to GitHub.

## Repository shape

```text
/Volumes/Files/NISetupPrototypeRepository/
  incoming-reference-captures/  # approved, read-only source captures
  objects/sha256/               # immutable content-addressed component objects
  metadata/
    catalogs/                   # catalog revisions
    channels/                   # NI-hosted and approved-baseline policy
    repository.json             # repository identity and exclusions
  bundles/                      # complete portable offline installers
  quarantine/                   # not eligible for planning/use
  logs/                         # non-secret build/capture audit logs
```

The initial `repository.json` classifies this location as `internal-reference-derived-poc-only`. The first staged POC seed now consists of original NIPM package artifacts copied read-only from `DEMODOG` into `incoming-reference-captures/nipkg-cache-seed/`: NI-DAQmx 26.0 runtime/LabVIEW support/docs, MAX 26.5, and a partial LabVIEW 2026 Q3 x64 source set. SHA-256 digests were calculated after transfer and recorded in local NAS manifests. The seed is not yet a complete dependency closure or an installable clean-machine package set.

## Why SMB for this phase

- It accommodates large payloads and bundles without repository-size constraints.
- It separates prototype bytes from GitHub review material.
- It is accessible to the local macOS development environment and can later be consumed by a Windows clean-machine test environment.
- It supports the first end-to-end flow: component capture → immutable object → catalog → selected offline bundle → disconnected clean-machine test.

SMB is a prototype transport, not the final customer-hosted repository protocol. The default product experience remains a small installer that retrieves selected components from an NI-hosted catalog. An internal SMB source can model an organization-approved/offline source during development.

## Content and safety rules

The share must never contain activation/entitlement records, license files, credentials, private keys, customer data, current machine configuration, calibration data, raw Driver Store content, or copied/altered signed Windows driver payloads.

Reference-derived POC component material must be read/copy-only from an approved allowlist and accompanied by:

- identity/version and `reference-derived-poc` classification;
- source path and observed-package evidence;
- SHA-256 file/payload manifests and byte counts;
- provenance and exclusion reports;
- candidate resource claims and compatibility metadata;
- non-destructive health checks.

The original NIPM package cache is an additional controlled POC input when a package artifact is individually allowlisted. The repository stores package files only—not the package-manager configuration, activation/entitlement state, logs, generic ProgramData content, or raw Driver Store files. Package-cache artifacts remain POC-only until signature/provenance validation and an authorized production build-output path exist.

See [reference-component POC capture](reference-component-poc-capture.md) and the [build-output contract](build-output-contract.md).

## Clean-machine success criterion

The eventual working-prototype demonstration succeeds only when a clean, supported Windows test machine can:

1. download the small bootstrap installer;
2. resolve its plan from the default NI-hosted-equivalent prototype channel;
3. retrieve and verify the exact component closure from the configured source;
4. install the selected supported platform components;
5. preserve the existing licensing/activation model without copying or changing its artifacts;
6. complete declared health checks; and
7. record a recoverable component ledger.

An equivalent portable offline bundle must be able to install the same selected closure on a disconnected clean test machine after bundle verification. This criterion is **not yet validated**: current CLI and UI behavior only plan/simulate, and no component payload has been staged on the SMB share.

## Next implementation steps

1. Verify remote SHA-256 and package signatures/provenance, then promote reviewed seed artifacts to `objects/sha256/`.
2. Resolve the complete LabVIEW/MAX/DAQmx dependency closure, including shared prerequisites, from authoritative package metadata.
3. Generate a catalog/channel that the CLI can resolve from SMB for development and from an NI-hosted-equivalent endpoint for the default UX.
4. Implement an approved package executor and atomic offline-bundle assembly.
5. Validate the clean-machine flow in a disposable Windows VM before claiming functional installation support.
