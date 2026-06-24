#!/usr/bin/env bash
# =============================================================================
# ExcPanel Transfer Agent — Yeni Sunucu Kurulum & Test Scripti
# =============================================================================
# Ubuntu 24.04 üzerinde sıfırdan agent kurulumunu ve Setup Wizard akışını test eder.
#
# Kullanım:
#   Boş sunucu — tek komut agent kurulumu:
#     curl -fsSL https://raw.githubusercontent.com/poyraz0/ExcPanelAgentTest/main/ExcPanel.TransferAgent/deploy/linux/bootstrap.sh | sudo bash
#
#   Sonrasında (wizard testleri):
#   1) Bu dosyadaki "YAPILANDIRMA" bölümünü düzenleyin
#   2) Adımları sırayla çalıştırın
#
#   chmod +x deploy/linux/new-server-agent-test.sh
#   bash deploy/linux/new-server-agent-test.sh --phase verify
#   bash deploy/linux/new-server-agent-test.sh --phase verify
#   bash deploy/linux/new-server-agent-test.sh --phase wizard-plan
#
# DİKKAT: --phase wizard-apply diski formatlayabilir. Bilinçli kullanın.
# =============================================================================

set -euo pipefail

# -----------------------------------------------------------------------------
# YAPILANDIRMA — sunucunuza göre düzenleyin
# -----------------------------------------------------------------------------
API_BASE="${API_BASE:-http://localhost:5000}"
REPO_ROOT="${REPO_ROOT:-/opt/excpanel}"                    # bootstrap.sh varsayılanı ile aynı
STORAGE_ROOT="${STORAGE_ROOT:-/data/excpanel-transfer}"
DISK_PATH="${DISK_PATH:-/dev/sdb}"                          # formatlanacak boş disk (DİKKAT)
HOSTNAME_FQDN="${HOSTNAME_FQDN:-transfer01.dogrumail-demo.com}"

# AD / Domain
DNS_DOMAIN="${DNS_DOMAIN:-dogrumail-demo.com}"
REALM="${REALM:-DOGRUMAIL-DEMO.COM}"
WORKGROUP="${WORKGROUP:-DOGRUMAIL-DEMO}"
DC_HOST="${DC_HOST:-dc.dogrumail-demo.com}"
DC_IP="${DC_IP:-10.34.141.2}"
COMPUTER_NAME="${COMPUTER_NAME:-transfer01}"
AD_GROUP='DOGRUMAIL-DEMO\Exchange Trusted Subsystem'
SHARE_NAME='PSTTransfer$'

# Export test
TEST_JOB_ID="${TEST_JOB_ID:-$(uuidgen 2>/dev/null || echo '00000000-0000-0000-0000-000000000001')}"
TEST_MAILBOX="${TEST_MAILBOX:-setup-test@excpanel.local}"
TEST_DOMAIN="${TEST_DOMAIN:-excpanel.local}"

# Domain join şifresi — komuta yazmayın, ortam değişkeni kullanın:
#   export DOMAIN_JOIN_PASSWORD='...'
DOMAIN_JOIN_USER="${DOMAIN_JOIN_USER:-Administrator}"

