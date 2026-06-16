#!/usr/bin/env bash
# Per-user desktop integration for Stellar Launcher (no root needed).
#
# Linux ELF binaries can't embed an icon the way a Windows .exe does, so a bare
# StellarLauncher.App shows a generic icon in the file manager. This installs a
# .desktop entry + the app icon into your user icon theme, so the launcher shows
# up in your application menu / dock with the proper sparkle icon.
#
# Run it once from the extracted release folder:  ./install.sh
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN="$HERE/StellarLauncher.App"

if [ ! -f "$BIN" ]; then
  echo "error: StellarLauncher.App not found next to this script ($HERE)" >&2
  exit 1
fi
chmod +x "$BIN"

ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/512x512/apps"
APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$ICON_DIR" "$APP_DIR"

install -m644 "$HERE/stellar-launcher.png" "$ICON_DIR/stellar-launcher.png"

# Substitute the absolute binary path into the Exec line.
sed "s|__EXEC__|$BIN|g" "$HERE/stellar-launcher.desktop" > "$APP_DIR/stellar-launcher.desktop"
chmod 644 "$APP_DIR/stellar-launcher.desktop"

# Refresh caches when the tools are present (harmless if missing).
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APP_DIR" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache   >/dev/null 2>&1 && gtk-update-icon-cache -f -t "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor" >/dev/null 2>&1 || true

echo "Installed. 'Stellar Launcher' should now appear in your application menu with its icon."
echo "Launch it from the menu, or run: $BIN"
