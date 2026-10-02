; Onkey-Windows-Setup.exe: the download for people, so installing is Next, Next, Finish.
; Built with Inno Setup 6 (jrsoftware.org) by .github/workflows/windows-release.yml after
; build.cmd has made Onkey.exe:  ISCC.exe /DVersion=4.5.1 Windows\installer.iss
; It installs into %LOCALAPPDATA%\Programs\Onkey, the same folder the in-app updater uses,
; so no administrator password is needed and updates keep working.

#ifndef Version
  #error Pass the version, for example /DVersion=4.5.1
#endif

[Setup]
; Never change this: it is how a newer installer finds the Onkey it updates.
AppId={{808C4548-4664-47FA-B9AA-6CD4515745F7}
AppName=Onkey
AppVersion={#Version}
AppVerName=Onkey {#Version}
AppPublisher=nolpearce
AppPublisherURL=https://github.com/nolpearce/onkey
AppSupportURL=https://github.com/nolpearce/onkey/issues
DefaultDirName={localappdata}\Programs\Onkey
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Onkey-Windows-Setup
SetupIconFile=Onkey.ico
UninstallDisplayIcon={app}\Onkey.exe
UninstallDisplayName=Onkey
WizardStyle=modern
WizardImageFile=..\Assets\Installer\wizard.bmp,..\Assets\Installer\wizard@2x.bmp
WizardSmallImageFile=..\Assets\Installer\wizard-small.bmp,..\Assets\Installer\wizard-small@2x.bmp
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no

[Messages]
WelcomeLabel1=Onkey is coming to your desktop!
WelcomeLabel2=He'll wander around your screen going "oooo".%n%nAfterwards, click his little head beside the clock for settings.
FinishedHeadingLabel=Onkey is in!
FinishedLabel=Look for his head beside the clock (open the ^ arrow if it's hidden) and click it for the jungle settings panel.

[Tasks]
Name: desktopicon; Description: "Put Onkey on the desktop"; GroupDescription: "Shortcuts:"
Name: startup; Description: "Open Onkey when Windows starts"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "Onkey.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Assets\Onkey.png"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "..\Assets\Sounds\oooo.wav"; DestDir: "{app}\Assets\Sounds"; Flags: ignoreversion
Source: "..\Assets\Sounds\README.txt"; DestDir: "{app}\Assets\Sounds"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Onkey"; Filename: "{app}\Onkey.exe"
Name: "{userdesktop}\Onkey"; Filename: "{app}\Onkey.exe"; Tasks: desktopicon

[Registry]
; The same entry the panel's "Open at startup" switch writes, so the switch shows it on.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Onkey"; ValueData: """{app}\Onkey.exe"""; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Onkey"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\Onkey.exe"; Description: "Start Onkey now"; Flags: nowait postinstall

[UninstallDelete]
; Files later updates bring that the installer didn't put there.
Type: filesandordirs; Name: "{app}\Source"
Type: files; Name: "{app}\Start Onkey.vbs"

[Code]
// A running Onkey holds Onkey.exe open, so close him before copying and before uninstalling.
procedure CloseOnkey();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Onkey.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(700);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseOnkey();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseOnkey();
  Result := True;
end;
