#!/usr/bin/env bash
set -euo pipefail

USERNAME="${1:-exp_565e314e48f3}"
JOB_ID="${2:-565e314e-48f3-4480-8270-4efa5d5f2b63}"

JOB_PATH="/data/excpanel-transfer/exports/${JOB_ID}"
FILES_PATH="/var/lib/excpanel-sftp/chroots/${USERNAME}/files"
HELPER="/opt/excpanel-transfer-agent/ExcPanel.TransferAgent.PrivilegedHelper"

require_root() {
  if [[ "${EUID}" -ne 0 ]]; then
    echo "Run as root: sudo $0 [username] [jobId]" >&2
    exit 1
  fi
}

require_root

echo "== Paths =="
echo "JOB_PATH=${JOB_PATH}"
echo "FILES_PATH=${FILES_PATH}"
ls -la "${JOB_PATH}" || true
ls -la "${FILES_PATH}" || true
echo

echo "== findmnt before =="
findmnt -n -M "${FILES_PATH}" || echo "(not mounted)"
echo

echo "== manual mount --bind =="
if findmnt -n -M "${FILES_PATH}" >/dev/null 2>&1; then
  umount "${FILES_PATH}" || umount -l "${FILES_PATH}" || true
fi

if [[ -d "${FILES_PATH}" ]]; then
  rmdir "${FILES_PATH}" 2>/dev/null || rm -rf "${FILES_PATH}"
fi
mkdir -p "${FILES_PATH}"
chown root:root "${FILES_PATH}"
chmod 0755 "${FILES_PATH}"

if mount --bind "${JOB_PATH}" "${FILES_PATH}"; then
  echo "manual mount: OK"
else
  echo "manual mount: FAILED (exit $?)"
  exit 1
fi

echo
echo "== findmnt after manual mount =="
findmnt -n -M "${FILES_PATH}" -o TARGET,FSROOT,SOURCE,FSTYPE
echo
ls -la "${FILES_PATH}"
echo

echo "== helper ensureBindMount =="
umount "${FILES_PATH}" || true
rmdir "${FILES_PATH}" 2>/dev/null || rm -rf "${FILES_PATH}"
mkdir -p "${FILES_PATH}"
chown root:root "${FILES_PATH}"
chmod 0755 "${FILES_PATH}"

REQUEST="$(cat <<EOF
{"requestId":"diag-1","action":"sftp.user.status","payload":{"username":"${USERNAME}","chrootPath":"/var/lib/excpanel-sftp/chroots/${USERNAME}","filesMountPath":"${FILES_PATH}","jobPath":"${JOB_PATH}","exportGroupName":"excpanel-sftp-export","ensureBindMount":true}}
EOF
)"
echo "${REQUEST}" | "${HELPER}"
echo

echo "== findmnt after helper =="
findmnt -n -M "${FILES_PATH}" -o TARGET,FSROOT,SOURCE,FSTYPE || echo "(not mounted)"
ls -la "${FILES_PATH}" || true
