# Component repository and packaging architecture

## Recommendation

Use a **package-based system**, but do not reproduce the old model of one monolithic product package or one unique mutable feed per revision.

The long-term system should have three independent things:

1. **Immutable component artifacts** — the exact bytes for one independently serviceable component revision.
2. **A signed compatibility catalog** — component metadata, capability mappings, dependency/compatibility constraints, hardware-family rules, and upgrade/migration rules.
3. **Channels or baselines** — small signed policy documents that select approved catalog revisions for an NI release train or customer organization.

Artifacts never change after publication. Catalogs and channels advance. This makes a plan reproducible and lets an upgrade modify only the smallest compatible closure.

## Reference-machine POC versus production artifacts

The reference Windows machine is evidence for the final state and relationships. For this internal prototype, selected final-state components may be captured into a controlled, read-only, non-redistributable POC repository. This lets the prototype exercise real component topology, digests, manifests, planning, and upgrade isolation.

That narrow POC exception does not make an installed machine a production package source.

- Do not redistribute reference-derived content or claim it is a clean-machine installer.
- Do not copy `ProgramData`, registry exports, Driver Store directories, legacy package caches, user data, or machine configuration as general payload.
- Reconstruct artifacts from authorized build outputs and original signed payloads.
- Preserve signed Windows driver packages as original INF/CAT/SYS units; a deployment package may *reference* and stage them, but must not rebuild their signing payload.
- Exclude licenses, activation data, credentials, user data, calibration data, and machine-specific configuration from general component artifacts.
- Keep the current NI activation and licensing tooling and policy unchanged. The delivery model may integrate with its approved result but must not carry, evaluate, bypass, or modify entitlement data.

The detailed prototype boundary and capture record are in [reference-component-poc-capture.md](reference-component-poc-capture.md). The required future build outputs are specified in [build-output-contract.md](build-output-contract.md).

For the NAS prototype, it is acceptable to stage a controlled internal artifact set that represents a few selected components. Treat it as an experimental repository, not a customer distribution mechanism.

## The two levels of package identity

### Component release

A component release is the solver and upgrade unit.

```yaml
id: daqmx.runtime.user-mode
version: 26.1.0
platform: windows-x64
role: api-runtime
apiContracts:
  - daqmx-c-abi: ">=26.0 <27.0"
upgradeDomain: daqmx-user-mode
artifacts:
  - sha256: 7f...e2
```

A component can have several artifacts, for example language-specific docs and binaries, but they resolve as one compatibility identity.

### Artifact

An artifact is a sealed byte payload with a content digest.

```yaml
sha256: 7f...e2
format: ni-component-v1
size: 124251238
compression: zstd
signatures: [ni-release-signature]
sbom: sha256: 91...aa
provenance: build-2026.1.0.42
```

The digest is the physical identity. The component version is the logical identity. Do not use a URL, feed path, installer filename, or product display name as identity.

## Recommended component granularity

A component boundary must align with **an independently serviceable contract or activation boundary**, not a source repository, MSI feature, or directory.

| Layer | Example component | Can update independently? | Must contain |
|---|---|---:|---|
| Capability profile | `profile.recommended-ni-foundation` | Yes; catalog-only | Selection rules; no payload. |
| Application | LabVIEW, TestStand, FlexLogger, InstrumentStudio, DIAdem | Usually | Application payload and its declared API/runtime requirements. |
| API/runtime | `daqmx.runtime.user-mode` | Usually | Public/user-mode ABI, runtime libraries, health tests. |
| Language adapter | `daqmx.labview.2026-q1.x64`, `daqmx.dotnet.net48`, `daqmx.python.cp311` | Yes | Binding-specific ABI, development tooling, examples if requested. |
| Configuration | `daqmx.configuration` | Yes | Configuration schema, discovery UI/service claims. |
| Hardware family | `daqmx.compactdaq-support`, `daqmx.pxi-daq-support` | Usually | Family matching rules and family user-mode support. |
| Bus/kernel | `windows.pxi.kernel`, `windows.usb-daq.kernel` | Only inside its upgrade domain | Original signed driver package, PnP matching, activation/rollback data. |
| Firmware | `daqmx.cdaq-firmware` | Yes, explicit transaction | Firmware artifact, eligible devices, recovery policy. |
| Optional content | docs, examples, localized help | Yes | Content only. |
| Shared prerequisite | VC++ runtime, .NET runtime, OpenSSL, TDMS | Policy constrained | Supplier, SBOM, servicing and coexistence rules. |

The first goal is not to split every DLL. It is to establish useful upgrade isolation: API-only versus device support, language adapters, hardware families, firmware, services, and optional content.

## Artifact format

Use a simple, deterministic archive for ordinary user-mode components:

```text
component.nipkg2
  manifest.json
  payload/...
  resource-claims.json
  health-check.json
  sbom.spdx.json
  signatures/...
```

Requirements:

- reproducible archive ordering, timestamps, and compression settings;
- SHA-256 or stronger digest over the exact artifact;
- signed manifest and catalog; separate signing/provenance records;
- SBOM and third-party-license metadata;
- declarative file, registry, service, and configuration claims;
- no arbitrary privileged scripts in ordinary packages;
- migrations represented as constrained declarative operations or narrowly audited native operations.

