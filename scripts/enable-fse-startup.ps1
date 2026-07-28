# FSE Path 1 reminder + Run key registration.
# Prefer scripts\install-startup.ps1 from the repo after publish.

$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$existing = (Get-ItemProperty -Path $run -Name 'SteamControllerBridge' -ErrorAction SilentlyContinue).SteamControllerBridge
if ($existing) {
  Write-Host "Already registered: $existing"
} else {
  Write-Host "Run scripts\install-startup.ps1 after building the Host."
}

Write-Host ""
Write-Host "Xbox mode / FSE:"
Write-Host "  Settings > Apps > Startup > Steam Controller Bridge > Start at log in"
Write-Host "AnyFSE Path 2: set custom startup to SteamControllerBridge.exe --tray"
Write-Host "Path 3: package FseHome as gamingHome MSIX (see docs/setup.md)"
