param(
    [Parameter(Mandatory = $false)]
    [string]$MinecraftDirectory = (Join-Path $env:APPDATA ".minecraft")
)

$versions = Join-Path $MinecraftDirectory "versions"
if (-not (Test-Path -LiteralPath $versions)) {
    throw "没有找到 versions 目录：$versions"
}

Get-ChildItem -LiteralPath $versions -Directory | ForEach-Object {
    $json = Join-Path $_.FullName ($_.Name + ".json")
    if (-not (Test-Path -LiteralPath $json)) {
        $json = Get-ChildItem -LiteralPath $_.FullName -Filter "*.json" -File |
            Where-Object { $_.Name -ne "PCL.json" } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (Test-Path -LiteralPath $json) {
        try {
            $metadata = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
            [PSCustomObject]@{
                Id = $_.Name
                Type = $metadata.type
                InheritsFrom = $metadata.inheritsFrom
                JavaMajor = $metadata.javaVersion.majorVersion
                Json = $json
            }
        }
        catch {
            [PSCustomObject]@{
                Id = $_.Name
                Type = "invalid"
                InheritsFrom = $null
                JavaMajor = $null
                Json = $json
            }
        }
    }
} | Format-Table -AutoSize
