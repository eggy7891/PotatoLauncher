param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build PotatoLauncher.slnx -c $Configuration
    dotnet run --project tests\PotatoLauncher.Tests\PotatoLauncher.Tests.csproj -c $Configuration --no-build
}
finally {
    Pop-Location
}
