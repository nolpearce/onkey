# Onkey

A little desktop pet that wanders around your screen going "oooo". Just for fun, not for commercial use.

![Onkey walking](Preview.gif)

## Mac

Needs the Xcode Command Line Tools (`xcode-select --install`).

```bash
./Mac/build.sh
```

This builds `Onkey.app`, installs it in `/Applications` and launches it. Click the Onkey head in the menu bar to:

- **Where Onkey Goes**: anywhere, along an edge, or stay put; how often he walks to your mouse; walking speed
- **Sound**: on/off, how often, volume, play now. His mouth opens while he makes the sound
- **Appearance**: size, opacity, in front of windows or on the desktop; eyes that watch your cursor; blinking
- **Let Me Drag Onkey Around**, **Open Onkey at Login**, **Pause**, **Quit**

The first time you open a copy that you didn't build yourself, right-click `Onkey.app` → **Open** → **Open**.
More detail is in [Mac/README-Mac.txt](Mac/README-Mac.txt).

## Windows

Double-click `Start Onkey.vbs`, then right-click the tray icon for Pause, Mute or Exit. See [README.txt](README.txt).

## Files

| Path | What it is |
|---|---|
| `Onkey.png` | The sprite. The Mac version draws every frame from it live |
| `Sounds/oooo.wav` | His sound |
| `Mac/Onkey.swift`, `Mac/build.sh` | The Mac app and its build script |
| `Onkey.cs`, `Start Onkey.ps1`, `Start Onkey.vbs`, `Frames/` | The Windows version and its pre-rendered frames |
