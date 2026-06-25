#!/usr/bin/env bash
# =============================================================================
# ExcPanel Transfer Agent — Snapshot sonrası tek komut: kurulum + setup + test
# =============================================================================
# Ubuntu 24.04 snapshot'ı geri yüklendikten sonra agent kurulumu, setup wizard
# apply (disk + domain + Samba + SFTP) ve API doğrulama testlerini sırayla çalıştırır.
#
# Tek komut (sunucuda):
#   export DOMAIN_JOIN_PASSWORD='Administrator şifresi'
#   curl -fsSL https://raw.githubusercontent.com/poyraz0/ExcPanelAgentTest/main/ExcPanel.TransferAgent/deploy/linux/fresh-server-setup.sh | sudo bash
#
# Repo zaten klonluysa (geliştirme):
#   export DOMAIN_JOIN_PASSWORD='...'
#   sudo SKIP_CLONE=1 bash /opt/excpanel/ExcPanel.TransferAgent/deploy/linux/fresh-server-setup.sh
#
# Ortam değişkenleri (opsiyonel — varsayılanlar dogrumail-demo / sftp):
#   DOMAIN_JOIN_PASSWORD   (zorunlu) AD join şifresi
#   DOMAIN_JOIN_USER       varsayılan: Administrator
#   REPO_ROOT              varsayılan: /opt/excpanel
#   REPO_URL, REPO_BRANCH
#   API_BASE, DISK_PATH, STORAGE_ROOT, COMPUTER_NAME, HOSTNAME_FQDN, ...
# =============================================================================

set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/poyraz0/ExcPanelAgentTest.git}"
REPO_BRANCH="${REPO_BRANCH:-main}"
REPO_ROOT="${REPO_ROOT:-/opt/excpanel}"
SKIP_CLONE="${SKIP_CLONE:-0}"
API_BASE="${API_BASE:-http://localhost:5000}"

# --- Demo ortam varsayılanları (sftp transfer sunucusu) ---
export API_BASE
export REPO_ROOT
export STORAGE_ROOT="${STORAGE_ROOT:-/data/excpanel-transfer}"
export DISK_PATH="${DISK_PATH:-/dev/sdb}"
export HOSTNAME_FQDN="${HOSTNAME_FQDN:-sftp.dogrumail-demo.com}"
export DNS_DOMAIN="${DNS_DOMAIN:-dogrumail-demo.com}"
export REALM="${REALM:-DOGRUMAIL-DEMO.COM}"
export WORKGROUP="${WORKGROUP:-DOGRUMAIL-DEMO}"
export DC_HOST="${DC_HOST:-dc.dogrumail-demo.com}"
export DC_IP="${DC_IP:-10.34.141.2}"
export COMPUTER_NAME="${COMPUTER_NAME:-sftp}"
export DOMAIN_JOIN_USER="${DOMAIN_JOIN_USER:-Administrator}"
export KRB5_REALM="${KRB5_REALM:-DOGRUMAIL-DEMO.COM}"
export TEST_MAILBOX="${TEST_MAILBOX:-ahmet.ertem@ofuzkal.com}"
export TEST_DOMAIN="${TEST_DOMAIN:-ofuzkal.com}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()  { echo -e "${GREEN}[fresh-setup]${NC} $*"; }
warn() { echo -e "${YELLOW}[fresh-setup]${NC} $*"; }
fail() { echo -e "${RED}[fresh-setup] ERROR:${NC} $*" >&2; exit 1; }

require_root() {
  [[ "${EUID:-$(id -u)}" -eq 0 ]] || fail "Root gerekli: ... | sudo bash"
}

require_password() {
  if [[ -z "${DOMAIN_JOIN_PASSWORD:-}" ]]; then
    cat >&2 <<'EOF'
DOMAIN_JOIN_PASSWORD tanımlı değil.

Örnek:
  export DOMAIN_JOIN_PASSWORD='...'
  curl -fsSL .../fresh-server-setup.sh | sudo bash

veya:
  curl -fsSL .../fresh-server-setup.sh | sudo DOMAIN_JOIN_PASSWORD='...' bash
EOF
    exit 1
  fi
}

