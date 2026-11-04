# Microsoft Store 上架指南

Microsoft Store 不接受 Inno Setup 的 `.exe` 安装包，需要 **MSIX**。仓库已经准备了 MSIX 清单模板和打包脚本，你只需要填入 Partner Center 给你的产品标识。

## 1. 在 Partner Center 创建应用

1. 登录 [Microsoft Partner Center](https://partner.microsoft.com/dashboard)。
2. `Apps and features` → `New product` → 选择 **MSIX or PWA app**。
3. 为应用预留名称，例如 **音签 Melotag**。
4. 进入产品的 `Product identity`（产品标识）页面，记下三个值：
   - `Package/Identity/Name`（例如 `12345zdxiaoda.Melotag`）
   - `Package/Identity/Publisher`（例如 `CN=XXXXXXXX-....`）
   - `Package/Properties/PublisherDisplayName`（例如 `zdxiaoda`）

> 这三个值必须和 MSIX 里的 `Identity` 完全一致，否则 Partner Center 会拒绝上传。

## 2. 配置 GitHub 仓库 Variables 或 Secrets

在 GitHub 仓库：

`Settings → Secrets and variables → Actions` → 选择 **Variables** 或 **Secrets**。

添加：

| 名称 | 值 |
| --- | --- |
| `MSIX_IDENTITY_NAME` | Partner Center 的 `Package/Identity/Name` |
| `MSIX_PUBLISHER` | Partner Center 的 `Package/Identity/Publisher` |
| `MSIX_PUBLISHER_DISPLAY_NAME` 或 `PUBLISHERDISPLAYNAME` | Partner Center 的 `PublisherDisplayName` |

当前仓库使用 Secrets，显示名 Secret 名为 `PUBLISHERDISPLAYNAME`。

配置后，`release` workflow 会生成：

- `Melotag-<版本>.msixbundle`（内含 x64 与 arm64 两个架构的包）
- artifact：`msix-bundle`

没有配置这三个标识时，MSIX 步骤会自动跳过，不影响其他安装包。

## 3. 本地构建 MSIX（可选）

先为两个架构各做一次非单文件的 self-contained 发布：

```powershell
foreach ($arch in @('x64', 'arm64')) {
  dotnet publish src/Melotag.Desktop `
    -c Release -r "win-$arch" --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None -p:DebugSymbols=false `
    -p:Version=1.4.1 `
    -o "dist/win-$arch-msix"
}
```

然后逐架构打包，再合并为 bundle：

```powershell
./packaging/windows/msix/build-msix.ps1 `
  -Version 1.4.1.0 `
  -Architecture x64 `
  -IdentityName "Partner Center 的 Package/Identity/Name" `
  -Publisher "Partner Center 的 Package/Identity/Publisher" `
  -PublisherDisplayName "Partner Center 的 PublisherDisplayName" `
  -SourceDir "dist/win-x64-msix" `
  -Output "dist/msix/win-x64/Melotag-1.4.1-win-x64.msix"

./packaging/windows/msix/build-msix.ps1 `
  -Version 1.4.1.0 `
  -Architecture arm64 `
  -IdentityName "Partner Center 的 Package/Identity/Name" `
  -Publisher "Partner Center 的 Package/Identity/Publisher" `
  -PublisherDisplayName "Partner Center 的 PublisherDisplayName" `
  -SourceDir "dist/win-arm64-msix" `
  -Output "dist/msix/win-arm64/Melotag-1.4.1-win-arm64.msix"

./packaging/windows/msix/build-msix-bundle.ps1 `
  -Version 1.4.1 `
  -PackageDir "dist/msix/win-x64", "dist/msix/win-arm64" `
  -Output "dist/msix-bundle/Melotag-1.4.1.msixbundle"
```

脚本会：

1. 用 `tools/Melotag.Branding` 生成 44/50/71/150/310/310×150 的 Store 图标；
2. 替换 `Package.appxmanifest` 中的三个产品标识、版本号与 `ProcessorArchitecture`；
3. 调用 Windows SDK 的 `makeappx.exe` 生成 `.msix`；`build-msix-bundle.ps1` 再用 `makeappx bundle` 合并。

需要安装 Windows 10/11 SDK（GitHub 的 `windows-latest` 已自带）。

## 4. 上传到 Partner Center

