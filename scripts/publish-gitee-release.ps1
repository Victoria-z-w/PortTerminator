param(
    [string]$Version = "1.0.0",
    [string]$AccessToken = $env:GITEE_TOKEN,
    [string]$Owner = "hherr54tge3tg",
    [string]$Repo = "port-terminator",
    [string]$TargetBranch = "master"
)

$ErrorActionPreference = "Stop"

if (-not $AccessToken) {
    throw @"
Gitee 私人令牌未设置。

请先在 Gitee 生成令牌：
https://gitee.com/profile/personal_access_tokens

然后执行：
`$env:GITEE_TOKEN = '你的令牌'
.\scripts\publish-gitee-release.ps1 -Version $Version
"@
}

$tag = "v$Version"
$installer = Join-Path (Split-Path $Parent $PSScriptRoot) "dist\PortTerminator-Setup-$Version.exe"

if (-not (Test-Path $installer)) {
    throw "安装包不存在，请先运行: .\scripts\build-installer.ps1 -Version $Version"
}

Write-Host "==> 创建 Gitee Release: $tag" -ForegroundColor Cyan

$releaseParams = @{
    access_token     = $AccessToken
    tag_name         = $tag
    name             = "Port Terminator $Version"
    body             = "Windows 端口管理工具 v$Version`n`n下载 PortTerminator-Setup-$Version.exe 安装即可使用，无需单独安装 .NET 运行时。"
    target_commitish = $TargetBranch
}

$existing = $null
try {
    $existing = Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/$Owner/$Repo/releases/tags/$tag?access_token=$AccessToken" -Method Get
} catch {
    $existing = $null
}

if ($existing -and $existing.id) {
    $releaseId = $existing.id
    Write-Host "Release 已存在 (id: $releaseId)，直接上传安装包" -ForegroundColor Yellow
} else {
    $release = Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/$Owner/$Repo/releases" -Method Post -Body $releaseParams
    $releaseId = $release.id
    Write-Host "已创建 Release (id: $releaseId)" -ForegroundColor Green
}

Write-Host "==> 上传安装包" -ForegroundColor Cyan
$uploadUri = "https://gitee.com/api/v5/repos/$Owner/$Repo/releases/$releaseId/attach_files?access_token=$AccessToken"
& curl.exe -sS -X POST $uploadUri -F "file=@$installer" | Out-Null

$final = Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/$Owner/$Repo/releases/tags/$tag?access_token=$AccessToken" -Method Get
Write-Host ""
Write-Host "完成!" -ForegroundColor Green
Write-Host "Release: $($final.html_url)" -ForegroundColor Green
if ($final.assets) {
    $final.assets | ForEach-Object {
        Write-Host "附件: $($_.name)" -ForegroundColor Green
    }
}
