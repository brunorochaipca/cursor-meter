#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Uso: ./install.sh [--autostart] [--no-start]

Instala o CursorMeter só para este utilizador, sem sudo.
  --autostart  inicia o CursorMeter com a sessão
  --no-start   não abre o CursorMeter no fim
EOF
}

autostart=false
start=true
for arg in "$@"; do
  case "$arg" in
    --autostart) autostart=true ;;
    --no-start) start=false ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; exit 2 ;;
  esac
done

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
config="${XDG_CONFIG_HOME:-$HOME/.config}"
app_dir="$data/cursormeter"
exe="$app_dir/CursorMeter"
desktop="$data/applications/cursormeter.desktop"
icon="$data/icons/hicolor/scalable/apps/cursormeter.svg"
autostart_entry="$config/autostart/cursormeter.desktop"

# Desktop Entry spec: quote the argument, escape " ` $ \ for the quoting and every backslash again for the string value.
exec_value() {
  local path="$1" quoted="" c i
  for ((i = 0; i < ${#path}; i++)); do
    c="${path:i:1}"
    # shellcheck disable=SC1003
    case "$c" in
      '"'|'`'|'$') quoted+='\\'"$c" ;;
      '\') quoted+='\\\\' ;;
      '%') quoted+='%%' ;;
      *) quoted+="$c" ;;
    esac
  done
  printf '"%s"' "$quoted"
}

write_entry() {
  local target="$1" extra="${2:-}" template
  template="$(<"$here/cursormeter.desktop")"
  mkdir -p "$(dirname "$target")"
  printf '%s\n%s' "${template//@EXEC@/"$(exec_value "$exe")"}" "$extra" > "$target.tmp"
  mv -f "$target.tmp" "$target"
}

stop_running() {
  pkill -u "$(id -u)" -x CursorMeter 2>/dev/null || return 0
  for _ in {1..50}; do
    pgrep -u "$(id -u)" -x CursorMeter >/dev/null || return 0
    sleep 0.1
  done
}

stop_running

mkdir -p "$app_dir"
install -m 755 "$here/CursorMeter" "$exe.tmp"
mv -f "$exe.tmp" "$exe"
install -m 755 "$here/uninstall.sh" "$app_dir/uninstall.sh"
install -D -m 644 "$here/cursormeter.svg" "$icon"
write_entry "$desktop"

if $autostart; then
  write_entry "$autostart_entry" $'X-GNOME-Autostart-enabled=true\n'
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t "$data/icons/hicolor" 2>/dev/null || true
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q "$data/applications" 2>/dev/null || true
fi

echo "CursorMeter instalado em $exe"
if $autostart; then
  echo "Inicia com a sessão: $autostart_entry"
fi
echo "Para desinstalar: $app_dir/uninstall.sh"

if $start && [[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
  setsid "$exe" >/dev/null 2>&1 < /dev/null &
fi