1. Partner Center → 你的应用 → `Start submission`。
2. **Packages**：上传生成的 `.msixbundle`。
   - Partner Center 会先做包校验；如果 Identity 不匹配，会直接报错。
   - Store 会重新签名，所以你不需要准备商店证书。
3. **Properties**：
   - Category：`Music`
   - Privacy policy URL：如果你收集任何信息（本应用主要是本地设置与用户主动发起的在线请求），建议提供隐私政策页面。
   - Support contact：你的邮箱或仓库 Issues 地址。
4. **Age ratings**：完成问卷。
5. **Store listings**：
   - 描述、短描述、关键词
   - 应用图标（300×300）
   - 截图（至少 1 张，建议 1366×768 或 1920×1080）
   - 支持的平台/系统要求（x64 与 arm64）
6. **Pricing and availability**：免费、选择市场。
7. 点击 `Submit for certification`。

## 5. 本地测试安装（可选）

商店提交不要求本地签名，但如果你要自己安装测试，需要给 MSIX 签一个自签名证书。注意：**测试包的清单 Publisher 必须和测试证书的 Subject 一致**，不要把正式提交的清单改掉。

```powershell
# 1) 生成测试证书（只用于本机测试）
$cert = New-SelfSignedCertificate -Type Custom `
  -Subject "CN=Melotag Test" `
  -KeyUsage DigitalSignature `
  -FriendlyName "Melotag Test" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

# 2) 用测试 Publisher 重新打一个测试包（复制清单，改 Publisher 为 CN=Melotag Test）
#    然后签名
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" `
  -FilePath "melotag-test.pfx" -Password (ConvertTo-SecureString "test" -AsPlainText -Force)
signtool sign /fd SHA256 /f melotag-test.pfx /p test dist\installer-win-x64\Melotag-1.4.1.0-win-x64.msix

# 3) 导出并信任测试证书后安装
Export-Certificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath "melotag-test.cer"
Import-Certificate -FilePath "melotag-test.cer" -CertStoreLocation "Cert:\LocalMachine\TrustedPeople"
Add-AppxPackage dist\installer-win-x64\Melotag-1.4.1.0-win-x64.msix
```

正式提交时不要使用自签名证书；Partner Center 会为商店版本签名。若只是快速侧载，也可以在开发者模式下尝试 `Add-AppxPackage -AllowUnsigned`（不同 Windows 版本行为可能不同）。

## 6. 更新版本

每次提交新版本时：

- MSIX `Version` 必须是四段数字，并且比已发布版本大，例如：
  - 首次：`1.2.1.0`
  - 修复：`1.2.2.0`
- 重新生成两个架构的 `.msix` 并合并成 `.msixbundle`，上传到新的 submission。
- 不要修改 `Identity Name` / `Publisher`，否则会被当成另一个应用。

## 7. 常见问题

| 问题 | 处理 |
| --- | --- |
| `Package identity does not match` | 检查 `MSIX_IDENTITY_NAME` / `MSIX_PUBLISHER` 是否完全复制自 Partner Center |
| `Version must be greater` | 把 MSIX 版本改成 `1.2.2.0` 这类比线上更大的四段版本 |
| 上传后图标缺失 | 确认 `Assets/` 下有 44/50/71/150/310/310×150 的 PNG |
| 文件关联被拒 | `Package.appxmanifest` 里声明了音频扩展名；如 Store 认为 `.mp4` 冲突，可先移除该扩展名再提交 |
| 应用启动失败 | MSIX 是 full-trust 桌面应用，必须保留 `runFullTrust` 能力，并确保发布目录里 `Melotag.exe` 在包根目录 |
| 需要单独测试 arm64 | 用 `-Architecture arm64 -SourceDir dist/win-arm64-msix` 生成单架构包，或直接侧载 `.msixbundle` |

## 8. 相关文件

- `packaging/windows/msix/Package.appxmanifest`：MSIX 清单模板
- `packaging/windows/msix/build-msix.ps1`：构建单架构 MSIX
- `packaging/windows/msix/build-msix-bundle.ps1`：合并 x64 + arm64 为 `.msixbundle`
- `tools/Melotag.Branding`：生成应用图标与 MSIX 图标
- `.github/workflows/release.yml`：CI 自动生成 `.msixbundle` 的逻辑
