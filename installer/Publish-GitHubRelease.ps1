param(
    [string]$Repository = 'XiaozhuangXzsg/WLAN-LAN-Monitor-',
    [string]$TargetCommit,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'NetworkMonitor.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The project must specify a release version.' }
if ($Repository -notmatch '^[\w.-]+/[\w.-]+$') { throw 'Invalid GitHub repository name.' }
$tag = 'v' + $version
$assetName = 'NetworkMonitor-Setup-' + $version + '.exe'
$installer = Join-Path $projectRoot ('artifacts\' + $assetName)
if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Build and verify the installer before publishing.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $installer).Length
$changelog = Get-Content -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Raw -Encoding UTF8
$section = [regex]::Match($changelog, '(?ms)^## ' + [regex]::Escape($version) + '\s*\r?\n(?<notes>.*?)(?=^## |\z)')
if (!$section.Success) { throw 'Add release notes for this version to CHANGELOG.md first.' }
$notes = ($section.Groups['notes'].Value -split '```', 2)[0].Trim()
$notes += "`n`n安装前请从托盘菜单退出旧版。需要 .NET 8 Windows Desktop Runtime。用量日志保存在安装目录之外，升级后保留。`n`n安装包：$assetName（$size 字节）`nSHA-256：$hash"

# Obtain existing credentials in memory; never print or persist the token.
$token = $env:GH_TOKEN
if (!$token) { $token = $env:GITHUB_TOKEN }
$previousInteractive = $env:GCM_INTERACTIVE
$previousPrompt = $env:GIT_TERMINAL_PROMPT
try {
    if (!$token) {
        $env:GCM_INTERACTIVE = 'never'; $env:GIT_TERMINAL_PROMPT = '0'
        $credential = @("protocol=https`nhost=github.com`n`n" | git -c credential.interactive=false -c credential.guiPrompt=false -c core.askPass= credential fill)
        if ($LASTEXITCODE -ne 0) { throw 'No existing GitHub credential is available. Sign in to Git or supply GH_TOKEN.' }
        $passwordLine = $credential | Where-Object { $_.StartsWith('password=') } | Select-Object -First 1
        if ($passwordLine) { $token = $passwordLine.Substring(9) }
        $credential = $null; $passwordLine = $null
    }
    if (!$token) { throw 'A GitHub credential with repository write permission is required.' }
    $headers = @{ Authorization = 'Bearer ' + $token; Accept = 'application/vnd.github+json'; 'User-Agent' = 'Internet-Monitor-Release'; 'X-GitHub-Api-Version' = '2026-03-10' }
    $api = 'https://api.github.com/repos/' + $Repository
    if (!$TargetCommit) { $TargetCommit = (Invoke-RestMethod -Uri ($api + '/commits/main') -Headers $headers).sha }
    if ($TargetCommit -notmatch '^[0-9a-f]{40}$') { throw 'TargetCommit must be a full commit SHA.' }
    # Ensure the source commit contains the exact tested installer being published.
    $committed = Invoke-RestMethod -Uri ($api + '/contents/artifacts/' + $assetName + '?ref=' + $TargetCommit) -Headers $headers
    if ($committed.encoding -ne 'base64') { throw 'Cannot verify the committed installer content.' }
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try { $committedHash = [BitConverter]::ToString($sha256.ComputeHash([Convert]::FromBase64String($committed.content))).Replace('-', '').ToLowerInvariant() }
    finally { $sha256.Dispose() }
    if ($hash -ne $committedHash) { throw 'Push the verified installer to the source commit before publishing.' }

    $release = $null
    try { $release = Invoke-RestMethod -Uri ($api + '/releases/tags/' + $tag) -Headers $headers }
    catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
    # A draft may not yet have a Git tag. Reuse it after an interrupted upload.
    if (!$release) {
        $page = 1
        do {
            $releases = @(Invoke-RestMethod -Uri ($api + '/releases?per_page=100&page=' + $page) -Headers $headers)
            $release = $releases | Where-Object { $_.tag_name -eq $tag } | Select-Object -First 1
            $page++
        } while (!$release -and $releases.Count -eq 100)
    }
    if ($VerifyOnly) {
        [pscustomobject]@{ Version = $version; Commit = $TargetCommit; InstallerBytes = $size; SHA256 = $hash; ExistingRelease = ($null -ne $release); ReleaseUrl = $release.html_url }
        return
    }
    if (!$release) {
        $body = @{ tag_name = $tag; target_commitish = $TargetCommit; name = 'Internet Monitor ' + $version; body = $notes; draft = $true; prerelease = $false } | ConvertTo-Json
        $release = Invoke-RestMethod -Method Post -Uri ($api + '/releases') -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    }
    $asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if (!$asset) {
        $uploadUrl = ($release.upload_url -split '\{')[0] + '?name=' + [Uri]::EscapeDataString($assetName)
        $asset = Invoke-RestMethod -Method Post -Uri $uploadUrl -Headers $headers -ContentType 'application/octet-stream' -InFile $installer
    }
    if ($asset.state -ne 'uploaded' -or [long]$asset.size -ne $size -or $asset.digest -ne ('sha256:' + $hash)) {
        throw 'Release asset differs from the verified installer. Publish a new version rather than replacing a published asset.'
    }
    if ($release.draft) {
        $body = @{ draft = $false; make_latest = 'true'; name = 'Internet Monitor ' + $version; body = $notes } | ConvertTo-Json
        $release = Invoke-RestMethod -Method Patch -Uri ($api + '/releases/' + $release.id) -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    }
    $published = Invoke-RestMethod -Uri ($api + '/releases/tags/' + $tag) -Headers $headers
    $verifiedAsset = $published.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if ($published.draft -or $published.prerelease -or $verifiedAsset.state -ne 'uploaded' -or [long]$verifiedAsset.size -ne $size -or $verifiedAsset.digest -ne ('sha256:' + $hash)) { throw 'Published release verification failed.' }
    [pscustomobject]@{ Version = $version; ReleaseUrl = $published.html_url; DownloadUrl = $verifiedAsset.browser_download_url; InstallerBytes = $size; SHA256 = $hash }
}
finally {
    $env:GCM_INTERACTIVE = $previousInteractive; $env:GIT_TERMINAL_PROMPT = $previousPrompt
    $token = $null; $headers = $null; $credential = $null; $passwordLine = $null
}
