# Launches Onkey using Windows PowerShell and .NET Framework.
$ErrorActionPreference = 'Stop'
try {
    $env:ONKEY_ASSET_DIR = $PSScriptRoot
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Onkey.cs') -Raw
    Add-Type -TypeDefinition $source -ReferencedAssemblies 'System.Windows.Forms','System.Drawing' -ErrorAction Stop
    [OnkeyDesktopPet.Program]::Main()
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Onkey could not start') | Out-Null
}
