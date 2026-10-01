#!/bin/zsh
# Builds both release downloads into dist/:
#   Onkey-Mac.zip      Onkey.app (Apple silicon + Intel)
#   Onkey-Windows.zip  the Windows launcher, its source and the assets it needs
# Needs a Mac with the Xcode Command Line Tools.
set -euo pipefail
root="${0:A:h}"
dist="$root/dist"
rm -rf "$dist"
mkdir -p "$dist"

"$root/Mac/build.sh" --no-install
ditto -c -k --norsrc --keepParent "$root/Mac/Onkey.app" "$dist/Onkey-Mac.zip"
rm -rf "$root/Mac/Onkey.app"

windows="$dist/Onkey-Windows"
mkdir -p "$windows/Source" "$windows/Assets/Sounds"
cp "$root/Windows/Start Onkey.vbs" "$root/Windows/Start Onkey.ps1" "$root/Windows/README.txt" "$windows/"
cp "$root"/Windows/Source/*.cs "$windows/Source/"
cp "$root/Assets/Onkey.png" "$windows/Assets/"
cp "$root/Assets/Sounds/oooo.wav" "$root/Assets/Sounds/README.txt" "$windows/Assets/Sounds/"
(cd "$dist" && zip -qrX Onkey-Windows.zip Onkey-Windows)
rm -rf "$windows"

ls -lh "$dist"
