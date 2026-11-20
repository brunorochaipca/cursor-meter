#!/usr/bin/env bash
set -euo pipefail

data="${XDG_DATA_HOME:-$HOME/.local/share}"
config="${XDG_CONFIG_HOME:-$HOME/.config}"
ext_uuid="cursormeter@cursor-meter"

pkill -u "$(id -u)" -x CursorMeter 2>/dev/null || true

# Take the extension out of the list GNOME Shell enables at login, then remove it.
if command -v gnome-extensions >/dev/null 2>&1; then
  gnome-extensions disable "$ext_uuid" 2>/dev/null || true
fi
if command -v gsettings >/dev/null 2>&1; then
  enabled="$(gsettings get org.gnome.shell enabled-extensions 2>/dev/null || true)"
  if [[ "$enabled" == *"'$ext_uuid'"* ]]; then
    remaining="$(printf '%s' "$enabled" | sed -e "s/, '$ext_uuid'//" -e "s/'$ext_uuid', //" -e "s/'$ext_uuid'//")"
    if [[ "$remaining" == "[]" ]]; then
      # Nothing else was enabled by the user: go back to the session's own list.
      gsettings reset org.gnome.shell enabled-extensions 2>/dev/null || true
    else
      gsettings set org.gnome.shell enabled-extensions "$remaining" 2>/dev/null || true
    fi
  fi
fi
rm -rf "$data/gnome-shell/extensions/$ext_uuid"
rm -rf "${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/cursormeter"

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
