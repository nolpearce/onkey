# Onkey

A little desktop pet that wanders around your screen going "oooo". Just for fun :)

![Onkey walking](Assets/Preview.gif)

## Mac

Needs the Xcode Command Line Tools (`xcode-select --install`).

```bash
./Mac/build.sh
```

This builds `Onkey.app`, installs it in `/Applications` and launches it. Click the Onkey head in the menu bar to:

- **Dance to Music**: every Onkey bops (squashes down 15%) on the beat of whatever's playing
- **How Many Onkeys**: one, two, three, five, or ten (chaos), each wandering on his own
- **Where Onkey Goes**: anywhere, along an edge, or stay put; how often he walks to your mouse; walking speed
- **Sound**: on/off, how often, volume, play now. His mouth opens while he makes the sound
- **Appearance**: size, opacity, in front of windows or on the desktop; eyes that watch your cursor; blinking
- **Check for Updates**: finds a newer release here on GitHub, then downloads it and restarts Onkey with your settings kept. He also checks on his own every few hours (you can turn that off)
- **Let Me Drag Onkey Around**, **Open Onkey at Login**, **Pause**, **Quit**

Or download `Onkey-Mac.zip` from [Releases](https://github.com/nolpearce/onkey/releases), unzip it and drag `Onkey.app` into Applications. It runs on Apple Silicon and Intel Macs with macOS 13 or later.
The first time, macOS will say it can't check the app: click **Done**, then go to **System Settings → Privacy & Security** and click **Open Anyway**.
More detail is in [Mac/README.txt](Mac/README.txt).

## Windows

Download `Onkey-Windows.zip` from [Releases](https://github.com/nolpearce/onkey/releases) and extract it, or clone this repo, then double-click `Start Onkey.vbs` (in `Windows\` in the repo). Then right-click Onkey's icon beside the clock. The menu has the same settings as the Mac version. See [Windows/README.txt](Windows/README.txt).

## Layout

```
Assets/      Onkey.png (the sprite both versions draw from), Sounds/, Preview.gif
Mac/         Sources/*.swift, build.sh (builds and installs Onkey.app), README.txt
Windows/     Source/*.cs, Start Onkey.vbs / .ps1 (compile and run at launch), README.txt
package.sh   builds both release downloads into dist/
```

The two versions mirror each other file for file where they can (for example
`Mac/Sources/BeatTracker.swift` and `Windows/Source/BeatTracker.cs`), so a change to one
usually wants the same change in the other.

## Releasing

Bump the version in `Mac/build.sh` (`CFBundleShortVersionString`, and `CFBundleVersion`),
`Windows/Source/Program.cs` (`Version`) and both README.txt files, run `./package.sh`, then
publish a GitHub release tagged `vX.Y.Z` with `Onkey-Mac.zip` and `Onkey-Windows.zip` attached
under exactly those names. Running Onkeys compare their version with the latest release's tag
and offer the update from the menu.
