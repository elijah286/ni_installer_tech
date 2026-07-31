# Testing and CI

The deployment proof of concept uses isolated integration-style tests in [NIInstallerTech.Tests](../NIInstallerTech.Tests). The tests run the real `ManagedDeploymentService` against an in-memory HTTP repository and a unique temporary deployment root. They never contact the internal repository and never write outside their temporary directory.

Current coverage verifies that:

- a missing approved catalog blocks deployment and produces a JSONL log entry;
- an approved artifact is hash-verified, deployed only below the managed root, recorded in the ledger, and removed by uninstall;
- a tampered artifact cannot create an installed component directory;
- ZIP payload path traversal is rejected; and
- a failed later component rolls back components deployed earlier in the same transaction; and
- an interrupted deployment recorded before payload mutation removes its pending payload and all app-owned transaction staging during uninstall.

Run the suite locally from the repository root:

```sh
dotnet test NIInstallerTech.Tests/NIInstallerTech.Tests.csproj --configuration Release
```

GitHub Actions runs the same suite on pull requests and pushes to `main`, after building the UI, CLI, and component assembler. Test result files are retained as workflow artifacts.

The Samba-backed repository round trip is intentionally opt-in and is not run by public CI. It downloads the staged source artifacts, deploys them to a unique temporary directory, verifies ledger ownership, and uninstalls them. Run it only from a trusted LAN host with access to the prototype repository:

```sh
NI_SETUP_LIVE_REPOSITORY_URL=http://192.168.68.125:8081/Files/NISetupPrototypeRepository/ \
	dotnet test NIInstallerTech.Tests/NIInstallerTech.Tests.csproj --configuration Release --filter "Category=LiveRepository"
```