param(
    [Parameter(Mandatory = $true)]
    [string]$GhPath,
    [string]$Repository = "eggy7891/PotatoLauncher",
    [string]$Branch = "main",
    [string]$Message = "Initial open-source PotatoLauncher implementation"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $trackedFiles = git ls-files
    if (-not $trackedFiles) {
        throw "No tracked files were found. Commit the source before pushing."
    }

    $entries = New-Object System.Collections.Generic.List[object]
    $index = 0
    foreach ($relativePath in $trackedFiles) {
        $index++
        Write-Host "[$index/$($trackedFiles.Count)] Uploading blob: $relativePath"
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $root $relativePath))
        $payload = @{
            content = [Convert]::ToBase64String($bytes)
            encoding = "base64"
        } | ConvertTo-Json -Compress
        $sha = $payload | & $GhPath api -X POST "repos/$Repository/git/blobs" --input - --jq ".sha"
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) {
            throw "Blob upload failed: $relativePath"
        }
        $entries.Add(@{
            path = $relativePath.Replace('\', '/')
            mode = "100644"
            type = "blob"
            sha = $sha.Trim()
        })
    }

    Write-Host "Creating tree..."
    $treePayload = @{ tree = $entries } | ConvertTo-Json -Depth 10 -Compress
    $treeSha = $treePayload | & $GhPath api -X POST "repos/$Repository/git/trees" --input - --jq ".sha"
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($treeSha)) {
        throw "Tree creation failed."
    }

    $parentSha = $null
    $existingRef = & $GhPath api "repos/$Repository/git/ref/heads/$Branch" --jq ".object.sha" 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existingRef)) {
        $parentSha = $existingRef.Trim()
    }

    Write-Host "Creating commit..."
    $commitPayload = @{
        message = $Message
        tree = $treeSha.Trim()
    }
    if ($parentSha) {
        $commitPayload.parents = @($parentSha)
    }
    $commitJson = $commitPayload | ConvertTo-Json -Depth 10 -Compress
    $commitSha = $commitJson | & $GhPath api -X POST "repos/$Repository/git/commits" --input - --jq ".sha"
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commitSha)) {
        throw "Commit creation failed."
    }

    if ($parentSha) {
        Write-Host "Updating branch ref..."
        $refPayload = @{
            sha = $commitSha.Trim()
            force = $true
        } | ConvertTo-Json -Compress
        $result = $refPayload | & $GhPath api -X PATCH "repos/$Repository/git/refs/heads/$Branch" --input -
    }
    else {
        Write-Host "Creating branch ref..."
        $refPayload = @{
            ref = "refs/heads/$Branch"
            sha = $commitSha.Trim()
        } | ConvertTo-Json -Compress
        $result = $refPayload | & $GhPath api -X POST "repos/$Repository/git/refs" --input -
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Branch ref update failed."
    }
    Write-Host "Pushed commit $commitSha to $Repository#$Branch"
}
finally {
    Pop-Location
}