clone_repo_if_needed() {
  if [[ "$SKIP_CLONE" == "1" ]]; then
    [[ -d "$REPO_ROOT/ExcPanel.TransferAgent" ]] || fail "SKIP_CLONE=1 ama repo yok: $REPO_ROOT"
    log "Klon atlandı: $REPO_ROOT"
    return
  fi

  if [[ -d "$REPO_ROOT/.git" ]]; then
    log "Repo mevcut, güncelleniyor: $REPO_ROOT"
    git -C "$REPO_ROOT" fetch origin "$REPO_BRANCH"
    git -C "$REPO_ROOT" checkout "$REPO_BRANCH" 2>/dev/null || true
    git -C "$REPO_ROOT" pull --ff-only origin "$REPO_BRANCH" || warn "git pull başarısız — mevcut commit ile devam"
    return
  fi

  log "Repo klonlanıyor: $REPO_URL -> $REPO_ROOT"
  install -d -m 0755 "$REPO_ROOT"
  git clone --branch "$REPO_BRANCH" --depth 1 "$REPO_URL" "$REPO_ROOT"
}

run_bootstrap() {
  local bootstrap="$REPO_ROOT/ExcPanel.TransferAgent/deploy/linux/bootstrap.sh"
  [[ -f "$bootstrap" ]] || fail "bootstrap.sh bulunamadı: $bootstrap"

  log "Agent kurulumu (bootstrap)..."
  SKIP_CLONE=1 REPO_ROOT="$REPO_ROOT" API_BASE="$API_BASE" KRB5_REALM="$KRB5_REALM" \
    bash "$bootstrap"
}

run_setup_and_tests() {
  local test_script="$REPO_ROOT/ExcPanel.TransferAgent/deploy/linux/new-server-agent-test.sh"
  [[ -f "$test_script" ]] || fail "Test scripti bulunamadı: $test_script"

  log "Setup wizard apply + doğrulama testleri..."
  log "  hostname=$HOSTNAME_FQDN  computer=$COMPUTER_NAME  disk=$DISK_PATH  mount=$STORAGE_ROOT"

  CONFIRM_APPLY=yes \
    DOMAIN_JOIN_PASSWORD="$DOMAIN_JOIN_PASSWORD" \
    API_BASE="$API_BASE" \
    REPO_ROOT="$REPO_ROOT" \
    STORAGE_ROOT="$STORAGE_ROOT" \
    DISK_PATH="$DISK_PATH" \
    HOSTNAME_FQDN="$HOSTNAME_FQDN" \
    DNS_DOMAIN="$DNS_DOMAIN" \
    REALM="$REALM" \
    WORKGROUP="$WORKGROUP" \
    DC_HOST="$DC_HOST" \
    DC_IP="$DC_IP" \
    COMPUTER_NAME="$COMPUTER_NAME" \
    DOMAIN_JOIN_USER="$DOMAIN_JOIN_USER" \
    TEST_MAILBOX="$TEST_MAILBOX" \
    TEST_DOMAIN="$TEST_DOMAIN" \
    bash "$test_script" --phase full
}

print_done() {
  cat <<EOF

================================================================================
Fresh server setup tamamlandı.
================================================================================
  API:      $API_BASE
  Swagger:  $API_BASE/swagger
  Storage:  $STORAGE_ROOT
  Share:    \\\\$HOSTNAME_FQDN\\PSTTransfer\$
  Servis:   systemctl status excpanel-transfer-agent

Exchange export (EXCSE02 üzerinde):
  New-MailboxExportRequest -Mailbox "$TEST_MAILBOX" -FilePath "<export/prepare exchangeFilePath>"

Tekrar test (apply olmadan):
  bash $REPO_ROOT/ExcPanel.TransferAgent/deploy/linux/new-server-agent-test.sh --phase all-safe
================================================================================

EOF
}

main() {
  require_root
  require_password
  clone_repo_if_needed
  run_bootstrap
  run_setup_and_tests
  print_done
}

main "$@"