# Renkler
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()  { echo -e "${GREEN}[OK]${NC} $*"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
fail() { echo -e "${RED}[FAIL]${NC} $*" >&2; exit 1; }
section() { echo; echo "========== $* =========="; }

need_root() { [[ "${EUID:-$(id -u)}" -eq 0 ]] || fail "Bu adım root gerektirir: sudo bash $0 $*"; }

curl_json() {
  local method="$1" url="$2" body="${3:-}"
  if [[ -n "$body" ]]; then
    curl -sS -X "$method" "$url" -H "Content-Type: application/json" -d "$body"
  else
    curl -sS -X "$method" "$url"
  fi
}

pretty() {
  if command -v jq >/dev/null 2>&1; then
    jq .
  else
    cat
  fi
}

assert_success() {
  local json="$1"
  if command -v jq >/dev/null 2>&1; then
    local ok
    ok=$(echo "$json" | jq -r '.success // empty')
    [[ "$ok" == "true" ]] || { echo "$json" | pretty; fail "API success=false"; }
  fi
}

# =============================================================================
# FAZ 0: Sunucu hazırlığı (root)
# =============================================================================
phase_prep() {
  need_root
  section "FAZ 0 — Sunucu hazırlığı"

  echo "OS: $(. /etc/os-release && echo "$PRETTY_NAME")"
  echo "Hostname: $(hostname -f 2>/dev/null || hostname)"

  apt-get update
  apt-get install -y \
    curl jq ca-certificates \
    parted util-linux e2fsprogs sudo acl \
    samba winbind krb5-user smbclient \
    openssh-server \
  || warn "Bazı paketler kurulamadı — setup prerequisites bunu raporlar"

  # .NET 8 (install.sh dotnet publish kullanır)
  if ! command -v dotnet >/dev/null 2>&1; then
    warn ".NET SDK yok — kuruluyor..."
    apt-get install -y dotnet-sdk-8.0 || fail ".NET 8 SDK kurulamadı. Manuel: https://dotnet.microsoft.com/download/dotnet/8.0"
  fi
  dotnet --version

  # Hostname (opsiyonel)
  if [[ "$(hostname -f 2>/dev/null)" != "$HOSTNAME_FQDN" ]]; then
    warn "Hostname $HOSTNAME_FQDN değil. Domain join öncesi ayarlayın:"
    echo "  hostnamectl set-hostname $COMPUTER_NAME"
    echo "  # /etc/hosts içine: $DC_IP veya transfer IP + $HOSTNAME_FQDN"
  fi

  # DNS kontrolü
  if getent hosts "$DC_HOST" >/dev/null 2>&1; then
    log "DC DNS çözümlendi: $DC_HOST"
  else
    warn "DC çözümlenemedi: $DC_HOST — /etc/resolv.conf veya hosts kontrol edin"
  fi

  log "Hazırlık tamamlandı"
}

# =============================================================================
# FAZ 1: Agent kurulumu (root)
# =============================================================================
phase_install() {
  need_root
  section "FAZ 1 — Agent kurulumu"

  local install_script="${REPO_ROOT}/ExcPanel.TransferAgent/deploy/linux/install.sh"
  [[ -f "$install_script" ]] || fail "install.sh bulunamadı: $install_script (REPO_ROOT ayarlayın)"

  echo "Kurulum başlıyor: $install_script"
  bash "$install_script"

  systemctl status excpanel-transfer-agent --no-pager || fail "Servis başlamadı"
  log "Agent kuruldu ve servis çalışıyor"
}

# =============================================================================
# FAZ 2: Temel doğrulama
# =============================================================================
phase_verify() {
  section "FAZ 2 — Temel doğrulama"

  # 1) Servis
  systemctl is-active --quiet excpanel-transfer-agent && log "Servis active" || fail "Servis çalışmıyor"

  # 2) Health
  local health
  health=$(curl_json GET "$API_BASE/api/agent/health")
  echo "$health" | pretty
  assert_success "$health"

  # 3) Privileged helper sudo
  section "Privileged helper sudo testi"
  local helper_out
  helper_out=$(sudo -u excpanel-agent sudo -n /opt/excpanel-transfer-agent/ExcPanel.TransferAgent.PrivilegedHelper <<< '{}' 2>&1 || true)
  echo "$helper_out" | head -3
  if echo "$helper_out" | grep -q 'NOT_ROOT\|not allowed\|password'; then
    fail "Privileged helper sudo yetkisi yok"
  fi
  log "Helper sudo OK"

  # 4) Storage status
  section "Storage status"
  curl_json GET "$API_BASE/api/storage/status" | pretty

  # 5) Setup status
  section "Setup status"
  curl_json GET "$API_BASE/api/setup/status" | pretty

  # 6) Prerequisites
  section "Setup prerequisites"
  local prereq
  prereq=$(curl_json GET "$API_BASE/api/setup/prerequisites")
  echo "$prereq" | pretty
  if command -v jq >/dev/null 2>&1; then
    local ready failed_count
    ready=$(echo "$prereq" | jq -r '.data.ready')
    failed_count=$(echo "$prereq" | jq '[.data.checks[] | select(.status=="Failed")] | length')
    if [[ "$ready" != "true" || "$failed_count" != "0" ]]; then
      warn "Prerequisites tam hazır değil (failed=$failed_count). Kuruluma devam etmeden eksikleri giderin."
      echo "$prereq" | jq '[.data.checks[] | select(.status=="Failed") | {name, message, remediation}]'
    else
      log "Prerequisites ready"
    fi
  fi

  log "Temel doğrulama tamamlandı"
}

# =============================================================================
# FAZ 3: Setup plan (dry-run — disk formatlamaz)
# =============================================================================
phase_wizard_plan() {
  section "FAZ 3 — Setup plan (dry-run)"

  local body
  body=$(cat <<EOF
{
  "storage": {
    "diskPath": "$DISK_PATH",
    "mountPath": "$STORAGE_ROOT",
    "fileSystem": "ext4"
  },
  "domain": {
    "dnsDomain": "$DNS_DOMAIN",
    "realm": "$REALM",
    "workgroup": "$WORKGROUP",
    "domainController": "$DC_HOST",
    "domainControllerIp": "$DC_IP",
    "joinUsername": "$DOMAIN_JOIN_USER",
    "computerName": "$COMPUTER_NAME",
    "computerOu": null
  },
  "samba": {
    "shareName": "$SHARE_NAME",
    "uncHost": "$HOSTNAME_FQDN",
    "requiredAdGroup": "$AD_GROUP"
  },
  "sftp": {
    "enabled": true,
    "port": 22
  }
}
EOF
)

  local plan
  plan=$(curl_json POST "$API_BASE/api/setup/plan" "$body")
  echo "$plan" | pretty

  if command -v jq >/dev/null 2>&1; then
    echo
    echo "--- Özet ---"
    echo "$plan" | jq '{
      canApply: .data.canApply,
      destructiveActions: .data.destructiveActions,
      requiredConfirmationCodes: .data.requiredConfirmationCodes,
      validationErrors: .data.validationErrors
    }'
    local fmt_code
    fmt_code=$(echo "$plan" | jq -r '.data.destructiveActions[]? | select(.code=="format-disk") | .confirmationCode // empty')
    if [[ -n "$fmt_code" ]]; then
      echo
      warn "Disk format onay kodu (apply için gerekli): $fmt_code"
      echo "export FORMAT_DISK_CODE='$fmt_code'"
    fi
  fi

  log "Plan testi tamamlandı (hiçbir değişiklik yapılmadı)"
}

