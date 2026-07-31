# Windows SMB source connection

The Windows prototype uses the internal reference-source share only for controlled prototype validation:

```text
\\192.168.68.125\Files\NISetupPrototypeRepository
```

It never treats the raw NIPM intake directory as an installable package source. The UI first verifies the repository identity file at `metadata\repository.json`, then reports whether an approved component catalog is available.

## Connect from the UI

1. Open **Organization-approved repository (advanced)** on the plan screen.
2. Keep the prefilled UNC path.
3. Select **Connect and verify source**.
4. If the current Windows session lacks access, enter an SMB account with read-only access and retry.

The password is sent only to Windows' temporary SMB connection request and is cleared from the UI immediately. The application does not save credentials, map a drive letter, or add a Windows Credential Manager entry.

## Common Windows results

| UI message | Meaning and action |
|---|---|
| `Windows cannot find 192.168.68.125` | Join the same LAN/VPN and confirm the NAS is reachable. |
| `Windows rejected the SMB user name or password` | Use the NAS-specific user name and password. The account needs read access to `Files\NISetupPrototypeRepository`. |
| `Windows already has an SMB session ... under another account` | Windows permits one credential identity per server. Disconnect the existing `\\192.168.68.125\Files` session in Windows, then reconnect with the intended account. |
| `Windows denied access` | Ask the NAS administrator to grant the intended account read access; do not change the prototype to bypass access controls. |
| Connected, but source is not approved | The SMB session is working. The current repository remains an internal reference-derived POC source until a reviewed catalog and supported executor are published. |

## Guest access

The development macOS host currently mounts the share as a guest session. Windows security policy commonly blocks insecure SMB guest logons. The prototype does not change that policy or weaken SMB signing/authentication. Prefer a dedicated read-only NAS account for Windows testing. If an administrator intentionally enables guest access on a test network, enter the guest account name supplied by that NAS; do not enter or store personal credentials in source control.

## Current delivery boundary

A successful SMB connection proves that the prototype can reach and validate its controlled source repository. It does **not** make raw source intake packages safe to install. The repository's current state is not ready for customer installation: it still needs a reviewed component catalog, dependency closure, authorization/signature validation, and a supported Windows deployment executor. The UI reports this honestly and performs no mock installation or machine modification.