Driver artifacts are special. Store the signed vendor/NI driver package unmodified alongside a deployment manifest that states hardware IDs, Driver Store staging action, service/PnP activation, restart semantics, and rollback boundary.

## NAS repository shape for the prototype

Serve the NAS over authenticated HTTPS if possible. SMB can be used only as an internal early experiment; HTTP(S) supports resumable range downloads, caching, proxying, and a future customer repository model more naturally.

The selected internal SMB experiment is now documented in [nas-prototype-repository.md](nas-prototype-repository.md). It holds prototype payloads and generated bundles only; GitHub remains the source of truth for metadata, tooling, and review documentation.

```text
ni-repository/
  metadata/
    root.json                 # signing-root / key policy
    catalogs/
      2026.1.0.json           # immutable component + compatibility catalog
    channels/
      stable.json             # points to approved catalog digest
      prototype.json
    baselines/
      lab-validated-2026.1.json
  objects/
    sha256/7f/7f...e2         # artifact bytes
    sha256/91/91...aa         # SBOM bytes
  drivers/
    sha256/ab/ab...cd         # original signed driver archive
```

Use a TUF-like signed metadata model: a long-lived root of trust, separately signed catalog/channel metadata, key rotation, expiry, and consistent snapshots. The prototype can begin with one offline signing key and a static signed catalog, but should retain the same identity model.

## Delivery modes and offline bundle

The default installer is deliberately small. It uses the NI-hosted catalog/channel, resolves the selected plan, and retrieves only the content-addressed artifacts required for that plan. An organization may later configure an approved repository or baseline, but this is an advanced policy choice rather than the normal setup path.

After a plan is resolved, the same UX can offer **Create offline installer**. This operation downloads the complete resolved artifact closure, verifies every digest, and generates one atomic portable bundle containing:

```text
ni-offline-installer/
  bootstrapper/                 # small plan-aware installer host
  plan.json                     # resolved component IDs, versions, catalog digest
  objects/sha256/...            # exact selected artifacts, once per digest
  metadata/                     # catalog subset, manifests, SBOM/provenance
  bundle-manifest.json          # bundle digest, size, target platform, expiry/policy
```

The bundle contains only selected component artifacts and metadata. It must not include activation or entitlement data, credentials, customer configuration, user data, or raw Driver Store state. On the disconnected destination, it still performs normal compatibility checks and invokes existing approved licensing/activation tooling without changing that tooling or policy. Driver/firmware activation remains an explicit boundary at the destination.

An offline bundle is atomic from the transport perspective: its manifest lists the exact complete artifact closure, and the bootstrapper rejects incomplete or altered contents. It is not an opaque monolithic product installer; its plan remains inspectable and records the independently serviceable component identities.

## Upgrade behavior

A channel does **not** contain copies of every package. It pins a catalog digest and policy.

```yaml
channel: lab-validated-2026.1
catalogDigest: sha256:...
policy:
  allowedUpgradeDomains:
    - daqmx-user-mode
    - teststand-runtime
  firmware: explicit-approval
  kernelChanges: explicit-plan-review
```

When a user requests an update:

1. Read the installed state ledger: component versions, selected dependency edges, resource claims, and health state.
2. Read the selected channel/catalog.
3. Solve for the smallest compatible change closure.
4. Download missing content by digest into a local content-addressed cache.
5. Stage artifacts; stage driver packages without binding them.
6. Apply migrations and activate only the affected upgrade domain.
7. Run declared health checks.
8. Commit the new state ledger or restore the previous verified activation state.

The upgrade plan must distinguish:

- **changed** — requested or required by a compatibility edge;
- **retained** — verified compatible and deliberately untouched;
- **available but not selected** — updates that are unrelated;
- **blocked** — conflict, policy, missing artifact, or incompatible hardware/firmware;
- **requires an explicit boundary** — kernel, shared service/schema, or firmware change.

## Performance rules

- Content-address artifacts: one local cached copy per digest, even if multiple components reference it.
- Chunk large artifacts and support resumable range downloads; add binary deltas only after measuring that they outperform chunk reuse.
- Keep a local cache separate from installed state; eviction must never remove an artifact still needed for rollback or a configured offline baseline.
- Resolve and prefetch the entire plan before privileged activation.
- Avoid unpacking/copying unchanged content on update.
- Keep customer-facing profiles small; do not pre-install every driver family “just in case.”

## Initial prototype scope

Use a small curated catalog and repository rather than attempting to package all 988 observed NIPM packages:

1. `profile.recommended-ni-foundation` — catalog-only profile.
2. `labview.core` — mock/placeholder payload and health check.
3. `daqmx.api-runtime` — mock user-mode payload.
4. `daqmx.labview-adapter.2026-q1` — mock language adapter.
5. `daqmx.compactdaq-support` — mock hardware-family pack.
6. `daqmx.cdaq-firmware` — explicit, disabled-by-default firmware item.
7. `daqmx.configuration` — mock configuration/discovery item.
8. `teststand.core` and `instrumentstudio.core` — optional application components.

Use real metadata shapes, digests, dependency edges, upgrade domains, and plan calculations even if most payloads remain mocked initially. The prototype succeeds when it demonstrates that updating `daqmx.api-runtime` can retain the LabVIEW adapter and leave hardware/firmware untouched unless a declared constraint requires a change.
