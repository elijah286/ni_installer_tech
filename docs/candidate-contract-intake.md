# Candidate Contract Intake

**Status:** implemented prototype workflow.  
**Audience:** product R&D, build/release engineering, installer engineering, security, and licensing reviewers.

## Purpose

The **Catalog intake** tab in NI Setup helps turn legacy installer artifacts into a reviewable candidate component contract. It is an authoring and evidence-capture workflow, not an installer and not an approval mechanism.

An operator supplies a proposed product/capability, a candidate component ID, and one or more local paths. A path can be a single file, a mounted cache, or a read-only network directory. The intake service recursively reads only these artifact types:

- `.nipkg`
- `.msi`
- `.exe`
- `.cab`
- `.zip`

For every artifact it can read, the service records its path, file name, type, byte size, and SHA-256 digest. For `.nipkg` artifacts it also reads the Debian-style package `control` metadata inside `control.tar.gz`, recording package name, version, and declared dependencies. MSI artifacts are fingerprinted but their tables, resource claims, and custom actions are deliberately left as evidence gaps until a Windows-specific collector captures them.

## Local Candidate Database

The local catalog is stored at:

```text
%LocalAppData%\NISetupPrototype\candidate-contracts\candidate-contract-catalog.json
```

The database contains generated evidence alongside authored R&D fields:

- review status;
- declared install mode;
- review notes and open questions; and
- reviewer identity.

Refreshing a candidate replaces only generated discovery fields. Authored review fields remain intact so a team can revisit, correct, and improve conclusions as additional evidence arrives.

## Authority Boundary

Candidate contracts are always non-deployable. They do not populate the approved remote deployment catalog, do not change NI Setup's selectable components, and cannot execute NIPM, MSI, drivers, firmware, licensing, or activation tooling.

Before a candidate can become an approved contract, its owning teams must provide or approve the missing product semantics required by [documentation governance](documentation-governance.md): resource ownership, compatibility and upgrade rules, health/repair/removal behavior, licensing/activation integration boundary, provenance/signing/SBOM data, and an appropriate validation matrix. The approved output is then published through the separate signed component-catalog process.

## Current Limits

- The intake service is intentionally read-only with respect to legacy sources.
- It does not connect to or execute remote NIPM instances.
- It does not inspect MSI databases outside Windows-specific evidence collection.
- It does not infer that an observed file or machine mutation is required or safe to reproduce.
- It excludes licenses, entitlement data, activation records, credentials, private keys, customer configuration, and raw driver-store content from candidate evidence.