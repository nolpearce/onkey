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
  Let Me Drag Onkey Around
                     when ticked, drag him anywhere; click him to hear him.
                     When unticked, clicks pass straight through him.
  Update to Onkey x.y.z
                     only there when a newer Onkey is out on GitHub.
  Settings...        everything else (Command-comma), below.
  Quit Onkey

SETTINGS WINDOW
  Behaviour   how many Onkeys (1-20; each wanders, blinks and "oooo"s on
              his own), where he roams (anywhere, an edge, or stay put),
              how often he walks to your mouse, walking speed, dancing,
              dragging, and Bring Onkey to this screen
  Sound       on/off, how often, volume, and Say oooo now. His mouth opens
              while he makes the sound.
  Look        size, how see-through he is, in front of windows or on the
              desktop behind them, whether he stays over full-screen apps,
              eyes that watch your cursor, and blinking
  Updates     Check for updates: when there's a newer Onkey, choose Update
              and Onkey downloads it, replaces Onkey.app, and reopens with
              your settings kept. Onkey.app needs to be in a folder you can
              write to, like Applications. After an update macOS may ask
              again whether he can listen to your music.
              Check by himself: once shortly after launch and then every
              six hours. Turn it off to keep Onkey offline.
              Open at login.

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
  SettingsWindow.swift  the hand-drawn jungle Settings window (SwiftUI)
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
