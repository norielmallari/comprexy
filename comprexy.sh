#!/usr/bin/env bash
# Local DX entrypoint for Comprexy (proxy + control-api).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

PROXY_PROJECT="apps/proxy/Comprexy.Api.csproj"
CONTROL_PROJECT="apps/control-api/Comprexy.ControlApi.csproj"
DOTNET_CHANNEL="10.0"
DOTNET_INSTALL_DIR="${DOTNET_ROOT:-$HOME/.dotnet}"
DOTNET_INSTALL_SCRIPT_URL="https://dot.net/v1/dotnet-install.sh"

usage() {
  cat <<'EOF'
Usage: ./comprexy.sh <command> [args...]

Commands:
  proxy [args...]       Run data-plane proxy (:8129)
  control-api [args...] Run control-api metrics host (:8130)
  control [args...]     Alias for control-api
  dev                   Run proxy + control-api together (Ctrl-C stops both)
  test [args...]        Run solution tests
  build [args...]       Build the solution
  clear-db              Rebuild SQLite from migrations (proxy --clear-db)
  install-dotnet        Install .NET 10 SDK into ~/.dotnet (official script)
  help                  Show this help

If .NET 10 is missing, run/build commands offer to install it (interactive),
or set COMPREXY_AUTO_INSTALL_DOTNET=1 to install without prompting.

Examples:
  ./comprexy.sh proxy
  ./comprexy.sh control-api
  ./comprexy.sh dev
  ./comprexy.sh test
  ./comprexy.sh clear-db
  ./comprexy.sh install-dotnet
EOF
}

prefer_local_dotnet() {
  # Prefer a user-local install from dotnet-install.sh when present.
  if [[ -x "$DOTNET_INSTALL_DIR/dotnet" ]]; then
    export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
    export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:${PATH:-}"
  fi
}

dotnet_has_sdk_10() {
  command -v dotnet >/dev/null 2>&1 || return 1
  local sdks
  sdks="$(dotnet --list-sdks 2>/dev/null || true)"
  [[ -n "$sdks" ]] || return 1
  awk '{ split($1, v, "."); if (v[1] + 0 >= 10) found=1 } END { exit found ? 0 : 1 }' <<<"$sdks"
}

install_dotnet_sdk() {
  local os
  os="$(uname -s)"
  case "$os" in
    Darwin|Linux) ;;
    *)
      cat >&2 <<EOF
error: automatic .NET install is only supported on macOS/Linux (got: $os).

Install manually: https://dotnet.microsoft.com/download/dotnet/10.0
EOF
      return 1
      ;;
  esac

  if ! command -v curl >/dev/null 2>&1; then
    echo "error: curl is required to download the .NET install script." >&2
    return 1
  fi

  echo "Installing .NET SDK ${DOTNET_CHANNEL} into ${DOTNET_INSTALL_DIR}…"
  echo "(official script: ${DOTNET_INSTALL_SCRIPT_URL})"
  echo

  local tmp
  tmp="$(mktemp -t comprexy-dotnet-install.XXXXXX)"
  # shellcheck disable=SC2064
  trap "rm -f '$tmp'" RETURN

  curl -fsSL "$DOTNET_INSTALL_SCRIPT_URL" -o "$tmp"
  bash "$tmp" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR"

  prefer_local_dotnet

  if ! dotnet_has_sdk_10; then
    echo "error: .NET SDK ${DOTNET_CHANNEL} install finished but SDK 10+ was not detected." >&2
    return 1
  fi

  cat <<EOF

.NET SDK ready: $(dotnet --version)

Add to your shell profile for new terminals:
  export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
  export PATH="\$DOTNET_ROOT:\$DOTNET_ROOT/tools:\$PATH"
EOF
}

