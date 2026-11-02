# 构建 Microsoft Store 用的 MSIX。
# 用法：
#   ./packaging/windows/msix/build-msix.ps1 `
#     -Version 1.2.1.0 `
#     -IdentityName "PartnerCenter 的 Package/Identity/Name" `
#     -Publisher "PartnerCenter 的 Package/Identity/Publisher" `
#     -PublisherDisplayName "PartnerCenter 的 PublisherDisplayName"
param(
  [Parameter(Mandatory = $true)] [string] $Version,
  [Parameter(Mandatory = $true)] [string] $IdentityName,
  [Parameter(Mandatory = $true)] [string] $Publisher,
  [Parameter(Mandatory = $true)] [string] $PublisherDisplayName,
  [string] $SourceDir = "dist/win-x64-msix",
  [string] $Output = ""
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$source = if ([System.IO.Path]::IsPathRooted($SourceDir)) { $SourceDir } else { Join-Path $repo $SourceDir }
if (!(Test-Path $source)) {
  throw "找不到 MSIX 源目录：$source。请先执行 dotnet publish（--self-contained true，PublishSingleFile=false）。"
}

if ([string]::IsNullOrWhiteSpace($Output)) {
  $Output = Join-Path $repo "dist\installer-win-x64\Melotag-$Version.msix"
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
Set-Content -Path (Join-Path $stage 'AppxManifest.xml') -Value $manifest -Encoding UTF8

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -like '*\x64\makeappx.exe' } |
  Sort-Object FullName -Descending |
  Select-Object -First 1 -ExpandProperty FullName
if (-not $makeappx) {
  throw '未找到 makeappx.exe，请安装 Windows 10/11 SDK。'
}

New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
& $makeappx pack /d $stage /p $Output /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx 打包失败' }

Write-Host "MSIX 已生成：$Output"
Write-Host "上传到 Partner Center 后由 Microsoft 签名；本地测试请先用自签名证书签名。"
