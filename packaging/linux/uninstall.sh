#!/usr/bin/env bash
# Removes the desktop integration installed by install.sh (does not touch the binary).
set -euo pipefail

APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/512x512/apps"

rm -f "$APP_DIR/stellar-launcher.desktop" "$ICON_DIR/stellar-launcher.png"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APP_DIR" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache   >/dev/null 2>&1 && gtk-update-icon-cache -f -t "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor" >/dev/null 2>&1 || true

echo "Removed Stellar Launcher desktop entry and icon."
