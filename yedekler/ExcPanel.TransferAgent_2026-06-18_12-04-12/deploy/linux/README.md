# ExcPanel Transfer Agent - Linux Deployment

## Architecture

1. The transfer agent runs as the unprivileged `excpanel-agent` system user.
2. The privileged helper performs only the required root-only storage operations.
3. The helper does not open any network ports.
4. The agent invokes the helper locally through a tightly scoped `sudo` rule.
5. Production installs keep binaries root-owned under `/opt/excpanel-transfer-agent`.
6. Runtime storage settings are written to `/var/lib/excpanel-transfer-agent/appsettings.Storage.json`.
7. Disk configuration is destructive and cannot be undone.

## Install

```bash
sudo bash deploy/linux/install.sh
```

The installer:

- Creates the `excpanel-agent` user and group
- Publishes agent and helper binaries to `/opt/excpanel-transfer-agent`
- Installs a systemd unit running as `excpanel-agent`
- Installs a sudoers rule allowing only the helper executable
- Validates sudoers with `visudo`
- Enables and starts the service

## Manual Test Procedure

1. Confirm the service is running:
   ```bash
   systemctl status excpanel-transfer-agent
   ```
2. List disks:
   ```bash
   curl -s http://localhost:5000/api/storage/disks | jq
   ```
3. Run dry-run against a non-system spare disk:
   ```bash
   curl -s -X POST http://localhost:5000/api/storage/configure/dry-run \
     -H 'Content-Type: application/json' \
     -d '{"diskPath":"/dev/sdb","mountPath":"/data/excpanel-transfer"}' | jq
   ```
4. Verify helper elevation (helper must run as root via sudo, not directly):
   ```bash
   sudo -u excpanel-agent sudo -n /opt/excpanel-transfer-agent/ExcPanel.TransferAgent.PrivilegedHelper <<< '{}'
   ```
   A JSON response is expected. `NOT_ROOT` means sudo elevation failed.
5. Use the returned `confirmationCode` in configure:
   ```bash
   curl -s -X POST http://localhost:5000/api/storage/configure \
     -H 'Content-Type: application/json' \
     -d '{"diskPath":"/dev/sdb","mountPath":"/data/excpanel-transfer","fileSystem":"ext4","confirmationCode":"<token>"}' | jq
   ```
6. Verify storage status and persisted settings:
   ```bash
   curl -s http://localhost:5000/api/storage/status | jq
   sudo cat /var/lib/excpanel-transfer-agent/appsettings.Storage.json
   ```

## Warning

Configuring storage partitions, formats the selected disk, updates `/etc/fstab`, and mounts the disk. This operation is irreversible and will destroy existing data on the target disk.
