# Onkey

A little desktop pet that wanders around your screen going "oooo". Just for fun :)

The Onkey isn't my idea: he comes from the Instagram creator **Nobey One**. I just thought
it would be fun to have him wandering around my desktop, so I made this.

![Onkey walking](Assets/Preview.gif)

## Mac

Needs the Xcode Command Line Tools (`xcode-select --install`).

```bash
./Mac/build.sh
```

This builds `Onkey.app`, installs it in `/Applications` and launches it. Click the Onkey head in the menu bar for the quick actions (**Pause**, **Dance to Music**, **Let Me Drag Onkey Around**, **Quit**) and **Settings…**, a hand-drawn jungle window with:

- **Behaviour**: how many Onkeys, where he roams, how often he walks to your mouse, walking speed, dancing to music, dragging
- **Sound**: on/off, how often, volume, play now. His mouth opens while he makes the sound
- **Look**: size, see-through, in front of windows or on the desktop, staying over full-screen apps, eyes that watch your cursor, blinking
- **Updates**: finds a newer release here on GitHub, then downloads it and restarts Onkey with your settings kept. He also checks on his own every few hours (you can turn that off). Open at login

Or download `Onkey-Mac.zip` from [Releases](https://github.com/nolpearce/onkey/releases), unzip it and drag `Onkey.app` into Applications. It runs on Apple Silicon and Intel Macs with macOS 13 or later.
The first time, macOS will say it can't check the app: click **Done**, then go to **System Settings → Privacy & Security** and click **Open Anyway**.
More detail is in [Mac/README.txt](Mac/README.txt).

## Windows

Download `Onkey-Windows.zip` from [Releases](https://github.com/nolpearce/onkey/releases) and extract it, or clone this repo, then double-click `Start Onkey.vbs` (in `Windows\` in the repo). Then right-click Onkey's icon beside the clock and choose **Settings…** for the same window as the Mac version. See [Windows/README.txt](Windows/README.txt).

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

Versions always have three parts, `MAJOR.MINOR.PATCH`: write `4.5.0`, never `4.5`, in the
files below, the tag (`v4.5.0`) and the release title (`Onkey 4.5.0: ...`).

Bump the version in `Mac/build.sh` (`CFBundleShortVersionString`, and `CFBundleVersion`),
`Windows/Source/Program.cs` (`Version`) and both README.txt files, run `./package.sh`, then
publish a GitHub release tagged `vX.Y.Z` with `Onkey-Mac.zip` and `Onkey-Windows.zip` attached
under exactly those names. Running Onkeys compare their version with the latest release's tag
and offer the update from the menu.
