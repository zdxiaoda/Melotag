# 构建 Microsoft Store 用的 MSIX（单个架构）。
# 用法：
#   ./packaging/windows/msix/build-msix.ps1 `
#     -Version 1.2.1.0 `
#     -Architecture x64 `
#     -IdentityName "Partner Center 的 Package/Identity/Name" `
#     -Publisher "Partner Center 的 Package/Identity/Publisher" `
#     -PublisherDisplayName "Partner Center 的 PublisherDisplayName"
#
# 两个架构的包由 build-msix-bundle.ps1 合并成 .msixbundle 后上传商店。
param(
  [Parameter(Mandatory = $true)] [string] $Version,
  [Parameter(Mandatory = $true)] [string] $IdentityName,
  [Parameter(Mandatory = $true)] [string] $Publisher,
  [Parameter(Mandatory = $true)] [string] $PublisherDisplayName,
  [ValidateSet('x64', 'arm64')] [string] $Architecture = 'x64',
  [string] $SourceDir = "",
  [string] $Output = ""
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($SourceDir)) {
  $SourceDir = "dist/win-$Architecture-msix"
}
$source = if ([System.IO.Path]::IsPathRooted($SourceDir)) { $SourceDir } else { Join-Path $repo $SourceDir }
if (!(Test-Path $source)) {
  throw "找不到 MSIX 源目录：$source。请先执行 dotnet publish（--self-contained true，PublishSingleFile=false，-r win-$Architecture）。"
}

if ([string]::IsNullOrWhiteSpace($Output)) {
  $Output = Join-Path $repo "dist\installer-win-$Architecture\Melotag-$Version-win-$Architecture.msix"
} elseif (![System.IO.Path]::IsPathRooted($Output)) {
  $Output = Join-Path $repo $Output
}

$stage = Join-Path $repo 'dist\msix-stage'
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -Recurse -Force (Join-Path $source '*') $stage

# 生成 Store 所需的图标尺寸（44 / 50 / 71 / 150 / 310 / 310x150）
& dotnet run --project (Join-Path $repo 'tools\Melotag.Branding') -- msix (Join-Path $stage 'Assets')
if ($LASTEXITCODE -ne 0) { throw '生成 MSIX 图标失败' }

$manifest = Get-Content (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw
$manifest = $manifest.Replace('__IDENTITY_NAME__', [System.Security.SecurityElement]::Escape($IdentityName))
$manifest = $manifest.Replace('__PUBLISHER__', [System.Security.SecurityElement]::Escape($Publisher))
$manifest = $manifest.Replace('__PUBLISHER_DISPLAY_NAME__', [System.Security.SecurityElement]::Escape($PublisherDisplayName))
$manifest = $manifest.Replace('__VERSION__', $Version)
$manifest = $manifest.Replace('__ARCH__', $Architecture)
Set-Content -Path (Join-Path $stage 'AppxManifest.xml') -Value $manifest -Encoding UTF8

# 在 ARM64 主机上找 arm64 版的 SDK 工具，x64 主机上仍用 x64。
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -like "*\$hostArch\makeappx.exe" } |
  Sort-Object FullName -Descending |
  Select-Object -First 1 -ExpandProperty FullName
if (-not $makeappx) {
  $makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
}
if (-not $makeappx) {
  throw '未找到 makeappx.exe，请安装 Windows 10/11 SDK。'
}

New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
& $makeappx pack /d $stage /p $Output /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx 打包失败' }

Write-Host "MSIX 已生成：$Output"
Write-Host "上传到 Partner Center 后由 Microsoft 签名；本地测试请先用自签名证书签名。"
