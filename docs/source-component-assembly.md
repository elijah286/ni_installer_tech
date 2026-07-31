# Source-component assembly pipeline

**Status:** executable prototype pipeline; component boundaries remain candidate-level pending ownership/dependency validation.

## Purpose

The SMB intake preserves original `.nipkg` files as evidence. [NISetup.ComponentAssembler](../NISetup.ComponentAssembler) transforms their `data.tar.gz` payloads into a **new** content-addressed source-artifact format:

```text
objects/sha256/<digest-prefix>/<artifact-digest>
metadata/component-sources/<source-component-id>.json
```

Each artifact is a deterministic ZIP containing:

```text
component.json
payload/<selected files>
```

Its manifest records the original package name and SHA-256 digest, selected payload-file hashes, excluded files, candidate role, and the unresolved work required before it can become an installable component. The new artifact is never a renamed `.nipkg`.

## Kernel and restricted-content policy

By default, the assembler excludes:

- `.sys`, `.inf`, and `.cat` files;
- Driver Store and `System32/drivers` paths;
- firmware packages;
- activation, entitlement, credential, and private-key-related paths.

The result can contain API/runtime/application payloads that originally shared a legacy package with a driver, but it is marked a candidate source artifact—not a supported installer component. Driver-only and firmware packages produce metadata with exclusions and no usable driver payload. The normal installer/catalog must reject them until an approved signed-driver or explicit firmware component exists.

## Run

After complete NIPM intake is present on the SMB share:

```sh
dotnet run --project NISetup.ComponentAssembler -- \
  --source /Volumes/Files/NISetupPrototypeRepository/incoming-reference-captures/nipkg-cache-full/2026-07-31 \
  --output /Volumes/Files/NISetupPrototypeRepository \
  --continue-on-error
```

Use `--dry-run` to create only candidate metadata. The default assembly mode creates component-source artifacts. `--include-firmware` exists only for controlled analysis; it must not be used to make firmware eligible for normal installation.

## Current verified seed

The pipeline has assembled the first four source artifacts from the staged NI-DAQmx/MAX seed:

- `ni-daqmx-runtime-core`
- `ni-daqmx-labview-support`
- `ni-daqmx-common-docs`
- `ni-max`

The new artifacts are stored under the SMB `objects/sha256/` hierarchy, separate from their original package inputs.

## What this does and does not prove

This step proves that the prototype can transform legacy source bytes into a new content-addressed artifact format while excluding kernel/signature material. It does **not** yet prove that the transformed components form a complete dependency closure, own all required resources, or can install successfully on a clean machine.

Before an artifact enters an installable catalog/channel, it must have:

1. a deliberate component boundary, not only a legacy package origin;
2. complete dependency and compatibility edges;
3. resource ownership/rollback claims;
4. SBOM/provenance and source-signature review;
5. a non-destructive health check;
6. clean-machine install, update, repair, removal, and interruption validation.

See [build-output contract](build-output-contract.md), [driver modernization and API decoupling](driver-modernization-and-api-decoupling.md), and [documentation governance](documentation-governance.md).
