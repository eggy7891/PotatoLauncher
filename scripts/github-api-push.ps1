param(
    [Parameter(Mandatory = $true)]
    [string]$GhPath,
    [string]$Repository = "eggy7891/PotatoLauncher",
    [string]$Branch = "main",
    [string]$Message = "Update PotatoLauncher"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $ref = & $GhPath api "repos/$Repository/git/ref/heads/$Branch"
    if ($LASTEXITCODE -ne 0) {
        throw "Branch '$Branch' does not exist."
    }
    $refObject = $ref | ConvertFrom-Json
    $parentSha = $refObject.object.sha
    $commit = & $GhPath api "repos/$Repository/git/commits/$parentSha" | ConvertFrom-Json
    $baseTreeSha = $commit.tree.sha
    $remoteTree = & $GhPath api "repos/$Repository/git/trees/${baseTreeSha}?recursive=1" | ConvertFrom-Json
    $remoteEntries = @{}
    foreach ($entry in $remoteTree.tree) {
        if ($entry.type -eq "blob") {
            $remoteEntries[$entry.path] = $entry.sha
        }
    }

    $localEntries = @{}
    $rawTree = git ls-tree -r -z --format="%(objectname) %(objectmode) %(path)" HEAD
    foreach ($record in ($rawTree -split "`0" | Where-Object { $_ })) {
        if ($record -match '^([0-9a-f]+) ([0-7]+) (.+)$') {
            $localEntries[$Matches[3]] = @{
                Sha = $Matches[1]
                Mode = $Matches[2]
                Path = $Matches[3]
            }
        }
    }

    $treeEntries = New-Object System.Collections.Generic.List[object]
    $uploaded = 0
    foreach ($path in $localEntries.Keys | Sort-Object) {
        $local = $localEntries[$path]
        if ($remoteEntries.ContainsKey($path) -and $remoteEntries[$path] -eq $local.Sha) {
            continue
        }

        Write-Host "Uploading changed blob: $path"
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $root $path))
        $payload = @{
            content = [Convert]::ToBase64String($bytes)
            encoding = "base64"
        } | ConvertTo-Json -Compress
        $sha = $payload | & $GhPath api -X POST "repos/$Repository/git/blobs" --input - --jq ".sha"
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha)) {
            throw "Blob upload failed: $path"
        }
        $treeEntries.Add(@{
            path = $path
            mode = $local.Mode
            type = "blob"
            sha = $sha.Trim()
        })
        $uploaded++
    }

    foreach ($path in $remoteEntries.Keys | Where-Object { -not $localEntries.ContainsKey($_) }) {
        Write-Host "Removing deleted path: $path"
        $treeEntries.Add(@{
            path = $path
            mode = "100644"
            type = "blob"
            sha = $null
        })
    }

    if ($treeEntries.Count -eq 0) {
        Write-Host "No file changes to push."
        return
    }

    $treePayload = @{
        base_tree = $baseTreeSha
        tree = $treeEntries
    } | ConvertTo-Json -Depth 10 -Compress
    $treeSha = $treePayload | & $GhPath api -X POST "repos/$Repository/git/trees" --input - --jq ".sha"
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($treeSha)) {
        throw "Tree creation failed."
    }

    $commitPayload = @{
        message = $Message
        tree = $treeSha.Trim()
        parents = @($parentSha)
    } | ConvertTo-Json -Depth 10 -Compress
    $commitSha = $commitPayload | & $GhPath api -X POST "repos/$Repository/git/commits" --input - --jq ".sha"
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commitSha)) {
        throw "Commit creation failed."
    }

    $updatePayload = @{
        sha = $commitSha.Trim()
        force = $true
    } | ConvertTo-Json -Compress
    $updatePayload | & $GhPath api -X PATCH "repos/$Repository/git/refs/heads/$Branch" --input - | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Branch ref update failed."
    }
    Write-Host "Pushed $uploaded changed blobs as $commitSha to $Repository#$Branch"
}
finally {
    Pop-Location
}
