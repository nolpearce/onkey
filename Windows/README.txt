ONKEY DESKTOP PET - VERSION 4.4 - WINDOWS

START
1. Exit any older Onkey from its tray icon first.
2. Keep all the files together in one folder.
3. Double-click "Start Onkey.vbs".
4. Right-click Onkey's icon beside the Windows clock for settings.
   Expand the ^ arrow if the icon is hidden. Double-click it to pause.

SETTINGS (right-click the tray icon)
  Pause Onkey
  Dance to music     Onkeys bop (squash down 15%) on the beat of whatever
                     Windows is playing through the speakers. Nothing is
                     recorded or sent anywhere.
  How many Onkeys    one, two, three, five, or ten (chaos). Each wanders and
                     blinks on his own. Windows plays one sound at a time, so
                     a new "oooo" cuts off the last one.
  Where Onkey goes   anywhere, along the bottom/top, left/right side, or stay
                     in one spot; how often he walks to your mouse; walking
                     speed; Bring Onkey to this screen
  Sound              on/off, how often, volume, Play sound now.
                     His mouth opens while he makes his sound.
  Appearance         size (tiny to huge), opacity, in front of all windows or
                     on the desktop behind windows
                     Eyes > Watch my cursor: his pupils follow your mouse
                     Eyes > Blink now and then: left eye, then right
  Let me drag Onkey around
                     when ticked, drag him anywhere; click him to hear him.
                     When unticked, clicks pass straight through him.
  Check for updates  looks for a newer Onkey on GitHub. When there is one,
                     this says "Update to Onkey x.y": choose it and Onkey
                     downloads it, replaces the files in this folder, and
                     restarts with your settings kept.
  Check for updates automatically
                     looks once shortly after launch and then every six
                     hours, and shows a note by the clock when a new version
                     is out. Untick it to stop.
  Open Onkey when Windows starts
  Exit Onkey

Settings and his position are saved in %APPDATA%\Onkey\settings.txt.
"Open Onkey when Windows starts" adds an entry under
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run; untick it to
remove it again. If you move the Onkey folder, untick and re-tick it.

WINDOWS REQUIREMENTS
Windows PowerShell and .NET Framework, included in standard Windows 10/11.
The files in Source\ are compiled in memory on launch; no install is needed.
Windows Script Host is used by the VBS launcher. If VBS is disabled, open
Windows PowerShell in this folder and run:
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Start Onkey.ps1"

The only thing Onkey does online is check GitHub for updates (and download
one when you ask). Untick "Check for updates automatically" to keep him
offline unless you choose "Check for updates...". Onkey is drawn live from Onkey.png, so he
stays sharp at every size and on high-DPI screens.
