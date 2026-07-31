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

The initial `repository.json` classifies this location as `internal-reference-derived-poc-only`; it is intentionally empty of payloads until a controlled capture has been reviewed.

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

1. Add the repository location to prototype configuration without committing SMB credentials.
2. Implement a read-only, allowlist-driven capture tool for the initial LabVIEW/MAX/DAQmx user-mode POC scope.
3. Generate payload manifests, provenance/exclusion reports, and content-addressed objects in `incoming-reference-captures/` then promote reviewed objects to `objects/sha256/`.
4. Generate a catalog/channel that the CLI can resolve from SMB for development and from an NI-hosted-equivalent endpoint for the default UX.
5. Implement atomic offline-bundle assembly.
6. Validate the clean-machine flow in a disposable Windows VM before claiming functional installation support.
