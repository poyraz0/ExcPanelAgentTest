# ExcPanel Transfer Agent

Linux transfer agent for ExcPanel storage provisioning.

## Runtime Model

1. The agent runs as a normal unprivileged service user (`excpanel-agent`).
2. The privileged helper performs only approved root-only operations.
3. The helper does not expose any network listener.
4. The agent invokes the helper through a local `sudo` call to a single fixed executable.
5. Production installs use root-owned binaries and a narrowly scoped sudoers rule.
6. Storage configuration is destructive and irreversible.

## Standalone Setup Wizard

The agent runs in **Standalone** mode by default (`AgentSecurity:Mode = Standalone`). Registration and agent auth are disabled until a future release.

### Wizard flow (panel or Swagger)

1. **Agent install** — deploy the agent and privileged helper on Linux.
2. **Prerequisites** — `GET /api/setup/prerequisites`
3. **Disk selection** — `GET /api/storage/disks`, then `POST /api/setup/plan` and `POST /api/setup/apply` with storage parameters.
4. **Domain join** — `POST /api/domain/precheck`, `POST /api/domain/join` (password never persisted or logged).
5. **Samba initialize** — included in `POST /api/setup/apply` or `POST /api/samba/initialize`.
6. **SFTP initialize** — included in setup apply or `POST /api/sftp/initialize`.
7. **Export prepare** — `POST /api/export/prepare` returns `exchangeFilePath` for EXCAPI.
8. **Exchange export test** — run export from Exchange using the UNC path.
9. **Import** — `POST /api/import/prepare` (placeholder; full import in a later release).

### Setup endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/setup/status` | Current setup state |
| GET | `/api/setup/prerequisites` | System readiness checks |
| POST | `/api/setup/plan` | Dry-run setup plan |
| POST | `/api/setup/apply` | Execute setup wizard |
| POST | `/api/setup/validate-export-path` | Validate export UNC path |
| POST | `/api/setup/test-samba-write` | Local Samba write test |
| GET/PUT | `/api/setup/config/*` | Panel setup configuration |

Setup state is persisted atomically to `setup-state.json` under the agent state directory (`/var/lib/excpanel-transfer-agent` in production).

### Configuration defaults

`appsettings.json` includes `Setup` and `AgentSecurity` sections. Optional runtime overlay: `appsettings.Setup.json` in the state directory.

## API

- `GET /api/storage/disks`
- `POST /api/storage/configure/dry-run`
- `POST /api/storage/configure`

Dry-run accepts `targetMountPath` or `mountPath`. Configure requires the single-use `confirmationCode` returned by dry-run.

## Persistent Storage Settings

Successful configure writes `appsettings.Storage.json` atomically:

```json
{
  "Storage": {
    "RootPath": "/data/excpanel-transfer"
  }
}
```

`Program.cs` loads this file optionally with `reloadOnChange` and maps `Storage:RootPath` to `TransferAgent:StorageRootPath`.

## Production Install

See [deploy/linux/README.md](deploy/linux/README.md).

## Development

```bash
dotnet run --project ExcPanel.TransferAgent
```

## Tests

```bash
dotnet test ExcPanel.TransferAgent.sln
```

Unit tests use fake command runners and do not format real disks or modify `/etc/fstab`.

## Remaining Security Notes

- The helper trusts local stdin JSON; protect host access to the service account.
- Single-use confirmation tokens are stored in process memory and expire after a short TTL.
- Sudo authorization depends on correct sudoers deployment and immutable helper binary ownership.
- Disk identity checks rely on `lsblk` serial/WWN reporting from the host environment.
