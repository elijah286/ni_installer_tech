# Candidate Contract Intake

**Status:** implemented prototype workflow.  
**Audience:** product R&D, build/release engineering, installer engineering, security, and licensing reviewers.

## Purpose

The **Catalog intake** tab in NI Setup helps turn legacy installer artifacts into a reviewable candidate component contract. It is an authoring and evidence-capture workflow, not an installer and not an approval mechanism.

An operator indexes a local or mounted NI Package Manager package cache, then chooses a discovered package from the resulting list. The selected package pre-populates the candidate display name, component ID, source path, version, dependency evidence, and digest; the operator does not begin with an empty product or component-ID form.

The indexer recursively reads `.nipkg` package artifacts only. It does not execute NIPM, run an installer, or change the indexed source. Future collectors may add separately reviewable discovery for MSI, EXE, and final installed-state evidence.

For every indexed package, the service records:

- source cache root and package path;
- package name, version, and declared dependencies from the Debian-style `control` metadata inside `control.tar.gz`; and
- SHA-256 digest and indexing time.

## Local Candidate Database

The local catalog is stored at:

```text
%LocalAppData%\NISetupPrototype\candidate-contracts\candidate-contract-catalog.json
```

The candidate database contains generated evidence alongside authored R&D fields:

- review status;
- declared install mode;
- review notes and open questions; and
- reviewer identity.

Refreshing a candidate replaces only generated discovery fields. Authored review fields remain intact so a team can revisit, correct, and improve conclusions as additional evidence arrives.

The package-source index is saved beside it at:

```text
%LocalAppData%\NISetupPrototype\candidate-contracts\legacy-package-index.json
```

Rescanning a cache replaces its own discovered entries, preserving packages indexed from other approved read-only sources.

## Authority Boundary

Candidate contracts are always non-deployable. They do not populate the approved remote deployment catalog, do not change NI Setup's selectable components, and cannot execute NIPM, MSI, drivers, firmware, licensing, or activation tooling.

Before a candidate can become an approved contract, its owning teams must provide or approve the missing product semantics required by [documentation governance](documentation-governance.md): resource ownership, compatibility and upgrade rules, health/repair/removal behavior, licensing/activation integration boundary, provenance/signing/SBOM data, and an appropriate validation matrix. The approved output is then published through the separate signed component-catalog process.

## Current Limits

- The intake service is intentionally read-only with respect to legacy sources.
- It does not connect to or execute remote NIPM instances.
- It does not inspect MSI databases outside Windows-specific evidence collection.
- It does not infer that an observed file or machine mutation is required or safe to reproduce.
- It excludes licenses, entitlement data, activation records, credentials, private keys, customer configuration, and raw driver-store content from candidate evidence.