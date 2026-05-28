#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REF_DIR="$ROOT_DIR/CLIProxyAPI-reference"
SERVER_FILE="$REF_DIR/internal/api/server.go"
AUTH_FILE="$REF_DIR/internal/api/handlers/management/auth_files.go"
USAGE_FILE="$REF_DIR/internal/api/handlers/management/usage.go"

if [[ ! -d "$REF_DIR" ]]; then
  echo "[FAIL] 未找到本地参考仓库：$REF_DIR"
  exit 1
fi

if [[ ! -f "$SERVER_FILE" || ! -f "$AUTH_FILE" ]]; then
  echo "[FAIL] 参考仓库缺少关键文件，请确认 CLIProxyAPI-reference 完整"
  exit 1
fi

failures=()
pass_count=0

check_contains() {
  local file="$1"
  local pattern="$2"
  local label="$3"

  if grep -Fq "$pattern" "$file"; then
    echo "[PASS] $label"
    pass_count=$((pass_count + 1))
  else
    echo "[FAIL] $label"
    failures+=("$label")
  fi
}

check_contains "$SERVER_FILE" 'mgmt.GET("/auth-files", s.mgmt.ListAuthFiles)' '管理路由 GET /auth-files 存在'
check_contains "$SERVER_FILE" 'mgmt.POST("/api-call", s.mgmt.APICall)' '管理路由 POST /api-call 存在'
check_contains "$SERVER_FILE" 'mgmt.PATCH("/auth-files/status", s.mgmt.PatchAuthFileStatus)' '管理路由 PATCH /auth-files/status 存在'
check_contains "$SERVER_FILE" 'mgmt.PATCH("/auth-files/fields", s.mgmt.PatchAuthFileFields)' '管理路由 PATCH /auth-files/fields 存在'
check_contains "$SERVER_FILE" 'mgmt.DELETE("/auth-files", s.mgmt.DeleteAuthFile)' '管理路由 DELETE /auth-files 存在'
check_contains "$SERVER_FILE" 'mgmt.GET("/usage-queue", s.mgmt.GetUsageQueue)' '管理路由 GET /usage-queue 存在'

if grep -Fq '"/usage"' "$SERVER_FILE" || grep -Fq 'GetUsage(' "$SERVER_FILE" || grep -Fq 'GetUsage(' "$AUTH_FILE" || ([[ -f "$USAGE_FILE" ]] && grep -Fq 'GetUsage(' "$USAGE_FILE"); then
  echo "[INFO] 参考仓库仍可定位遗留 GET /usage 接口"
else
  echo "[INFO] 参考仓库未发现 GET /usage；当前主流程依赖 usage-queue，不视为契约失败"
fi

check_contains "$AUTH_FILE" '"auth_index"' 'auth-files 返回字段 auth_index 仍存在'
check_contains "$AUTH_FILE" '"name"' 'auth-files 返回字段 name 仍存在'
check_contains "$AUTH_FILE" '"disabled"' 'auth-files 返回字段 disabled 仍存在'
check_contains "$AUTH_FILE" '"priority"' 'auth-files 返回字段 priority 仍存在'
check_contains "$AUTH_FILE" 'syncAuthFilePriorityAttribute' 'priority 字段仍会同步到运行态属性'
check_contains "$AUTH_FILE" 'PatchAuthFileFields' 'auth_files handler 仍包含字段更新入口'
check_contains "$AUTH_FILE" 'PatchAuthFileStatus' 'auth_files handler 仍包含状态更新入口'
check_contains "$USAGE_FILE" 'func (h *Handler) GetUsageQueue' 'usage-queue handler 仍存在'

if [[ ${#failures[@]} -gt 0 ]]; then
  echo
  echo "契约检查失败，共 ${#failures[@]} 项："
  for item in "${failures[@]}"; do
    echo "- $item"
  done
  exit 1
fi

echo

echo "契约检查通过，共 $pass_count 项。"
