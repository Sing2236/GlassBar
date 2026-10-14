[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$PrivateKeyPath = (Join-Path $env:USERPROFILE '.glassbar\update-signing-private.pem'),
    [string]$Repository = 'Sing2236/GlassBar',

    # Optional. When omitted, the patch notes are built from the commit subjects
    # since the last published release, which is what they should say anyway.
    [string]$ReleaseNotes = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$currentBranch = (& git -C $projectRoot branch --show-current).Trim()
if ($currentBranch -ne 'main') { throw 'Secure releases must be published from main.' }

$trackedChanges = @(& git -C $projectRoot status --porcelain --untracked-files=no)
if ($trackedChanges.Count -gt 0) { throw 'Commit tracked changes before publishing a release.' }
if (-not (Test-Path -LiteralPath $PrivateKeyPath)) { throw "The offline update signing key was not found at $PrivateKeyPath." }

$commit = (& git -C $projectRoot rev-parse HEAD).Trim()
$remoteCommit = (& gh api "repos/$Repository/commits/main" --jq .sha).Trim()
if ($commit -ne $remoteCommit) { throw 'Local main must exactly match the remote main branch.' }

$tag = "v$Version"
$existingTags = @(& gh release list --repo $Repository --limit 100 --json tagName --jq '.[].tagName')
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect existing GitHub releases.' }
if ($existingTags -contains $tag) { throw "Release $tag already exists." }

# The last published tag, so the patch notes can be built from what has actually
# changed since it rather than from the whole history.
$previousTag = (& gh release list --repo $Repository --limit 1 --json tagName --jq '.[0].tagName' 2>$null)
$previousTag = if ($previousTag) { "$previousTag".Trim() } else { '' }

$artifactRoot = Join-Path $projectRoot "artifacts\release-$Version"
$publishDirectory = Join-Path $artifactRoot 'app'
$publishedExecutable = Join-Path $publishDirectory 'GlassBar.exe'
$manifestPath = Join-Path $artifactRoot 'update.json'
$installerPath = Join-Path $projectRoot 'installer\output\GlassBarSetup.exe'
$installerUrl = "https://github.com/$Repository/releases/download/$tag/GlassBarSetup.exe"

$compilerCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup 6 is required to build the installer.' }

if (-not $PSCmdlet.ShouldProcess("$Repository $tag", 'Build, sign, and publish a production release')) { return }

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
& dotnet publish (Join-Path $projectRoot 'GlassBar.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -p:Version=$Version `
    -p:InformationalVersion="$Version+$commit" `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$publishedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($publishedExecutable).FileVersion
if (-not $publishedVersion.StartsWith("$Version.", [StringComparison]::Ordinal)) {
    throw "Published executable version $publishedVersion does not match $Version."
}

& $compiler "/DMyAppVersion=$Version" "/DMyAppSourceDir=$publishDirectory" `
    (Join-Path $projectRoot 'installer\GlassBar.iss')
if ($LASTEXITCODE -ne 0) { throw 'The installer build failed.' }

$installerSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath).Hash.ToLowerInvariant()

# The patch notes the update prompt will show. Generated from the commits since
# the last release unless the caller supplied their own.
$notesPath = Join-Path $artifactRoot 'release-notes.txt'
if ($ReleaseNotes.Trim()) {
    $notesBody = $ReleaseNotes.Trim()
} else {
    # A tag created by `gh release create` lives on the remote, and a local clone
    # may not have it yet - in which case "tag..HEAD" is unresolvable, git fails,
    # and the range yields nothing. Fetch first, and if it still will not resolve
    # fall back to the most recent commits rather than writing a placeholder: these
    # notes are what the update prompt shows the user, so a silent "Maintenance
    # release." publishes a release with nothing to say for itself. That happened
    # on v1.0.5 and had to be re-signed by hand.
    & git -C $projectRoot fetch --tags --quiet 2>$null | Out-Null
    $range = if ($previousTag) { "$previousTag..$commit" } else { '' }
    $lines = if ($range) { @(& git -C $projectRoot log --no-merges --pretty=format:'- %s' $range 2>$null) } else { @() }
    if (-not ("$($lines -join '')".Trim())) {
        Write-Warning "Could not resolve '$range' for the patch notes; falling back to the most recent commits."
        $lines = @(& git -C $projectRoot log --no-merges --pretty=format:'- %s' -n 15 $commit 2>$null)
    }
    $notesBody = ($lines -join "`n").Trim()
    if (-not $notesBody) {
        throw 'Could not determine any patch notes. Refusing to publish a release whose notes are empty.'
    }
}
Set-Content -LiteralPath $notesPath -Value $notesBody -Encoding utf8
Write-Host "Patch notes:"
Write-Host $notesBody

$signingTool = Join-Path $projectRoot 'tools\GlassBar.LicenseIssuer\GlassBar.LicenseIssuer.csproj'
& dotnet run --project $signingTool -c Release -- sign-update `
    $PrivateKeyPath $manifestPath $Version $installerUrl $installerSha256 $Repository $notesPath
if ($LASTEXITCODE -ne 0) { throw 'Update manifest signing failed.' }

& dotnet run --project $signingTool -c Release -- verify-update `
    (Join-Path $projectRoot 'assets\update-public.pem') $manifestPath $Repository
if ($LASTEXITCODE -ne 0) { throw 'Update manifest verification failed.' }

$releaseNotes = @"
$notesBody

---

Secure update release built from commit $commit.

- Update metadata is signed with GlassBar's offline release key.
- GlassBar rejects unsigned or modified update manifests.
- CI validates builds but cannot publish production releases.
"@

& gh release create $tag `
    $installerPath `
    $publishedExecutable `
    $manifestPath `
    --repo $Repository `
    --target $commit `
    --title "GlassBar $tag" `
    --notes $releaseNotes `
    --latest
if ($LASTEXITCODE -ne 0) { throw 'GitHub release publication failed.' }

Write-Host "Published $tag with an offline-signed update manifest."