# =============================================================================
# FAZ 4: Domain / Samba / SFTP durum kontrolleri
# =============================================================================
phase_services() {
  section "FAZ 4 — Domain / Samba / SFTP"

  echo "--- Domain status ---"
  curl_json GET "$API_BASE/api/domain/status" | pretty

  echo "--- Domain precheck ---"
  curl_json POST "$API_BASE/api/domain/precheck" "$(cat <<EOF
{
  "dnsDomain": "$DNS_DOMAIN",
  "realm": "$REALM",
  "workgroup": "$WORKGROUP",
  "domainController": "$DC_HOST",
  "domainControllerIp": "$DC_IP",
  "computerName": "$COMPUTER_NAME",
  "requiredAdGroup": "$AD_GROUP"
}
EOF
)" | pretty

  echo "--- Domain test ---"
  curl_json POST "$API_BASE/api/domain/test" | pretty

  echo "--- Samba status ---"
  curl_json GET "$API_BASE/api/samba/status" | pretty

  echo "--- SFTP status ---"
  curl_json GET "$API_BASE/api/sftp/status" | pretty

  log "Servis durum kontrolleri tamamlandı"
}

# =============================================================================
# FAZ 5: Samba config & ACL kalıcı düzeltme kontrolleri
# =============================================================================
phase_samba_acl_checks() {
  section "FAZ 5 — Samba config & ACL kontrolleri"

  echo "--- testparm: valid users formatı ---"
  if command -v testparm >/dev/null 2>&1; then
    sudo testparm -s 2>/dev/null | grep -i 'valid users' || warn "valid users satırı bulunamadı (Samba henüz init edilmemiş olabilir)"
    echo "Beklenen: valid users = +\"DOGRUMAIL-DEMO\\Exchange Trusted Subsystem\""
    sudo testparm -s 2>/dev/null | grep -F '[PSTTransfer$]' && log "Share section mevcut" || warn "[PSTTransfer$] henüz yok"
  else
    warn "testparm yok"
  fi

  echo "--- Samba write test endpoint ---"
  curl_json POST "$API_BASE/api/setup/test-samba-write" | pretty

  log "Samba/ACL kontrolleri tamamlandı"
}

