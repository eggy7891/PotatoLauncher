$ErrorActionPreference = "Stop"
$url = "https://github.com/eggy7891/PotatoLauncher/releases/latest/download/PotatoLauncher-win-x64-portable.zip"
$destination = Join-Path (Get-Location) "PotatoLauncher-win-x64-portable.zip"
Invoke-WebRequest -Uri $url -OutFile $destination
Write-Host "Downloaded the latest PotatoLauncher to $destination"
