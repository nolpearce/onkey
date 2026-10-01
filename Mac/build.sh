#!/bin/zsh
# Builds Onkey.app and installs it in /Applications (quitting any running Onkey first).
# Requires Xcode Command Line Tools (install with: xcode-select --install).
# Pass --no-install to only build Mac/Onkey.app.
set -euo pipefail
here="${0:A:h}"
root="${here:h}"
assets="$root/Assets"
app="$here/Onkey.app"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

echo "Compiling for Apple silicon and Intel..."
for arch in arm64 x86_64; do
    swiftc -O -target "$arch-apple-macos13.0" -o "$work/Onkey-$arch" "$here"/Sources/*.swift -framework AppKit -framework AVFoundation -framework CoreAudio
done
lipo -create "$work/Onkey-arm64" "$work/Onkey-x86_64" -output "$app/Contents/MacOS/Onkey"
mkdir -p "$app/Contents/Resources/Sounds"
cp "$assets/Onkey.png" "$app/Contents/Resources/"
cp "$assets/Sounds/oooo.wav" "$app/Contents/Resources/Sounds/"

echo "Making icon..."
iconset="$work/AppIcon.iconset"
mkdir -p "$iconset"
sips -p 2000 2000 "$assets/Onkey.png" --out "$work/square.png" >/dev/null
for size in 16 32 128 256 512; do
    sips -z $size $size "$work/square.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    sips -z $((size * 2)) $((size * 2)) "$work/square.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"

cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Onkey</string>
    <key>CFBundleDisplayName</key><string>Onkey</string>
    <key>CFBundleIdentifier</key><string>local.onkey.desktoppet</string>
    <key>CFBundleExecutable</key><string>Onkey</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleVersion</key><string>14</string>
    <key>CFBundleShortVersionString</key><string>4.4.3</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>LSUIElement</key><true/>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSAudioCaptureUsageDescription</key><string>Onkey listens to the music you're playing so he can bop to the beat. Nothing is recorded or sent anywhere.</string>
    <key>NSMicrophoneUsageDescription</key><string>Onkey listens for music so he can bop to the beat. Nothing is recorded or sent anywhere.</string>
</dict>
</plist>
PLIST

codesign --force --sign - "$app" >/dev/null
echo "Built $app"

if [[ "${1:-}" != "--no-install" ]]; then
    pkill -x Onkey 2>/dev/null && sleep 1 || true
    rm -rf /Applications/Onkey.app
    cp -R "$app" /Applications/
    rm -rf "$app"
    echo "Installed /Applications/Onkey.app"
    open /Applications/Onkey.app
fi
