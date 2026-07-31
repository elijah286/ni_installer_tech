# NI Setup CLI interface

**Status:** draft v0.1 — executable, non-mutating prototype interface.
**Audience:** build engineers, container/build-agent authors, enterprise automation, installer engineering, and R&D component owners.

## Purpose

`ni-setup` is the headless counterpart to the desktop planning experience. It is designed for a CI worker, container, or other non-interactive automation host. Its JSON output and exit codes are stable integration points for the prototype; the deployment engine behind actual installation is not implemented yet.

The CLI never opens a UI and must never require a prompt in its headless path. Normal operation is non-privileged and is safe to run in a container.

## Security, licensing, and host boundary

The CLI preserves existing approved NI activation and licensing tooling. It does not carry, inspect, copy, migrate, implement, bypass, or modify activation/entitlement material.

A generic container is an API/build/runtime planning environment, not a driver or firmware installation host. The CLI therefore:

- resolves user-mode and language-adapter components for a selected profile;
- represents hardware-family, kernel-driver, and firmware implications explicitly;
- refuses real installation in this prototype unless a future deployment engine supplies the approved platform executor;
- requires `--simulate` for `install` and offline `bundle create` today;
- never attempts host Driver Store, PnP, kernel-module, service, firmware, or activation changes from a container.

A future supported container execution model must separately validate the host-driver/container-user-mode compatibility range for each API/runtime component. “Container eligible” is never inferred merely because a component has a CLI plan.

## Commands

| Command | Purpose | Mutation in v0.1 |
|---|---|---:|
| `plan` | Resolve a capability/profile into component planes and boundaries. | None |
| `bundle create --simulate` | Validate the complete artifact closure that would form one portable offline installer. | None |
| `install --non-interactive --simulate` | Validate a headless installation plan. | None |

### Common options

| Option | Meaning |
|---|---|
| `--profile recommended\|hardware\|test-system` | Selects the customer capability starting point. Default: `recommended`. |
| `--labview-release 2026-q1\|2026-q3` | Selects exactly one observed LabVIEW release line. Default: `2026-q3`. |
| `--source ni\|offline\|repository` | Source policy. Default: `ni`, the NI-hosted catalog. |
| `--repository <URL>` | Required only with `--source repository`; identifies an organization-approved catalog. |
| `--platform <OS-ARCH>` | Requested target, such as `linux-x64` or `windows-x64`. Default: `linux-x64`. |
| `--format json\|text` | Output format. Automation should use `json`. |
| `--non-interactive` | Required by `install`; declares that no prompt can be shown. |
| `--simulate` | Required for mutating command shapes in this prototype. Validates without downloading, installing, or writing a bundle. |

### Examples

Resolve the default NI-hosted plan for a Linux build container:

```sh
ni-setup plan --profile recommended --source ni --platform linux-x64 --format json
```

Resolve a plan from an organization-approved repository:

```sh
ni-setup plan --profile test-system --labview-release 2026-q1 --source repository \
  --repository https://packages.example.invalid/ni/validated-2026.1 \
  --platform windows-x64 --format json
```

Validate the exact selected closure that will become a portable offline installer:

```sh
ni-setup bundle create --simulate --profile hardware --source ni --format json
```

Validate an unattended headless install plan:

```sh
ni-setup install --non-interactive --simulate --profile recommended --source ni --format json
```

## JSON contract

On success, the CLI writes one JSON object to standard output. It identifies the selected profile, source, target platform, licensing boundary, component IDs, roles, and explicit activation boundaries.

```json
{
  "status": "planned",
  "message": "A non-mutating component plan was created.",
  "plan": {
    "schemaVersion": "prototype-plan-v0.1",
    "profile": "recommended",
    "source": "ni",
    "licensingIntegration": "existing-ni-activation-tooling",
    "licensingBehavior": "unchanged"
  }
}
```

A future production version must add catalog/artifact digests, policy/baseline identity, full compatibility evaluation, download/cache estimates, and machine-readable declined/blocked reasons. It must preserve backward compatibility through a documented schema version rather than changing fields silently.

## Exit codes

| Code | Meaning | Automation action |
|---:|---|---|
| `0` | Plan or simulation completed. | Consume JSON output. |
| `2` | Invalid command or arguments. | Correct invocation. |
| `4` | Policy violation, such as missing `--non-interactive`. | Change automation policy/invocation. |
| `6` | Requested real operation is intentionally unsupported by this prototype. | Use `--simulate` or wait for an approved deployment executor. |

Errors are emitted as JSON on standard error when `--format json` is used.

## Container image

[NISetup.Cli/Dockerfile](../NISetup.Cli/Dockerfile) builds a rootless, headless image containing only the CLI prototype. Build it from the repository root:

```sh
docker build --file NISetup.Cli/Dockerfile --tag ni-setup:prototype .
docker run --rm ni-setup:prototype plan --profile recommended --source ni --format json
```

The image intentionally does not contain NI product payloads, host-device access, driver installers, firmware updaters, or licensing/activation records. A later artifact-aware image should mount a read-only offline bundle or use authenticated catalog access supplied by the deployment environment; it must not bake customer credentials into the image.

## Path to a functional headless executor

Before changing `install` or `bundle create` from simulation to a mutating operation, the project must document and validate:

1. Catalog authentication, signature verification, mirror/offline source policy, cache location, and digest verification.
2. Non-interactive compatibility, policy, and entitlement result handling—without changing existing licensing/activation tooling.
3. Durable transaction journal, structured progress events, cancellation/resume, idempotency, and exact exit-code semantics.
4. User-mode/container execution rules and host-driver compatibility for each supported API/runtime.
5. Explicit rejection or brokered host execution for driver, service, PnP, firmware, and kernel/module operations.
6. Atomic offline bundle assembly, bundle digest/manifest verification, and no-secret/no-machine-state checks.
7. Clean-install, update, repair, removal, interruption, and disconnected-system validation.

The component output requirements that make these operations possible are defined in the [build-output contract](build-output-contract.md). Changes to CLI semantics follow [documentation governance](documentation-governance.md).
