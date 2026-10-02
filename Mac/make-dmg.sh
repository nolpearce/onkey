#!/bin/zsh
# Wraps an Onkey.app in Onkey-Mac.dmg: a jungle window with Onkey on the left and the
# Applications folder on the right, so installing is one drag.
#   Mac/make-dmg.sh path/to/Onkey.app path/to/Onkey-Mac.dmg
# Laying out the window uses Finder, so the first time macOS asks whether Terminal may
# control Finder: click OK. Needs a Mac with the Xcode Command Line Tools.
set -euo pipefail
here="${0:A:h}"
art="${here:h}/Assets/Installer"
app="${1:A}"
out="${2:A}"
volume="Onkey"
work="$(mktemp -d)"
device=""
cleanup() {
    [[ -n "$device" ]] && hdiutil detach "$device" -force >/dev/null 2>&1 || true
    rm -rf "$work"
}
trap cleanup EXIT

# An Onkey volume that's already mounted (an older download, say) would confuse Finder.
if [[ -d "/Volumes/$volume" ]]; then
    hdiutil detach "/Volumes/$volume" -force >/dev/null || {
        echo "Eject the Onkey disk in Finder first." >&2; exit 1; }
fi

stage="$work/stage"
mkdir -p "$stage/.background"
ditto "$app" "$stage/Onkey.app"
ln -s /Applications "$stage/Applications"
# One file holding both sizes, so the window stays sharp on Retina screens.
tiffutil -cathidpicheck "$art/dmg-background.png" "$art/dmg-background@2x.png" -out "$stage/.background/background.tiff" >/dev/null
cp "$app/Contents/Resources/AppIcon.icns" "$stage/.VolumeIcon.icns"

echo "Making the disk image..."
# Room to spare for the .DS_Store Finder writes.
size=$(( $(du -sm "$stage" | cut -f1) + 20 ))
hdiutil create -quiet -srcfolder "$stage" -volname "$volume" -fs HFS+ -format UDRW -size "${size}m" -ov "$work/rw.dmg"
device=$(hdiutil attach -readwrite -noverify -noautoopen "$work/rw.dmg" | awk '/Apple_HFS/ {print $1}')
mount="/Volumes/$volume"
# Show the Onkey head as the disk's icon.
SetFile -a C "$mount" 2>/dev/null || true

echo "Laying out the window..."
# The window is the size of the background (640 x 420), and the icon spots match the
# arrow drawn in Assets/Installer/make-art.py.
osascript <<APPLESCRIPT
tell application "Finder"
    tell disk "$volume"
        open
        delay 1
        set current view of container window to icon view
        set toolbar visible of container window to false
        set statusbar visible of container window to false
        set the bounds of container window to {200, 120, 840, 540}
        set opts to the icon view options of container window
        set arrangement of opts to not arranged
        set icon size of opts to 112
        set text size of opts to 13
        set background picture of opts to file ".background:background.tiff"
        set position of item "Onkey.app" of container window to {170, 222}
        set position of item "Applications" of container window to {470, 222}
        update without registering applications
        delay 1
        close
    end tell
end tell
APPLESCRIPT

# Give Finder a moment to write its .DS_Store, then lock everything in.
sync
sleep 2
rm -rf "$mount/.fseventsd" 2>/dev/null || true
hdiutil detach "$device" -quiet
device=""
rm -f "$out"
hdiutil convert "$work/rw.dmg" -quiet -format UDZO -imagekey zlib-level=9 -o "$out"
echo "Built $out"
