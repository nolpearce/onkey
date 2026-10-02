#!/bin/zsh
# Builds the Mac release download, dist/Onkey-Mac.zip (Onkey.app for Apple silicon + Intel).
# Onkey-Windows.zip is built on Windows by .github/workflows/windows-release.yml and
# attached to the release when it's published.
# Needs a Mac with the Xcode Command Line Tools.
set -euo pipefail
root="${0:A:h}"
dist="$root/dist"
# Versions are always three parts (4.4.1, 4.5.0), never two (4.4).
mac=$(sed -n 's|.*<key>CFBundleShortVersionString</key><string>\(.*\)</string>.*|\1|p' "$root/Mac/build.sh")
win=$(sed -n 's|.*Version = "\(.*\)";.*|\1|p' "$root/Windows/Source/Program.cs")
if ! print -r -- "$mac" | grep -Eqx '[0-9]+\.[0-9]+\.[0-9]+' || [[ "$mac" != "$win" ]]; then
    echo "Version must be MAJOR.MINOR.PATCH and match: Mac/build.sh has '$mac', Program.cs has '$win'." >&2
    exit 1
fi
echo "Packaging Onkey $mac (tag the release v$mac)"
rm -rf "$dist"
mkdir -p "$dist"

"$root/Mac/build.sh" --no-install
ditto -c -k --norsrc --keepParent "$root/Mac/Onkey.app" "$dist/Onkey-Mac.zip"
rm -rf "$root/Mac/Onkey.app"

ls -lh "$dist"
