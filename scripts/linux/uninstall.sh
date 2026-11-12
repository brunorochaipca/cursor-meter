#!/usr/bin/env bash
set -euo pipefail

data="${XDG_DATA_HOME:-$HOME/.local/share}"
config="${XDG_CONFIG_HOME:-$HOME/.config}"

pkill -u "$(id -u)" -x CursorMeter 2>/dev/null || true

rm -f "$config/autostart/cursormeter.desktop"
rm -f "$data/applications/cursormeter.desktop"
rm -f "$data/icons/hicolor/scalable/apps/cursormeter.svg"
rm -rf "$data/cursormeter"

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t "$data/icons/hicolor" 2>/dev/null || true
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q "$data/applications" 2>/dev/null || true
fi

echo "CursorMeter desinstalado."
