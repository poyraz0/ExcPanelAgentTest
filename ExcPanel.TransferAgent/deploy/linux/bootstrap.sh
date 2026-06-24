#!/usr/bin/env bash
# =============================================================================
# ExcPanel Transfer Agent — Tek komutla boş sunucu kurulumu
# =============================================================================
# Ubuntu 24.04 (22.04 desteklenir) üzerinde repo'yu klonlar, bağımlılıkları
# kurar ve agent + privileged helper + systemd servisini yükler.
#
# Tek komut (boş sunucu):
#   curl -fsSL https://raw.githubusercontent.com/poyraz0/ExcPanelAgentTest/main/ExcPanel.TransferAgent/deploy/linux/bootstrap.sh | sudo bash
#
# Özelleştirme:
#   curl -fsSL ... | sudo REPO_ROOT=/opt/excpanel REPO_BRANCH=main bash
#
# Repo zaten klonluysa:
#   sudo REPO_ROOT=/opt/excpanel SKIP_CLONE=1 bash deploy/linux/bootstrap.sh
# =============================================================================

set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/poyraz0/ExcPanelAgentTest.git}"
REPO_BRANCH="${REPO_BRANCH:-main}"
REPO_ROOT="${REPO_ROOT:-/opt/excpanel}"
SKIP_CLONE="${SKIP_CLONE:-0}"
API_BASE="${API_BASE:-http://localhost:5000}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_SCRIPT="${SCRIPT_DIR}/install.sh"

log()  { echo "[bootstrap] $*"; }
fail() { echo "[bootstrap] ERROR: $*" >&2; exit 1; }

require_root() {
  [[ "${EUID:-$(id -u)}" -eq 0 ]] || fail "Root gerekli: curl ... | sudo bash"
}

require_ubuntu() {
  if [[ ! -f /etc/os-release ]]; then
    fail "Yalnızca Ubuntu/Debian tabanlı sistemler desteklenir."
  fi
  . /etc/os-release
  case "${ID:-}" in
    ubuntu|debian) ;;
    *) fail "Desteklenmeyen OS: ${ID:-unknown}. Ubuntu 24.04 önerilir." ;;
  esac
  log "OS: ${PRETTY_NAME:-unknown}"
}

apt_install() {
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq "$@"
}

ensure_base_packages() {
  log "Temel paketler kuruluyor..."
  apt_install \
    curl ca-certificates git \
    parted util-linux e2fsprogs sudo
}

ensure_dotnet_sdk() {
  if command -v dotnet >/dev/null 2>&1; then
    log ".NET mevcut: $(dotnet --version)"
    return
  fi

  log ".NET 8 SDK kuruluyor..."
  local version_id codename
  . /etc/os-release
  version_id="${VERSION_ID:-}"
  codename="${VERSION_CODENAME:-}"

  if [[ -z "$codename" && -n "$version_id" ]]; then
    case "$version_id" in
      24.04) codename=noble ;;
      22.04) codename=jammy ;;
      20.04) codename=focal ;;
    esac
  fi

  [[ -n "$codename" ]] || fail "Ubuntu sürümü tanınamadı (VERSION_ID=$version_id). dotnet-sdk-8.0 elle kurun."

  local ms_pkg="/tmp/packages-microsoft-prod.deb"
  curl -fsSL "https://packages.microsoft.com/config/ubuntu/${version_id}/packages-microsoft-prod.deb" -o "$ms_pkg"
  dpkg -i "$ms_pkg" >/dev/null
  rm -f "$ms_pkg"
  apt-get update -qq
  apt_install dotnet-sdk-8.0

  command -v dotnet >/dev/null 2>&1 || fail "dotnet-sdk-8.0 kurulamadı"
  log ".NET kuruldu: $(dotnet --version)"
}

clone_or_update_repo() {
  if [[ "$SKIP_CLONE" == "1" ]]; then
    [[ -d "$REPO_ROOT/ExcPanel.TransferAgent" ]] || fail "SKIP_CLONE=1 ama repo yok: $REPO_ROOT"
    log "Klon atlandı, mevcut repo kullanılıyor: $REPO_ROOT"
    return
  fi

  if [[ -d "$REPO_ROOT/.git" ]]; then
    log "Repo mevcut, güncelleniyor: $REPO_ROOT"
    git -C "$REPO_ROOT" fetch origin "$REPO_BRANCH"
    git -C "$REPO_ROOT" checkout "$REPO_BRANCH"
    git -C "$REPO_ROOT" pull --ff-only origin "$REPO_BRANCH" || true
    return
  fi

  log "Repo klonlanıyor: $REPO_URL -> $REPO_ROOT"
  install -d -m 0755 "$REPO_ROOT"
  git clone --branch "$REPO_BRANCH" --depth 1 "$REPO_URL" "$REPO_ROOT"
}

run_agent_install() {
  local install_script="$REPO_ROOT/ExcPanel.TransferAgent/deploy/linux/install.sh"
  [[ -f "$install_script" ]] || fail "install.sh bulunamadı: $install_script"
  log "Agent kurulumu başlıyor..."
  bash "$install_script"
}

verify_install() {
  log "Kurulum doğrulanıyor..."
  systemctl is-active --quiet excpanel-transfer-agent || fail "excpanel-transfer-agent servisi çalışmıyor"
  log "Servis: active"

  if command -v curl >/dev/null 2>&1; then
    local attempts=0 health
    while (( attempts < 15 )); do
      if health=$(curl -fsS "$API_BASE/api/agent/health" 2>/dev/null); then
        echo "$health"
        log "Health check OK ($API_BASE/api/agent/health)"
        return
      fi
      sleep 2
      (( attempts++ )) || true
    done
    warn_health="Health endpoint henüz yanıt vermedi — birkaç saniye sonra tekrar deneyin: curl -s $API_BASE/api/agent/health"
    log "$warn_health"
  fi
}

print_summary() {
  cat <<EOF

================================================================================
ExcPanel Transfer Agent kuruldu.
================================================================================
  Binary:   /opt/excpanel-transfer-agent
  Servis:   systemctl status excpanel-transfer-agent
  Repo:     $REPO_ROOT
  API:      $API_BASE
  Swagger:  $API_BASE/swagger

Sonraki adımlar (Setup Wizard):
  cd $REPO_ROOT/ExcPanel.TransferAgent
  bash deploy/linux/new-server-agent-test.sh --phase verify
  bash deploy/linux/new-server-agent-test.sh --phase wizard-plan

Tam wizard (disk format + domain join — dikkatli):
  CONFIRM_APPLY=yes bash deploy/linux/new-server-agent-test.sh --phase wizard-apply
================================================================================

EOF
}

main() {
  require_root
  require_ubuntu
  ensure_base_packages
  ensure_dotnet_sdk
  clone_or_update_repo
  run_agent_install
  verify_install
  print_summary
}

main "$@"
