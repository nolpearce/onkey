# Onkey

A little desktop pet that wanders around your screen going "oooo". Just for fun :)

The Onkey isn't my idea: he comes from the Instagram creator **Nobey One**. I just thought
it would be fun to have him wandering around my desktop, so I made this.

![Onkey walking](Assets/Preview.gif)

## Mac

Download `Onkey-Mac.dmg` from [Releases](https://github.com/nolpearce/onkey/releases), open it and drag Onkey into the Applications folder in the window that appears. It runs on Apple Silicon and Intel Macs with macOS 13 or later.
The first time you open him, macOS will say it can't check the app: click **Done**, then go to **System Settings → Privacy & Security** and click **Open Anyway**.

To build it yourself instead, install the Xcode Command Line Tools (`xcode-select --install`) and run:

```bash
./Mac/build.sh
```

This builds `Onkey.app`, installs it in `/Applications` and launches it. Click the Onkey head in the menu bar and a hand-drawn jungle panel drops down, with **Pause**, **Dance** and **Drag him** switches along the top and four tabs:

- **Moves**: how many Onkeys, where he roams, how often he walks to your mouse, walking speed
- **Sound**: on/off, how often, volume, play now. His mouth opens while he makes the sound
- **Look**: size, see-through, in front of windows or on the desktop, staying over full-screen apps, eyes that watch your cursor, blinking
- **Updates**: finds a newer release here on GitHub, then downloads it and restarts Onkey with your settings kept. He also checks on his own every few hours (you can turn that off). Open at login

Dancing bops every Onkey (squashes him down 15%) on the beat of whatever's playing. Right-click the head for a short menu.

If Onkey crashes, **Send crash report** at the bottom of the panel opens a new issue here with his version, what went wrong and the end of his log, for you to check before sending. He keeps that log in `~/Library/Logs/Onkey` on a Mac and `%APPDATA%\Onkey` on Windows.

More detail is in [Mac/README.txt](Mac/README.txt).

## Windows

Download `Onkey-Windows-Setup.exe` from [Releases](https://github.com/nolpearce/onkey/releases) and open it. Windows may say it protected your PC because Onkey isn't from a known publisher: click **More info**, then **Run anyway**. The installer needs no administrator password; it adds Onkey to the Start menu (and the desktop, if you like), and you can remove him again from **Settings → Apps**. Then click Onkey's icon beside the clock for the same settings panel as the Mac version. To run it from this repo instead, run `Windows\build.cmd` and start `Windows\Onkey.exe`. See [Windows/README.txt](Windows/README.txt).

## Layout

```
Assets/      Onkey.png (the sprite both versions draw from), AppIcon.png (the Mac app icon), Sounds/, Preview.gif
Assets/Installer/  the jungle pictures for the Mac icon, DMG window and Windows installer (make-art.py)
Mac/         Sources/*.swift, build.sh (builds and installs Onkey.app), make-dmg.sh, README.txt
Windows/     Source/*.cs, build.cmd (builds Onkey.exe), installer.iss, Onkey.ico, README.txt
package.sh   builds the Mac release downloads into dist/
.github/     windows-release.yml builds and attaches the Windows downloads
```

The two versions mirror each other file for file where they can (for example
`Mac/Sources/BeatTracker.swift` and `Windows/Source/BeatTracker.cs`), so a change to one
usually wants the same change in the other.

## Releasing

Versions always have three parts, `MAJOR.MINOR.PATCH`: write `4.5.0`, never `4.5`, in the
files below, the tag (`v4.5.0`) and the release title (`Onkey 4.5.0: ...`).

Bump the version in `Mac/build.sh` (`CFBundleShortVersionString`, and `CFBundleVersion`),
`Windows/Source/Program.cs` (`Version`) and both README.txt files, run `./package.sh`, then
publish a GitHub release tagged `vX.Y.Z` with `Onkey-Mac.dmg` and `Onkey-Mac.zip` attached.
Publishing it starts the Windows release workflow, which builds `Onkey.exe` and attaches
`Onkey-Windows-Setup.exe` and `Onkey-Windows.zip`. The DMG and the setup are what people
download; the zips are what the in-app updater downloads, so their names must stay exactly
as they are. Running Onkeys compare their version with the latest release's tag
and offer the update from the menu.