# =============================================================================
# FAZ 6: Export prepare testi
# =============================================================================
phase_export() {
  section "FAZ 6 — Export prepare"

  local body
  body=$(cat <<EOF
{
  "jobId": "$TEST_JOB_ID",
  "mailbox": "$TEST_MAILBOX",
  "domain": "$TEST_DOMAIN",
  "estimatedMailboxSizeGb": 1
}
EOF
)

  local result
  result=$(curl_json POST "$API_BASE/api/export/prepare" "$body")
  echo "$result" | pretty
  assert_success "$result"

  if command -v jq >/dev/null 2>&1; then
    export EXCHANGE_FILE_PATH
    EXCHANGE_FILE_PATH=$(echo "$result" | jq -r '.data.exchangeFilePath')
    export JOB_DIR
    JOB_DIR=$(echo "$result" | jq -r '.data.physicalDirectory')
    echo
    log "EXCHANGE_FILE_PATH=$EXCHANGE_FILE_PATH"
    log "JOB_DIR=$JOB_DIR"

    if [[ -d "$JOB_DIR" ]]; then
      echo "--- Job klasörü ACL ---"
      getfacl "$JOB_DIR" | grep -i 'exchange trusted subsystem' || warn "ACL satırı bulunamadı"
      echo "Beklenen:"
      echo "  group:DOGRUMAIL-DEMO\\exchange trusted subsystem:rwx"
      echo "  default:group:DOGRUMAIL-DEMO\\exchange trusted subsystem:rwx"
    fi
  fi

  echo "--- UNC path endpoint ---"
  curl_json GET "$API_BASE/api/samba/unc-path/Export/$TEST_JOB_ID" | pretty

  log "Export prepare testi tamamlandı"
}

# =============================================================================
# FAZ 7: Import prepare placeholder
# =============================================================================
phase_import() {
  section "FAZ 7 — Import prepare (placeholder)"

  curl_json POST "$API_BASE/api/import/prepare" "$(cat <<EOF
{
  "jobId": "$TEST_JOB_ID",
  "mailbox": "$TEST_MAILBOX",
  "domain": "$TEST_DOMAIN"
}
EOF
)" | pretty

  log "Import placeholder testi tamamlandı"
}

# =============================================================================
# DİKKAT: Setup apply — diski formatlayabilir
# =============================================================================
phase_wizard_apply() {
  section "DİKKAT — Setup apply (destructive olabilir)"

  echo -e "${RED}Bu adım disk formatlama, domain join, Samba ve SFTP init yapabilir.${NC}"
  echo "Devam etmek için: CONFIRM_APPLY=yes bash $0 --phase wizard-apply"
  [[ "${CONFIRM_APPLY:-}" == "yes" ]] || { warn "İptal edildi (CONFIRM_APPLY=yes gerekli)"; return 0; }

  if [[ -z "${DOMAIN_JOIN_PASSWORD:-}" ]]; then
  read -rs -p "Domain join password ($DOMAIN_JOIN_USER): " DOMAIN_JOIN_PASSWORD
  echo
  fi

  # Plan'dan format kodu al
  local plan_body plan fmt_code
  plan_body=$(cat <<EOF
{"storage":{"diskPath":"$DISK_PATH","mountPath":"$STORAGE_ROOT","fileSystem":"ext4"},"domain":{"dnsDomain":"$DNS_DOMAIN","realm":"$REALM","workgroup":"$WORKGROUP","domainController":"$DC_HOST","domainControllerIp":"$DC_IP","joinUsername":"$DOMAIN_JOIN_USER","computerName":"$COMPUTER_NAME"},"samba":{"shareName":"$SHARE_NAME","uncHost":"$HOSTNAME_FQDN","requiredAdGroup":"$AD_GROUP"},"sftp":{"enabled":true,"port":22}}
EOF
)
  plan=$(curl_json POST "$API_BASE/api/setup/plan" "$plan_body")
  fmt_code=$(echo "$plan" | jq -r '.data.destructiveActions[]? | select(.code=="format-disk") | .confirmationCode // empty')
  [[ -n "${FORMAT_DISK_CODE:-}" ]] && fmt_code="$FORMAT_DISK_CODE"

  local apply_body
  apply_body=$(jq -n \
    --arg disk "$DISK_PATH" \
    --arg mount "$STORAGE_ROOT" \
    --arg dns "$DNS_DOMAIN" \
    --arg realm "$REALM" \
    --arg wg "$WORKGROUP" \
    --arg dc "$DC_HOST" \
    --arg dcip "$DC_IP" \
    --arg user "$DOMAIN_JOIN_USER" \
    --arg comp "$COMPUTER_NAME" \
    --arg share "$SHARE_NAME" \
    --arg unc "$HOSTNAME_FQDN" \
    --arg group "$AD_GROUP" \
    --arg fmt "$fmt_code" \
    --arg duser "$DOMAIN_JOIN_USER" \
    --arg dpass "$DOMAIN_JOIN_PASSWORD" \
    '{
      storage: {diskPath: $disk, mountPath: $mount, fileSystem: "ext4"},
      domain: {dnsDomain: $dns, realm: $realm, workgroup: $wg, domainController: $dc, domainControllerIp: $dcip, joinUsername: $user, computerName: $comp},
      samba: {shareName: $share, uncHost: $unc, requiredAdGroup: $group},
      sftp: {enabled: true, port: 22},
      confirmations: {formatDisk: $fmt, joinDomain: true, configureSamba: true, initializeSftp: true},
      domainCredentials: {username: $duser, password: $dpass}
    }')

  local result
  result=$(curl_json POST "$API_BASE/api/setup/apply" "$apply_body")
  echo "$result" | pretty

  unset DOMAIN_JOIN_PASSWORD

  echo "--- Setup status (son) ---"
  curl_json GET "$API_BASE/api/setup/status" | pretty

  log "Setup apply tamamlandı"
}

