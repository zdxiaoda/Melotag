# 把多个架构的 MSIX 合并为一个 .msixbundle（Microsoft Store 上传用）。
# 用法：
#   ./packaging/windows/msix/build-msix-bundle.ps1 `
#     -Version 1.4.0 `
#     -PackageDir "dist/msix/win-x64", "dist/msix/win-arm64"
param(
  [Parameter(Mandatory = $true)] [string] $Version,
  [Parameter(Mandatory = $true)] [string[]] $PackageDir,
  [string] $Output = ""
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($Output)) {
  $Output = Join-Path $repo "dist\msix-bundle\Melotag-$Version.msixbundle"
} elseif (![System.IO.Path]::IsPathRooted($Output)) {
  $Output = Join-Path $repo $Output
}

# makeappx bundle 要求所有包在同一目录里，且不能有子目录。
# 注意：不要用 $input，它是 PowerShell 的自动变量，在 ForEach-Object 里会被覆盖。
$bundleInput = Join-Path $repo 'dist\msix-bundle-input'
Remove-Item -Recurse -Force $bundleInput -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $bundleInput | Out-Null

foreach ($dir in $PackageDir) {
  $path = if ([System.IO.Path]::IsPathRooted($dir)) { $dir } else { Join-Path $repo $dir }
  if (!(Test-Path $path)) { throw "找不到 MSIX 目录：$path" }
  Get-ChildItem $path -Filter *.msix -File | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $bundleInput $_.Name) -Force
  }
}

$packages = @(Get-ChildItem $bundleInput -Filter *.msix -File)
if ($packages.Count -lt 2) {
  throw "至少需要两个架构的 MSIX（当前 $($packages.Count) 个）：$bundleInput"
}

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
$bundleArgs = @('bundle', '/d', $bundleInput, '/p', $Output, '/o')
& $makeappx @bundleArgs
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx bundle 打包失败' }

Write-Host "MSIX Bundle 已生成：$Output"
Write-Host "包含：$($packages.Name -join ', ')"
