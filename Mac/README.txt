ONKEY DESKTOP PET - MAC - VERSION 4.4.3

START
Open Onkey from your Applications folder (or Launchpad / Spotlight).
Click the little Onkey head in the menu bar (top right of the screen) for:

  Pause Onkey
  Dance to Music     Onkeys bop (squash down 15%) on the beat of whatever
                     your Mac is playing. The first time, macOS asks whether
                     Onkey may listen to system audio (on macOS 13-14.1 it
                     uses the microphone instead). Nothing is recorded or sent
                     anywhere.
  How Many Onkeys    one, two, three, five, or ten (chaos). Each wanders,
                     blinks and "oooo"s on his own.
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
  Check for Updates  looks for a newer Onkey on GitHub. When there is one,
                     this says "Update to Onkey x.y": choose it and Onkey
                     downloads it, replaces Onkey.app, and reopens with your
                     settings kept. Onkey.app needs to be in a folder you can
                     write to, like Applications. After an update macOS may
                     ask again whether he can listen to your music.
  Check for Updates Automatically
                     looks once shortly after launch and then every six
                     hours. Untick it to keep Onkey offline.
  Open Onkey at Login
  Quit Onkey

Settings and his position are remembered between launches.

If you downloaded Onkey (rather than building it), macOS will say it can't
check the app the first time. Click Done, then open System Settings >
Privacy & Security, scroll down, and click "Open Anyway" next to Onkey.
On macOS 14 or older you can instead right-click Onkey.app > Open > Open.
You only need to do this once.

REBUILDING
After changing anything in Sources/ or ../Assets, run in Terminal:
    ./build.sh
It rebuilds, replaces /Applications/Onkey.app, and relaunches him.
Requires the Xcode Command Line Tools (xcode-select --install).

SOURCES
  main.swift            starts the app (plus --export / --beat-test checks)
  OnkeyApp.swift        menu bar menu, settings, timer, and the Onkeys
  Pet.swift             one Onkey: walking, eyes, blinks, mouth, bopping
  PetView.swift         draws one Onkey (body, pupils, lids, mouth)
  Renderer.swift        draws his frames from Onkey.png
  Settings.swift        setting names and cached values
  Updater.swift         finds and installs new releases from GitHub
  BeatTracker.swift     finds the beat in audio
  MusicListener.swift   hears what the Mac is playing

NOTES
Onkey is drawn live from ../Assets/Onkey.png using the same arm-swing
animation as the Windows version, so he stays sharp at any size and on Retina
screens.
