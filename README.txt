ONKEY DESKTOP PET - VERSION 3 - WINDOWS

START
1. Exit the old Onkey from its tray icon first.
2. Extract this entire ZIP to a fresh folder.
3. Double-click "Start Onkey.vbs".
4. Right-click the icon beside the Windows clock for Pause, Mute, or Exit.
   Expand the ^ arrow if the icon is hidden.

CHANGES
- True per-pixel transparency fixes the old pink edge.
- Onkey is approximately 40% smaller (140 pixels wide instead of 224).
- Arms alternate lifting and planting while he moves; his head gently bobs.
- He rests briefly between trips.
- Sound is scheduled only once every 90-180 seconds.
- The old synthetic chirp and text speech bubbles have been removed.

AUDIO
Your supplied short Onkey sound is included as Sounds\oooo.wav.
It plays once every 90-180 seconds while Onkey is running and unpaused.
Right-click the tray icon to mute or unmute it.
The clip has short fades at the edges to prevent clicks.

WINDOWS REQUIREMENTS
Windows PowerShell and .NET Framework, included in standard Windows 10/11.
The supplied C# source is compiled in memory on launch; no Python is needed.
Windows Script Host is used by the VBS launcher. If VBS is disabled, open
Windows PowerShell in this folder and run:
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Start Onkey.ps1"

Clicks pass through Onkey. Controls are in the system tray. The app does not
use the internet or start automatically with Windows. Keep all files together.

VERIFICATION
The C# source compiles successfully. All 40 animation frames were rendered
and checked for transparent edges, clipping, and correct alpha data.
Native Windows overlay behavior still needs checking on a Windows desktop.
Preview.gif shows the walking cycle.