# =============================================================================
# Tüm güvenli fazlar (apply hariç)
# =============================================================================
phase_all_safe() {
  phase_verify
  phase_wizard_plan
  phase_services
  phase_samba_acl_checks
  phase_export
  phase_import
  section "BAŞARI KRİTERLERİ"
  cat <<'CRITERIA'

  [ ] systemctl: excpanel-transfer-agent active
  [ ] GET /api/agent/health → success: true
  [ ] GET /api/setup/prerequisites → ready: true, Failed yok
  [ ] POST /api/setup/plan → canApply veya storage zaten mount (skip)
  [ ] GET /api/domain/status → domainJoined (join sonrası)
  [ ] GET /api/samba/status → shareConfigured: true
  [ ] GET /api/sftp/status → initialized: true
  [ ] testparm → valid users = +"DOGRUMAIL-DEMO\Exchange Trusted Subsystem"
  [ ] POST /api/export/prepare → exchangeFilePath dolu
  [ ] getfacl job dir → group + default:group Exchange Trusted Subsystem rwx
  [ ] POST /api/import/prepare → readyForImport: false + warning

CRITERIA
}

usage() {
  cat <<EOF
Kullanım: $0 --phase <adım>

Adımlar:
  prep           Sunucu paketleri + .NET (root)
  install        Agent kurulumu install.sh (root)
  verify         Health, helper sudo, prerequisites
  wizard-plan    Setup plan dry-run (güvenli)
  services       Domain/Samba/SFTP status
  samba-acl      testparm + ACL + samba write test
  export         Export prepare + UNC + ACL kontrol
  import         Import placeholder
  wizard-apply   DİKKAT: destructive setup apply
  all-safe       verify + plan + services + samba-acl + export + import

Örnek yeni sunucu akışı:
  curl -fsSL https://raw.githubusercontent.com/poyraz0/ExcPanelAgentTest/main/ExcPanel.TransferAgent/deploy/linux/bootstrap.sh | sudo bash
  bash $0 --phase verify
  bash $0 --phase all-safe

Destructive apply (bilinçli):
  CONFIRM_APPLY=yes bash $0 --phase wizard-apply

Ortam değişkenleri: API_BASE, REPO_ROOT, DISK_PATH, DOMAIN_JOIN_PASSWORD, FORMAT_DISK_CODE
EOF
}

main() {
  local phase="${1:-}"
  case "${phase}" in
    --phase)
      phase="${2:-}"
      ;;
  esac

  case "$phase" in
    prep)          phase_prep ;;
    install)       phase_install ;;
    verify)        phase_verify ;;
    wizard-plan)   phase_wizard_plan ;;
    services)      phase_services ;;
    samba-acl)     phase_samba_acl_checks ;;
    export)        phase_export ;;
    import)        phase_import ;;
    wizard-apply)  phase_wizard_apply ;;
    all-safe)      phase_all_safe ;;
    -h|--help|help|"") usage ;;
    *) fail "Bilinmeyen phase: $phase ( --help ile listeleyin)" ;;
  esac
}

main "$@"
