# Onkey

A little desktop pet that wanders around your screen going "oooo". Just for fun :)

![Onkey walking](Preview.gif)

## Mac

Needs the Xcode Command Line Tools (`xcode-select --install`).

```bash
./Mac/build.sh
```

This builds `Onkey.app`, installs it in `/Applications` and launches it. Click the Onkey head in the menu bar to:

- **How Many Onkeys**: one, two, three, five, or ten (chaos), each wandering on his own
- **Where Onkey Goes**: anywhere, along an edge, or stay put; how often he walks to your mouse; walking speed
- **Sound**: on/off, how often, volume, play now. His mouth opens while he makes the sound
- **Appearance**: size, opacity, in front of windows or on the desktop; eyes that watch your cursor; blinking
- **Let Me Drag Onkey Around**, **Open Onkey at Login**, **Pause**, **Quit**

Or download `Onkey-Mac.zip` from [Releases](https://github.com/nolpearce/onkey/releases), unzip it and drag `Onkey.app` into Applications. It runs on Apple Silicon and Intel Macs with macOS 13 or later.
The first time, macOS will say it can't check the app: click **Done**, then go to **System Settings → Privacy & Security** and click **Open Anyway**.
More detail is in [Mac/README-Mac.txt](Mac/README-Mac.txt).

## Windows

Download `Onkey-Windows.zip` from [Releases](https://github.com/nolpearce/onkey/releases) (or clone this repo), extract it, and double-click `Start Onkey.vbs`. Then right-click Onkey's icon beside the clock. The menu has the same settings as the Mac version. See [README.txt](README.txt).

## Files

| Path | What it is |
|---|---|
| `Onkey.png` | The sprite. Both versions draw every frame from it live |
| `Sounds/oooo.wav` | His sound |
| `Mac/Onkey.swift`, `Mac/build.sh` | The Mac app and its build script |
| `Onkey.cs`, `Start Onkey.ps1`, `Start Onkey.vbs` | The Windows version, compiled by PowerShell at launch |
