ONKEY DESKTOP PET - VERSION 4.4.3 - WINDOWS

START
1. Exit any older Onkey from its tray icon first.
2. Keep all the files together in one folder.
3. Double-click "Start Onkey.vbs".
4. Right-click Onkey's icon beside the Windows clock and choose Settings.
   Expand the ^ arrow if the icon is hidden. Double-click it to pause.

TRAY MENU (right-click the tray icon)
  Pause Onkey
  Dance to music     Onkeys bop (squash down 15%) on the beat of whatever
                     Windows is playing through the speakers. Nothing is
                     recorded or sent anywhere.
  Let me drag Onkey around
                     when ticked, drag him anywhere; click him to hear him.
                     When unticked, clicks pass straight through him.
  Update to Onkey x.y.z
                     only there when a newer Onkey is out on GitHub.
  Settings...        everything else, below.
  Exit Onkey

SETTINGS WINDOW
  Behaviour   how many Onkeys (1-20; each wanders and blinks on his own),
              where he roams (anywhere, an edge, or stay put), how often he
              walks to your mouse, walking speed, dancing, dragging, and
              Bring Onkey to this screen
  Sound       on/off, how often, volume, and Say oooo now. His mouth opens
              while he makes his sound. Windows plays one sound at a time,
              so a new "oooo" cuts off the last one.
  Look        size, how see-through he is, in front of windows or on the
              desktop behind them, whether he stays over full-screen apps
              (videos, games), eyes that watch your cursor, and blinking
  Updates     Check for updates: when there's a newer Onkey, choose Update
              and Onkey downloads it, replaces the files in this folder, and
              restarts with your settings kept.
              Check by himself: once shortly after launch and then every six
              hours, with a note by the clock when a new version is out.
              Open at startup: when Windows starts.

Settings and his position are saved in %APPDATA%\Onkey\settings.txt.
"Open at startup" adds an entry under
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run; turn it off to
remove it again. If you move the Onkey folder, turn it off and on again.

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
