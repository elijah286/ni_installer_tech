# Windows prototype source connection

The Windows prototype supports two internal source transports for controlled prototype validation:

```text
Web (preferred): http://192.168.68.125:8081/files
SMB fallback:     \\192.168.68.125\Files\NISetupPrototypeRepository
```

The web server root may expose `NISetupPrototypeRepository/`, as the current `http://192.168.68.125:8081/Files` endpoint does, or it may serve the repository root directly. The UI checks `metadata/repository.json` first, then automatically checks `NISetupPrototypeRepository/metadata/repository.json`. It never treats the raw NIPM intake directory as an installable package source.

## Connect by web server

1. Open **Organization-approved repository (advanced)** on the plan screen.
2. Select **Web source**.
3. Keep the default `http://192.168.68.125:8081/files` or enter the exact local server root you need.
4. Select **Connect and verify source**.

The app requests `<entered-url>/metadata/repository.json`, then falls back to `<entered-url>/NISetupPrototypeRepository/metadata/repository.json`. It does not add credentials to the URL or save any authentication material. The server must be reachable from the Windows test machine and permit a read-only `GET` request to that file.

## Web source results

| UI message | Meaning and action |
|---|---|
| `did not respond within 10 seconds` | Verify the URL, port, Windows/local firewall, and that the server listens on its LAN interface rather than only `localhost`. |
| `HTTP 401` or `HTTP 403` | Configure read-only access to the repository metadata or use the web server's supported authentication scheme. The URL field must not contain credentials. |
| `metadata/repository.json was not found` | Set the server root to `NISetupPrototypeRepository`, not an `objects` or intake subdirectory. |
| repository identity is not recognized | The endpoint is reachable but is not serving the approved prototype repository metadata. |
| Connected, but source is not approved | The endpoint is correct. The source remains an internal POC until it is marked ready and a reviewed managed-install catalog is published. |

## Connect by SMB

1. Open **Organization-approved repository (advanced)** on the plan screen.
2. Select **SMB share**.
3. Keep the prefilled UNC path.
4. Select **Connect and verify source**.
5. If the current Windows session lacks access, enter an SMB account with read-only access and retry.

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

## Managed deployment boundary

A successful source connection does **not** make raw source intake packages safe to install. Before the UI enables installation, the web repository must be marked `ready` and provide `metadata/catalogs/prototype-managed-install-catalog-v0.1.json`. Every selected logical component must be approved in that catalog, declare `managed-file-copy`, have an immutable SHA-256 artifact digest and version, and be available under the content-addressed object tree.

For each approved component, the prototype streams and verifies the artifact, extracts only ZIP entries below `payload/`, and writes only below `%LocalAppData%\NISetupPrototype\components`. Before any payload directory is moved into that location, it persists an ownership record in `%LocalAppData%\NISetupPrototype\ledger.json`. Uninstall removes every non-removed ledger record, including an interrupted transaction, and clears the app-owned transaction staging tree. It records every preflight, download, verification, install, rollback, and uninstall outcome as JSONL under `%LocalAppData%\NISetupPrototype\logs`.

This is not a native NI product installer. It does not execute NIPM/MSI payloads or alter drivers, services, firmware, licensing, activation, credentials, customer configuration, or system-wide locations. The current repository remains blocked because it has no reviewed catalog; that blocked result is logged rather than reported as a successful installation.
