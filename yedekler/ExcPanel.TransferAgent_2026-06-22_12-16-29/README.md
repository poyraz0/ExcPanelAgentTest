# ExcPanel Transfer Agent

Linux transfer agent for ExcPanel storage provisioning.

## Runtime Model

1. The agent runs as a normal unprivileged service user (`excpanel-agent`).
2. The privileged helper performs only approved root-only operations.
3. The helper does not expose any network listener.
4. The agent invokes the helper through a local `sudo` call to a single fixed executable.
5. Production installs use root-owned binaries and a narrowly scoped sudoers rule.
6. Storage configuration is destructive and irreversible.

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
