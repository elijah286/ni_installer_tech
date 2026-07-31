# Build-output contract for component-oriented delivery

**Status:** draft v0.1 — additive prototype contract; not an approved production packaging specification.  
**Purpose:** give NI R&D teams a stable, reviewable target for build outputs that can be assembled into the evolving installation model without changing existing installers, signing, activation, or licensing behavior.

## Scope and non-goals

The contract describes what a product build should emit **in addition to** its current authorized installer/media outputs. It gives a catalog/solver enough information to create a precise plan, explain it, stage the right payload, and validate the installed component.

This v0.1 contract does not:

- replace NIPM/MSI/MSIX or any existing release process;
- authorize redistribution of extracted installed files;
- prescribe a new activation or entitlement system;
- include licenses, activation records, entitlement tokens, credentials, private keys, customer data, or machine state;
- permit a build to embed arbitrary privileged installation scripts; or
- permit raw driver-file copying or altered signed driver packages.

## Core principles

1. **Build output is component-ready, not product-name-only.** A customer product may resolve to several independently serviceable components.
2. **One logical component release is traceable to immutable bytes.** Logical component identity and physical artifact digest are separate.
3. **Declared ownership replaces installer inference.** Builds identify the resources they own or require; deployment engines decide supported platform actions.
4. **Compatibility is explicit.** API, ABI, OS, architecture, product-host, hardware-family, firmware, and driver constraints are machine-readable.
5. **Activation stays external.** A component declares its approved licensing/activation integration requirement but never carries or transforms activation data.
6. **The contract evolves through repository documentation.** See [documentation governance](documentation-governance.md).
7. **Headless use is explicit.** A component must declare whether it is eligible for build-agent/container planning, and its host-driver compatibility must be validated before it is eligible for container execution.

## Required output layout

For every candidate independently serviceable component, the product build should emit an output directory like this:

```text
<component-id>/<version>/<platform>/
  component.json
  payload-manifest.json
  resource-claims.json
  compatibility.json
  health-check.json
  provenance.json
  sbom.spdx.json
  notices/
  payload/                         # optional in early metadata-only adoption
```

The final production archive/repository format remains open. These are logical build outputs that a packager can validate and transform deterministically.

## `component.json`: identity and selection

```json
{
  "schemaVersion": "0.1",
  "id": "daqmx.runtime.user-mode",
  "version": "26.0.0",
  "displayName": "NI-DAQmx User-Mode Runtime",
  "role": "api-runtime",
  "platform": { "os": "windows", "architecture": "x64" },
  "upgradeDomain": "daqmx-user-mode",
  "provides": ["daqmx-c-abi:26"],
  "selection": {
    "buildAgentEligible": true,
    "containerPlanningEligibility": "eligible",
    "containerExecutionEligibility": "pending-validation",
    "hostDriverCompatibilityRange": "not-applicable-or-validated-range-required",
    "defaultProfileEligibility": ["recommended-ni-foundation"]
  },
  "documentation": {
    "componentOwner": "<owning R&D team>",
    "decisionRecord": "docs/ni-daqmx-cut-point-hypothesis.md"
  }
}
```

Required fields are `schemaVersion`, `id`, `version`, `displayName`, `role`, `platform`, `upgradeDomain`, and an owning team/reference. `id` is stable, lowercase, and dot-separated; version is immutable once released. Do not use a display name, installer filename, or download URL as identity.

Every component must also declare `coexistencePolicy`, `maxSimultaneousVersions`, and, where relevant, `platformRelease`. These fields implement the [coexistence and revision policy](coexistence-and-revision-policy.md): user-mode coexistence requires explicit proof; drivers, services, configuration schemas, and firmware use singleton domains.

Allowed initial `role` values include `application`, `api-runtime`, `language-adapter`, `configuration`, `hardware-family-support`, `device-driver`, `platform-service`, `firmware`, `documentation`, `examples`, and `third-party-prerequisite`.

## `payload-manifest.json`: bytes and deterministic assembly

Every shipped file must have a relative destination, size, and SHA-256 digest. The build must define deterministic ordering and must not use build-time timestamps as semantic input.

```json
{
  "schemaVersion": "0.1",
  "files": [
    {
      "source": "payload/bin/NIDAQmx.dll",
      "destination": "bin/NIDAQmx.dll",
      "sha256": "<64 lowercase hexadecimal characters>",
      "sizeBytes": 123456,
      "mode": "replace-if-owned"
    }
  ],
  "archive": { "ordering": "lexicographic", "timestampPolicy": "normalized" }
}
```

Early adopter builds may publish a metadata-only manifest with no payload until the packaging path is ready. They must label it `payloadAvailability: "metadata-only"` in `component.json`.

Do not include any license, activation, credential, user, calibration, or machine-specific configuration file in `payload/`.

## `resource-claims.json`: ownership and privileged boundaries

Claim only resources owned by this component or explicitly consumed by it. Each claim must state install/activation/removal intent, sharing policy, and rollback behavior.

```json
{
  "schemaVersion": "0.1",
  "claims": [
    {
      "kind": "file",
      "identity": "bin/NIDAQmx.dll",
      "ownership": "exclusive",
      "activation": "none",
      "rollback": "restore-previous-owned-version"
    },
    {
      "kind": "windows-driver-package",
      "identity": "original-signed-inf-package:<published identity>",
      "ownership": "exclusive-upgrade-domain",
      "activation": "stage-and-bind-through-supported-windows-api",
      "rollback": "driver-specific-validated-plan"
    }
  ]
}
```

