ONKEY DESKTOP PET - VERSION 4.5.2 - WINDOWS

START
1. Open Onkey-Windows-Setup.exe. Windows may say it protected your PC
   because Onkey isn't from a known publisher: click "More info", then
   "Run anyway". Click through the installer; it needs no administrator
   password and starts Onkey at the end. Next time, start him from the
   Start menu. To remove him, use Settings > Apps.
   (From Onkey-Windows.zip instead: keep all the files together in one
   folder and double-click "Onkey.exe".)
2. Click Onkey's icon beside the Windows clock and a hand-drawn jungle
   settings panel pops up. Expand the ^ arrow if the icon is hidden.
   Click anywhere else (or press Escape) to close it.

SETTINGS PANEL (click the tray icon)
  Along the top   Pause, Dance (Onkeys bop on the beat of whatever Windows
                  is playing through the speakers; nothing is recorded or
                  sent anywhere), and Drag him (drag him anywhere and click
                  him to hear him; when off, clicks pass straight through)
  Moves           how many Onkeys (1-20; each wanders and blinks on his
                  own), where he roams (anywhere, an edge, or stay put), how
                  often he walks to your mouse, walking speed, and Bring
                  Onkey to this screen
  Sound           on/off, how often, volume, and Say oooo now. His mouth
                  opens while he makes his sound. Windows plays one sound at
                  a time, so a new "oooo" cuts off the last one.
  Look            size, how see-through he is, in front of windows or on
                  the desktop behind them, whether he stays over full-screen
                  apps (videos, games), eyes that watch your cursor, blinking
  Updates         Opening this tab looks for a newer Onkey right there.
                  When there is one, it shows what's new; choose "Update and
                  restart" and Onkey downloads it (you see the progress),
                  replaces the files in this folder, and restarts with your
                  settings kept. Check by
                  himself: once shortly after launch and then every six
                  hours, with a note by the clock when a new version is out.
                  Open at startup: when Windows starts.
  Exit Onkey      at the bottom of the panel.
  Report a problem  beside it. If Onkey crashed, it reads "Send crash
                  report" (and a note by the clock says so the next time he
                  starts). It opens a new GitHub issue in your browser with
                  his version, what went wrong and the end of his log, for
                  you to check and send; the whole log goes on your
                  clipboard. Onkey never sends anything himself.

Right-click the icon for a short menu: Pause, Dance to music, Let me drag
Onkey around, Settings, Report a problem, and Exit.

Onkey keeps a log of what he does (including every step of an update) in
%APPDATA%\Onkey\onkey.log, about the last half megabyte of it.

Settings and his position are saved in %APPDATA%\Onkey\settings.txt.
"Open at startup" adds an entry under
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run; turn it off to
remove it again. If you move the Onkey folder, turn it off and on again.

WINDOWS REQUIREMENTS
Windows 10 or 11 (Onkey.exe uses the .NET Framework that comes with them).
Nothing needs installing. The Source folder holds the code Onkey.exe is
built from; it isn't needed to run him.

The only thing Onkey does online is check GitHub for updates (and download
one when you ask). Untick "Check for updates automatically" to keep him
offline unless you choose "Check for updates...". Onkey is drawn live from Onkey.png, so he
stays sharp at every size and on high-DPI screens.
