# Documentation governance for the installation-model prototype

**Status:** active project rule.  
**Audience:** NI R&D, build/release engineering, installer engineering, driver/firmware owners, security, licensing, and UX teams.

## Rule

This GitHub repository is the reviewable design record for the new installation model. Before a prototype conclusion becomes a requirement for a product build, repository, catalog, activation integration, or deployment engine, the conclusion and its evidence must be published here.

Every proposed change to build output must have a corresponding update to the [build-output contract](build-output-contract.md), or a linked proposal that states why the contract does not yet change.

This is an additive R&D rule. It does not authorize changes to existing customer delivery, signing, activation, licensing, entitlement, or security controls.

## Required documents for each component family

For each onboarded family—such as LabVIEW, NI-DAQmx, NI-VISA, TestStand, PXI Platform Services, or an instrument driver—maintain a focused document or section that includes:

1. **Customer capability and scope** — what is selected and what is deliberately outside the boundary.
2. **Candidate component boundaries** — application, API/runtime, language adapter, configuration, hardware family, kernel/driver, firmware, documentation, and prerequisites as applicable.
3. **Evidence** — source, collection time, machine/OS context, command/test method, and redaction boundary.
4. **Build-output mapping** — the owner, source pipeline, and contract fields produced for every candidate component.
5. **Resource ownership** — files, services, registry/configuration schemas, driver packages, firmware, and shared APIs.
6. **Compatibility and upgrade rules** — ABI, platform, device, driver, firmware, and migration constraints.
7. **Licensing/activation boundary** — how the component continues to use approved existing licensing/activation tooling, with no secret or entitlement data in the component artifact.
8. **Validation matrix** — clean install, update, repair, removal, interruption, offline, build-agent/container, and representative hardware tests.
9. **Open questions and decision owner** — what remains unproven and who can approve a conclusion.

## Evidence and maturity labels

Use one label at the top of each document and on material claims:

| Label | Meaning | May change product behavior? |
|---|---|---:|
| `proposal` | Candidate direction needing review. | No |
| `public-information hypothesis` | Inferred from public material. | No |
| `evidence-informed hypothesis` | Supported by observed package, runtime, or resource evidence but not fully validated. | No |
| `observed evidence` | Reproducible recorded observation with provenance. | No, by itself |
| `validated prototype result` | Tested in the defined prototype matrix. | Only in the prototype |
| `approved contract` | Reviewed and accepted by named owners. | Yes, subject to release governance |

Evidence must not contain license files, activation records, entitlement tokens, credentials, customer data, or private keys.

## Change process

1. Create or update the relevant design/evidence document.
2. State the decision, alternatives, affected component IDs, and expected customer/build impact.
3. Update the [build-output contract](build-output-contract.md) when build-produced metadata or payload boundaries change.
4. Link raw or redacted reproducible evidence under [reference-inventory](../reference-inventory/README.md) when relevant.
5. Add validation results and unresolved risks.
6. Request review from the owning product/build team plus installer, security, and licensing stakeholders when the change crosses their boundary.
7. Promote the status only when its stated evidence and approvals exist.

Avoid silent contract changes embedded only in code, build scripts, or chat discussions.

## Documentation layout

```text
README.md                                    # Project goal and documentation index
docs/documentation-governance.md             # This rule and maturity process
docs/build-output-contract.md                # Versioned build-team contract
docs/<family>-cut-point-hypothesis.md        # Family boundary proposal/evidence
docs/<family>-validation.md                  # Validation matrix and results
reference-inventory/                         # Read-only, redacted evidence
tools/                                       # Reproducible collection/validation tools
```

## Decision-record template

Use this compact structure in a family document or a new decision record:

```markdown
## Decision: <short title>

**Status:** proposal | evidence-informed hypothesis | validated prototype result | approved contract  
**Owners:** <product>, <build>, <installer>, <security/licensing as applicable>  
**Affected component IDs:** `<id>`

### Decision and customer impact

### Evidence and method

### Build-output contract impact

### Licensing/activation impact
No change to approved tooling or policy; state the integration boundary.

### Validation and rollback

### Open questions
```

## Review gate for the build-output contract

The contract may become `approved` only when:

- component owners confirm the payload boundary and compatibility promises;
- build/release engineering confirms outputs are deterministic and reproducible;
- installer engineering confirms resource claims and activation operations are representable;
- security confirms provenance/SBOM/signing inputs and secret exclusions;
- licensing confirms the declared integration preserves existing activation and access protection; and
- tests cover installation, update, repair/removal, and an interruption/rollback path appropriate to the component class.

Until then, build teams should emit contract metadata as an additive experimental output beside their existing deliverables.