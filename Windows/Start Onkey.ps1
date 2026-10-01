# Launches Onkey using Windows PowerShell and .NET Framework.
# Compiles every file in Source\ in memory, then runs it.
$ErrorActionPreference = 'Stop'
try {
    $env:ONKEY_APP_DIR = $PSScriptRoot
    # Assets sit beside this script in a release download, or in ..\Assets in the repo.
    $assets = Join-Path $PSScriptRoot 'Assets'
    if (-not (Test-Path -LiteralPath $assets)) { $assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets' }
    $env:ONKEY_ASSET_DIR = $assets
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Source') -Filter '*.cs' | ForEach-Object { $_.FullName })
    Add-Type -Path $sources -ReferencedAssemblies 'System.Windows.Forms','System.Drawing' -IgnoreWarnings -ErrorAction Stop
    [OnkeyDesktopPet.Program]::Main()
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Onkey could not start') | Out-Null
}
