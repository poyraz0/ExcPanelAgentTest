#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="/opt/excpanel-transfer-agent"
SERVICE_NAME="excpanel-transfer-agent"
SERVICE_USER="excpanel-agent"
SERVICE_GROUP="excpanel-agent"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AGENT_PROJECT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SOLUTION_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

require_root() {
  if [[ "${EUID}" -ne 0 ]]; then
    echo "This installer must be run as root." >&2
    exit 1
  fi
}

ensure_packages() {
  local packages=(parted util-linux e2fsprogs sudo)
  local missing=()
  for pkg in "${packages[@]}"; do
    if ! dpkg -s "${pkg}" >/dev/null 2>&1; then
      missing+=("${pkg}")
    fi
  done

  if ((${#missing[@]} > 0)); then
    apt-get update
    apt-get install -y "${missing[@]}"
  fi
}

ensure_user() {
  if ! getent group "${SERVICE_GROUP}" >/dev/null; then
    groupadd --system "${SERVICE_GROUP}"
  fi

  if ! id -u "${SERVICE_USER}" >/dev/null 2>&1; then
    useradd --system --gid "${SERVICE_GROUP}" --home-dir /var/lib/excpanel-transfer-agent --shell /usr/sbin/nologin "${SERVICE_USER}"
  fi

  install -d -o "${SERVICE_USER}" -g "${SERVICE_GROUP}" -m 0750 /var/lib/excpanel-transfer-agent
}

stop_service_if_running() {
  if systemctl is-active --quiet "${SERVICE_NAME}" 2>/dev/null; then
    echo "Stopping ${SERVICE_NAME} before updating binaries..."
    systemctl stop "${SERVICE_NAME}"
  fi
}

publish_binaries() {
  stop_service_if_running

  dotnet publish "${AGENT_PROJECT_DIR}/ExcPanel.TransferAgent.csproj" -c Release -o "${INSTALL_DIR}/agent-publish"
  dotnet publish "${SOLUTION_ROOT}/ExcPanel.TransferAgent.PrivilegedHelper/ExcPanel.TransferAgent.PrivilegedHelper.csproj" -c Release -o "${INSTALL_DIR}/helper-publish"

  install -d -m 0755 "${INSTALL_DIR}"
  cp -a "${INSTALL_DIR}/agent-publish/." "${INSTALL_DIR}/"
  cp -a "${INSTALL_DIR}/helper-publish/." "${INSTALL_DIR}/"
  chown -R root:root "${INSTALL_DIR}"
  chmod -R a+rX "${INSTALL_DIR}"
  chmod 0755 "${INSTALL_DIR}/ExcPanel.TransferAgent" "${INSTALL_DIR}/ExcPanel.TransferAgent.PrivilegedHelper"
}

install_service() {
  install -m 0644 "${SCRIPT_DIR}/excpanel-transfer-agent.service" "/etc/systemd/system/${SERVICE_NAME}.service"
  systemctl daemon-reload
  systemctl enable "${SERVICE_NAME}.service"
  systemctl start "${SERVICE_NAME}.service"
}

install_sudoers() {
  install -m 0440 "${SCRIPT_DIR}/excpanel-transfer-agent.sudoers" "/etc/sudoers.d/excpanel-transfer-agent"
  chmod 0440 "/etc/sudoers.d/excpanel-transfer-agent"
  visudo -cf "/etc/sudoers.d/excpanel-transfer-agent"
}

main() {
  require_root
  ensure_packages
  ensure_user
  publish_binaries
  install_sudoers
  install_service
  echo "ExcPanel Transfer Agent installed to ${INSTALL_DIR}"
}

main "$@"
