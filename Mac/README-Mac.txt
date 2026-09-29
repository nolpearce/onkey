ONKEY DESKTOP PET - MAC - VERSION 4

START
Open Onkey from your Applications folder (or Launchpad / Spotlight).
Click the little Onkey head in the menu bar (top right of the screen) for:

  Pause Onkey
  Where Onkey Goes   anywhere, along the bottom/top, left/right side, or stay
                     in one spot; how often he walks to your mouse; walking
                     speed; Bring Onkey to This Screen
  Sound              on/off, how often, volume, Play Sound Now
  Appearance         size (tiny to huge), opacity, in front of all windows or
                     on the desktop behind windows
                     Eyes > Watch my cursor: his pupils follow your mouse
                     Eyes > Blink now and then: left eye, then right
  Let Me Drag Onkey Around
                     when ticked, drag him anywhere; click him to hear him.
                     When unticked, clicks pass straight through him.
  Open Onkey at Login
  Quit Onkey

Settings and his position are remembered between launches.

If macOS says the app "can't be opened" the first time, right-click
Onkey.app > Open > Open. You only need to do this once.

REBUILDING
After changing Onkey.swift, Onkey.png or the sound, run in Terminal:
    ./build.sh
It rebuilds, replaces /Applications/Onkey.app, and relaunches him.
Requires the Xcode Command Line Tools (xcode-select --install).

NOTES
Onkey is drawn live from Onkey.png using the same arm-swing animation as the
Windows version, so he stays sharp at any size and on Retina screens. The
Frames folder is only used by the Windows version.
