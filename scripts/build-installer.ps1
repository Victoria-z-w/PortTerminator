param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish\$Runtime"
$distDir = Join-Path $root "dist"
$uiProject = Join-Path $root "src\PortTerminator.UI\PortTerminator.UI.csproj"
$setupScript = Join-Path $root "installer\setup.iss"

Write-Host "==> Publishing Port Terminator v$Version ($Runtime)" -ForegroundColor Cyan

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

& dotnet publish $uiProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$elevatedExe = Join-Path $publishDir "PortTerminator.Elevated.exe"
if (-not (Test-Path $elevatedExe)) {
    throw "Missing PortTerminator.Elevated.exe in publish output"
}

Write-Host "==> Building installer" -ForegroundColor Cyan

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "E:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "D:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw @"
Inno Setup 6 not found.

Install from: https://jrsoftware.org/isinfo.php
Or on GitHub Actions, the release workflow installs it automatically.
"@
}

& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" $setupScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$installer = Get-ChildItem $distDir -Filter "PortTerminator-Setup-$Version.exe" | Select-Object -First 1
if (-not $installer) {
    throw "Installer not found in $distDir"
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "Installer: $($installer.FullName)" -ForegroundColor Green
Write-Host "Size: $([math]::Round($installer.Length / 1MB, 2)) MB" -ForegroundColor Green
