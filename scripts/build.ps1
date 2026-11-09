param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipMsi
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"
$artifacts = Join-Path $root "artifacts"
New-Item -ItemType Directory -Force -Path $publish | Out-Null

Push-Location $root
try {
    dotnet build PotatoLauncher.slnx -c $Configuration
    dotnet run --project tests\PotatoLauncher.Tests\PotatoLauncher.Tests.csproj -c $Configuration --no-build
    dotnet publish src\PotatoLauncher.App\PotatoLauncher.App.csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publish

    $zip = Join-Path $artifacts "PotatoLauncher-0.0.1-beta-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal -Force

    if (-not $SkipMsi) {
        wix build installer\Package.wxs -arch x64 -o (Join-Path $artifacts "PotatoLauncher-0.0.1-beta-win-x64.msi")
    }

    Write-Host "Portable: $zip"
    if (-not $SkipMsi) {
        Write-Host "Installer: $(Join-Path $artifacts 'PotatoLauncher-0.0.1-beta-win-x64.msi')"
    }
}
finally {
    Pop-Location
}
