#!/usr/bin/env bash
set -euo pipefail
APP_DIR="/opt/canopus-jd1"
BIN_NAME="canopusMapApp"
DESKTOP_FILE="/usr/share/applications/canopus-jd1.desktop"
ICON_SRC="$PWD/trayIcon.ico"
ICON_DST="/usr/share/pixmaps/canopus-jd1.ico"
sudo install -d -m 0755 "$APP_DIR"
sudo install -m 0755 "$PWD/$BIN_NAME" "$APP_DIR/$BIN_NAME"
# optional: include pdb for symbols (omit if not needed)
# sudo install -m 0644 "$PWD/$BIN_NAME.pdb" "$APP_DIR/$BIN_NAME.pdb"
# Install desktop file
TMP_DESKTOP=$(mktemp)
cat > "$TMP_DESKTOP" <<EOF
[Desktop Entry]
Name=Canopus JD-1 Mapper
Exec=$APP_DIR/$BIN_NAME
Terminal=false
Type=Application
Categories=Utility;AudioVideo;
Icon=canopus-jd1
X-GNOME-UsesNotifications=true
EOF
sudo install -m 0644 "$TMP_DESKTOP" "$DESKTOP_FILE"
rm "$TMP_DESKTOP"
# Install icon (ico works for most DEs; could convert to png if desired)
if [ -f "$ICON_SRC" ]; then
  sudo install -m 0644 "$ICON_SRC" "$ICON_DST"
fi
# Update desktop database / icon cache (best-effort)
if command -v update-desktop-database >/dev/null; then sudo update-desktop-database || true; fi
if command -v gtk-update-icon-cache >/dev/null; then sudo gtk-update-icon-cache -f /usr/share/icons/hicolor || true; fi
echo "Installed to $APP_DIR. Launcher: Canopus JD-1 Mapper. Uninstall with: sudo rm -rf $APP_DIR $DESKTOP_FILE $ICON_DST"
