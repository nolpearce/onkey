@echo off
rem Builds Onkey.exe from Source\*.cs with the C# compiler that comes with .NET Framework
rem (part of every Windows 10/11 install), so nothing needs installing. Run it from a
rem command prompt in this folder. Onkey.exe finds Onkey.png and Sounds in ..\Assets.
setlocal
cd /d "%~dp0"
set "csc=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%csc%" set "csc=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%csc%" (
    echo Couldn't find the .NET Framework 4 C# compiler, csc.exe.
    exit /b 1
)
"%csc%" /nologo /target:winexe /optimize+ /out:Onkey.exe /win32icon:Onkey.ico ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
    Source\*.cs
if errorlevel 1 exit /b 1
echo Built Onkey.exe