offer_dotnet_install() {
  local reason="$1"

  cat >&2 <<EOF
$reason

EOF

  if [[ "${COMPREXY_AUTO_INSTALL_DOTNET:-}" == "1" ]]; then
    echo "COMPREXY_AUTO_INSTALL_DOTNET=1 → installing .NET ${DOTNET_CHANNEL}…" >&2
    install_dotnet_sdk
    return
  fi

  if [[ ! -t 0 ]]; then
    cat >&2 <<EOF
Non-interactive shell: install with:
  ./comprexy.sh install-dotnet
or:
  COMPREXY_AUTO_INSTALL_DOTNET=1 ./comprexy.sh <command>

Manual download: https://dotnet.microsoft.com/download/dotnet/10.0
EOF
    exit 1
  fi

  local answer
  read -r -p "Install .NET ${DOTNET_CHANNEL} SDK into ${DOTNET_INSTALL_DIR} now? [y/N] " answer
  case "$answer" in
    y|Y|yes|YES)
      install_dotnet_sdk
      ;;
    *)
      cat >&2 <<EOF
Aborted. Install later with:
  ./comprexy.sh install-dotnet
or: https://dotnet.microsoft.com/download/dotnet/10.0
EOF
      exit 1
      ;;
  esac
}

require_dotnet() {
  prefer_local_dotnet

  if ! command -v dotnet >/dev/null 2>&1; then
    offer_dotnet_install "error: .NET SDK not found on PATH (need .NET ${DOTNET_CHANNEL}+)."
    prefer_local_dotnet
  fi

  local sdks
  sdks="$(dotnet --list-sdks 2>/dev/null || true)"
  if [[ -z "$sdks" ]]; then
    offer_dotnet_install "error: \`dotnet\` is on PATH ($(command -v dotnet)) but no SDKs are installed."
    prefer_local_dotnet
    sdks="$(dotnet --list-sdks 2>/dev/null || true)"
  fi

  if ! awk '{ split($1, v, "."); if (v[1] + 0 >= 10) found=1 } END { exit found ? 0 : 1 }' <<<"$sdks"; then
    offer_dotnet_install "error: .NET ${DOTNET_CHANNEL}+ SDK is required (found only):
$sdks"
    prefer_local_dotnet
  fi

  if ! dotnet_has_sdk_10; then
    echo "error: .NET ${DOTNET_CHANNEL}+ SDK still not available after install attempt." >&2
    exit 1
  fi
}

run_proxy() {
  require_dotnet
  exec dotnet run --project "$PROXY_PROJECT" -- "$@"
}

run_control() {
  require_dotnet
  exec dotnet run --project "$CONTROL_PROJECT" -- "$@"
}

run_dev() {
  require_dotnet
  local proxy_pid control_pid

  cleanup() {
    local code=$?
    trap - EXIT INT TERM
    if [[ -n "${control_pid:-}" ]] && kill -0 "$control_pid" 2>/dev/null; then
      kill "$control_pid" 2>/dev/null || true
      wait "$control_pid" 2>/dev/null || true
    fi
    if [[ -n "${proxy_pid:-}" ]] && kill -0 "$proxy_pid" 2>/dev/null; then
      kill "$proxy_pid" 2>/dev/null || true
      wait "$proxy_pid" 2>/dev/null || true
    fi
    exit "$code"
  }

  trap cleanup EXIT INT TERM

  echo "Starting proxy (:8129) and control-api (:8130)…"
  echo "Press Ctrl-C to stop both."
  echo

  dotnet run --project "$PROXY_PROJECT" &
  proxy_pid=$!
  dotnet run --project "$CONTROL_PROJECT" &
  control_pid=$!

  wait "$proxy_pid" "$control_pid"
}

run_test() {
  require_dotnet
  exec dotnet test "$ROOT/Comprexy.slnx" "$@"
}

run_build() {
  require_dotnet
  exec dotnet build "$ROOT/Comprexy.slnx" "$@"
}

run_clear_db() {
  require_dotnet
  echo "Rebuilding database (data/comprexy.db) from migrations…"
  exec dotnet run --project "$PROXY_PROJECT" -- --clear-db
}

cmd="${1:-help}"
if [[ $# -gt 0 ]]; then
  shift
fi

case "$cmd" in
  proxy)
    run_proxy "$@"
    ;;
  control-api|control)
    run_control "$@"
    ;;
  dev)
    run_dev
    ;;
  test)
    run_test "$@"
    ;;
  build)
    run_build "$@"
    ;;
  clear-db)
    run_clear_db
    ;;
  install-dotnet)
    install_dotnet_sdk
    ;;
  help|-h|--help)
    usage
    ;;
  *)
    echo "error: unknown command '$cmd'" >&2
    echo >&2
    usage >&2
    exit 1
    ;;
esac
