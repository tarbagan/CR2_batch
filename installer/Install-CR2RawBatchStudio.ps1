$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Split-Path -Parent $scriptRoot
$sourceRoot = Join-Path $packageRoot "app"
$targetRoot = Join-Path $env:LOCALAPPDATA "Programs\CR2RawBatchStudio"
$startMenuRoot = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuShortcut = Join-Path $startMenuRoot "CR2(RAW) Batch Studio.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "CR2(RAW) Batch Studio.lnk"
$exePath = Join-Path $targetRoot "CR2RawBatchStudio.exe"

if (-not (Test-Path $sourceRoot)) {
    throw "Application files were not found in: $sourceRoot"
}

New-Item -ItemType Directory -Force $targetRoot | Out-Null
Copy-Item -Path (Join-Path $sourceRoot "*") -Destination $targetRoot -Recurse -Force

$wsh = New-Object -ComObject WScript.Shell

foreach ($shortcutPath in @($startMenuShortcut, $desktopShortcut)) {
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $targetRoot
    $shortcut.IconLocation = "$exePath,0"
    $shortcut.Save()
}

$uninstallScriptPath = Join-Path $targetRoot "Uninstall-CR2RawBatchStudio.ps1"
@'
$ErrorActionPreference = "Stop"
$targetRoot = Join-Path $env:LOCALAPPDATA "Programs\CR2RawBatchStudio"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\CR2(RAW) Batch Studio.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "CR2(RAW) Batch Studio.lnk"

foreach ($shortcut in @($startMenuShortcut, $desktopShortcut)) {
    if (Test-Path $shortcut) {
        Remove-Item -LiteralPath $shortcut -Force
    }
}

if (Test-Path $targetRoot) {
    Remove-Item -LiteralPath $targetRoot -Recurse -Force
}

Write-Host "CR2(RAW) Batch Studio has been removed."
'@ | Set-Content -Path $uninstallScriptPath -Encoding UTF8

Write-Host "Installed to $targetRoot"
Write-Host "Start menu shortcut: $startMenuShortcut"
Write-Host "Desktop shortcut: $desktopShortcut"
