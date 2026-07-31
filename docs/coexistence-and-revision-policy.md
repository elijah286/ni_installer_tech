# Component coexistence and revision policy

**Status:** evidence-informed prototype policy.

## Decision

A setup plan selects one primary platform/application release by default. For the initial prototype, the observed reference-machine choices are **LabVIEW 2026 Q1 x64** and **LabVIEW 2026 Q3 x64**. The plan selects one of these releases; Q3 is the default.

The catalog may permit side-by-side installation only where it explicitly declares compatibility. Product naming, an existing package-manager dependency, or a shared directory is not sufficient evidence of coexistence.

## Allowed coexistence classes

| Class | Examples | Policy |
|---|---|---|
| `one-selected-release` | LabVIEW core in one setup plan | Select exactly one primary release for the plan. A future installed-state migration may add another release only after explicit catalog validation. |
| `side-by-side-when-compatible` | user-mode applications, API/runtime revisions, language adapters | Allowed only when the catalog declares isolated resource ownership, ABI compatibility, and health checks. |
| `singleton` | driver/bus domain, platform service, configuration schema, firmware | One active compatible version. The solver must plan an upgrade/replacement or block the request. |

The prototype displays the policy on every component card. It does not claim that side-by-side runtime execution is supported until validation proves the relevant component/resource boundary.

## Catalog/build requirements

Each component output must declare:

```json
{
  "platformRelease": "labview-2026-q3-x64",
  "upgradeDomain": "daqmx-labview-2026-q3",
  "coexistencePolicy": "side-by-side-when-compatible",
  "maxSimultaneousVersions": null,
  "requires": ["labview-abi:2026-q3-x64"]
}
```

A singleton uses `maxSimultaneousVersions: 1`. A language adapter binds to its specific LabVIEW ABI. A compatible adapter for a second release is a separate component identity; it is not a replacement file copied into the first adapter.

## Initial proof matrix

1. Select LabVIEW 2026 Q1: the Q1 adapter is selected and Q3 adapter is absent.
2. Select LabVIEW 2026 Q3: the Q3 adapter is selected and Q1 adapter is absent.
3. Add an eligible user-mode component: plan declares its coexistence policy and resource claims.
4. Select a driver, firmware, service, or configuration boundary: plan shows it as singleton and requires explicit activation review.
5. CLI and UI resolve the same release identifier.
6. Before supporting an installed side-by-side scenario, validate clean install, update, repair, removal, interruption, and cross-release application/API loading on a clean Windows test machine.

## Evidence limit

The reference machine establishes that Q1/Q3 fragments coexist in its broad installed state. It does not establish that all LabVIEW, DAQmx, driver, configuration, or service revisions safely coexist. This policy therefore keeps the initial customer plan to one selected release and treats any broader installed-state coexistence as a validation question.
