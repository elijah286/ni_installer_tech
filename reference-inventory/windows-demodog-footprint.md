# Windows reference footprint measurement

**Collected:** 2026-07-31, read-only, from `DEMODOG` (Windows 11 Enterprise x64).  
**Purpose:** establish an observed upper-bound reference for a complete locally cached Windows NI software catalog. This is not a shipping-size estimate until artifact deduplication, package provenance, and release policy are verified.

| Location | Observed size | Interpretation |
|---|---:|---|
| `C:\ProgramData\National Instruments\NI Package Manager` | **29.49 GB** | Local NI Package Manager metadata/cache location; 1,170 files. This is the closest observed proxy for currently available installable content on the reference machine. |
| `C:\Program Files\National Instruments` | **11.00 GB** | Installed 64-bit program payload. |
| `C:\Program Files (x86)\National Instruments` | **12.85 GB** | Installed x86 compatibility/legacy/tooling payload on the x64 host. |
| `C:\ProgramData\National Instruments` | **48.33 GB** | Machine configuration, caches, support data, firmware, services, application data, and NI Package Manager data. It must not be treated as a package-repository size because it includes installed-state/configuration data. |

## Interpretation

- A customer should **not** download all software to have a modern setup experience. A normal plan should download only its resolved component closure.
- If the question is “what space is needed for an offline Windows repository with everything currently cached on this machine,” the evidence-based first answer is **roughly 30 GB**, before verifying duplicate artifacts, cache retention, and required packages absent from this machine.
- If the question is “how much disk space does this particular broad installation consume,” the directly observed NI locations total about **72 GB** (`11.00 + 12.85 + 48.33`). This is not a clean installed-product footprint because `ProgramData` includes cache and configuration.
- A single complete Windows catalog that covers products and hardware families not installed on `DEMODOG` will be larger than the observed 29.49 GB cache. Linux desktop and NI Linux Real-Time repositories are separate artifact sets and must be measured independently.

## Recommended repository design

1. Keep immutable payloads in a content-addressed repository; store each digest once across products and releases.
2. Separate a small signed catalog/compatibility index from payload blobs.
3. Offer three distribution modes:
   - **connected:** download only the resolved plan;
   - **approved mirror:** cache organization-selected baselines and hardware families;
   - **offline bundle:** export only a declared profile and its transitive closure.
4. Never use an installed machine’s Program Files/ProgramData directories as production installer payload sources. Use authorized, signed source artifacts and a curated catalog.
5. Make catalog sizing a solver result: show exact download, cache, temporary staging, installed, and retained rollback sizes for the chosen plan.