Driver claims must reference the **original signed** INF/CAT/SYS package and matching metadata. They must not package copied files from the Driver Store or alter signature material. Firmware claims must include device eligibility, current/target version policy, verification, recovery, and an explicit confirmation boundary.

## `compatibility.json`: constraints and relationships

Express both requirements and user-visible explanations. Do not infer requirements only from the current installer dependency graph.

```json
{
  "schemaVersion": "0.1",
  "requires": [
    {
      "capability": "daqmx-c-abi",
      "version": ">=26.0 <27.0",
      "reason": "Uses the NI-DAQmx C ABI."
    }
  ],
  "compatibleWith": [
    {
      "capability": "labview-abi",
      "version": "2026-q3-x64",
      "reason": "Validated LabVIEW integration ABI."
    }
  ],
  "deviceRules": [],
  "upgrade": {
    "canRetainCompatibleDependents": true,
    "requiresExplicitBoundary": false
  }
}
```

Language adapters must identify the exact supported language/product ABI and architecture. Hardware support must identify family-level hardware/compatible-ID rules. Driver and firmware components must explicitly mark their upgrade boundary.

## Headless and container declaration

Every component must declare separate **planning** and **execution** eligibility:

- `containerPlanningEligibility`: whether a CLI may resolve and inspect the component in a headless build/container plan.
- `containerExecutionEligibility`: `eligible`, `ineligible`, or `pending-validation`; only an approved compatibility matrix may promote it to `eligible`.
- `hostDriverCompatibilityRange`: required for a container user-mode API/runtime that relies on a host driver.
- `headlessHealthCheck`: the non-destructive health check usable without a display or prompt.

`device-driver`, `firmware`, and host service components are `ineligible` for a generic container by default. A future brokered host executor is a separate component/transaction boundary; a container may request it but must not directly mutate host Driver Store, PnP, kernel/module, firmware, service, licensing, or activation state.

The command contract, JSON output, and exit-code requirements are specified in the [CLI interface](cli-interface.md).

## `health-check.json`: verifiable post-state

Each component declares a non-destructive health check that proves its stated contract, such as loading a user-mode library, locating a LabVIEW adapter, querying a service, or verifying a staged driver package. A check must specify its prerequisites, timeout, expected result, and whether it requires a device.

Health checks must not activate products, consume or alter licenses, update firmware, or require customer data.

## `provenance.json`, SBOM, notices, and signing inputs

`provenance.json` ties the output to source revision, build pipeline/run, build environment, source artifact identifiers, and payload-manifest digest. `sbom.spdx.json` identifies first- and third-party content and license notices. The existing release-signing workflow remains authoritative; the future repository can consume its signing/provenance output without changing its trust model.

Build outputs must identify whether the payload was generated from authorized build artifacts, original signed vendor payloads, or an internal reference-derived POC capture. Only the first two can be candidates for production delivery. Reference-derived captures are POC-only.

## Licensing and activation declaration

Every component requires a `licensing` declaration:

```json
{
  "integration": "existing-ni-activation-tooling",
  "behavior": "unchanged",
  "artifactContainsEntitlementData": false,
  "installerMayEvaluateEntitlement": false,
  "notes": "Installation remains subject to the existing approved NI activation and licensing behavior."
}
```

The deployment model may invoke approved existing tooling at its existing boundary or report its result. It must not copy activation material, implement entitlement logic, bypass restrictions, downgrade protection, or change license policy.

## Adoption sequence

| Phase | Build-team action | Prototype outcome |
|---|---|---|
| 0: map | Publish component candidates, owners, and existing installer/artifact lineage. | Reviewable scope; no payload change. |
| 1: describe | Emit `component.json`, compatibility, licensing declaration, and SBOM/provenance pointers beside existing outputs. | Catalog can explain choices. |
| 2: claim | Emit payload and resource manifests; validate ownership conflicts. | Planner can calculate smallest update closure. |
| 3: package | Produce deterministic component payloads from authorized build outputs. | Repository can stage immutable POC artifacts. |
| 4: validate | Run defined clean-install, update, repair/removal, interruption, and hardware tests. | Candidate contract promotion. |

## Acceptance checks for a build output

- Schema validates and component IDs/versions are unique.
- Every payload entry has a verified digest, size, and declared destination.
- No prohibited activation, entitlement, credential, customer, or machine-state content is present.
- Every shared or privileged resource has a declared ownership and rollback policy.
- Every dependency/compatibility edge includes a human-readable reason.
- A health check is non-destructive and executable in its stated context.
- Provenance and SBOM identify the delivered bytes.
- The family documentation links the output to evidence and validation status.

## Open decisions

- Exact final schema technology and repository/archive encoding.
- Version-expression grammar and canonical API/ABI capability identifiers.
- Whether each role requires a dedicated artifact or can be a catalog-only logical component.
- Production signing metadata, key-management, and TUF-like repository policy.
- How product-specific build systems generate manifests without duplicating source-of-truth ownership data.

Changes to this document require the process in [documentation governance](documentation-governance.md).