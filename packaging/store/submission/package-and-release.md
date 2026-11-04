# MSIX 包与发布前检查

适用版本：应用 `1.4.1`；MSIX 包版本 `1.4.1.0`；整理日期：2026-11-04。

> 本文只记录发布前的标识、路径、包内容和风险检查项，不表示应用已经提交、通过认证、发布或获得评级。任何一项如果需要改代码、清单或打包参数，都应单独评审、重新构建、重新测试并重新上传，不能只改文件名或只改 listing。

## 1. Partner Center 三个产品标识

Partner Center 的“产品标识”页会给出三个值。仓库工作流优先读取 Variables，其次读取 Secrets：

| 配置名 | GitHub 配置 | MSIX 清单字段 | 说明 |
| --- | --- | --- | --- |
| `MSIX_IDENTITY_NAME` | Variable 或 Secret | `Identity/@Name` | 对应 Partner Center 的 `Package/Identity/Name` |
| `MSIX_PUBLISHER` | Variable 或 Secret | `Identity/@Publisher` | 对应 `Package/Identity/Publisher`，通常为证书主题形式的完整字符串 |
| `MSIX_PUBLISHER_DISPLAY_NAME` | Variable 或 Secret | `Properties/PublisherDisplayName` | 对应 `PublisherDisplayName`；工作流还兼容 Secret 名 `PUBLISHERDISPLAYNAME` |

工作流中的实际取值顺序：

- `MSIX_IDENTITY_NAME`：`vars.MSIX_IDENTITY_NAME` → `secrets.MSIX_IDENTITY_NAME`
- `MSIX_PUBLISHER`：`vars.MSIX_PUBLISHER` → `secrets.MSIX_PUBLISHER`
- `MSIX_PUBLISHER_DISPLAY_NAME`：`vars.MSIX_PUBLISHER_DISPLAY_NAME` → `secrets.MSIX_PUBLISHER_DISPLAY_NAME` → `secrets.PUBLISHERDISPLAYNAME`

提交前应把三个占位符替换为 Partner Center 的实际值，而不是保留占位符：

```text
MSIX_IDENTITY_NAME=<Package/Identity/Name>
MSIX_PUBLISHER=<Package/Identity/Publisher>
MSIX_PUBLISHER_DISPLAY_NAME=<PublisherDisplayName>
```

检查点：

- [ ] 三个值都已在 GitHub 仓库的 Actions Variables 或 Secrets 中配置；任一为空时，`release.yml` 会跳过 MSIX 构建和 `msix-bundle` 上传。
- [ ] 三个值与 Partner Center 产品标识逐字符一致，包括大小写、空格和证书主题中的逗号/等号顺序。
- [ ] 不要把值直接写进 `Package.appxmanifest`；清单中的 `__IDENTITY_NAME__`、`__PUBLISHER__`、`__PUBLISHER_DISPLAY_NAME__`、`__ARCH__` 由构建脚本替换。
- [ ] 不要把 Publisher 证书、密码或 Partner Center 凭据提交到仓库；本文只涉及产品标识，不涉及签名证书。

本地构建命令（在 Windows 上，需 Windows 10/11 SDK 的 `makeappx.exe`）：先为两个架构各做一次自包含发布（`-p:PublishSingleFile=false`，MSIX 需要展开的应用文件），再逐架构打包并合并：

```powershell
foreach ($arch in @('x64', 'arm64')) {
  dotnet publish src/Melotag.Desktop `
    -c Release -r "win-$arch" --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None -p:DebugSymbols=false `
    -p:Version=1.4.1 `
    -o "dist/win-$arch-msix"
}

./packaging/windows/msix/build-msix.ps1 `
  -Version 1.4.1.0 `
  -Architecture x64 `
  -IdentityName "<Package/Identity/Name>" `
  -Publisher "<Package/Identity/Publisher>" `
  -PublisherDisplayName "<PublisherDisplayName>" `
  -SourceDir dist/win-x64-msix `
  -Output dist/msix/win-x64/Melotag-1.4.1-win-x64.msix

./packaging/windows/msix/build-msix.ps1 `
  -Version 1.4.1.0 `
  -Architecture arm64 `
  -IdentityName "<Package/Identity/Name>" `
  -Publisher "<Package/Identity/Publisher>" `
  -PublisherDisplayName "<PublisherDisplayName>" `
  -SourceDir dist/win-arm64-msix `
  -Output dist/msix/win-arm64/Melotag-1.4.1-win-arm64.msix

./packaging/windows/msix/build-msix-bundle.ps1 `
  -Version 1.4.1 `
  -PackageDir dist/msix/win-x64, dist/msix/win-arm64 `
  -Output dist/msix-bundle/Melotag-1.4.1.msixbundle
```

## 2. MSIX 路径与版本

| 产物 | 路径 | 说明 |
| --- | --- | --- |
| MSIX 发布源目录 | `dist/win-{x64,arm64}-msix` | self-contained、`PublishSingleFile=false` 的展开文件 |
| 单架构 MSIX | `dist/msix/win-<arch>/Melotag-1.4.1-win-<arch>.msix` | `release.yml` 显式传入 `-Output`（与本地示例一致） |
| 本地默认输出（省略 `-Output`） | `dist/installer-win-<arch>/Melotag-1.4.1.0-win-<arch>.msix` | `build-msix.ps1` 默认用 `-Architecture`/`-Version` 拼路径 |
| MSIX Bundle | `dist/msix-bundle/Melotag-1.4.1.msixbundle` | `build-msix-bundle.ps1` 合并两个架构 |
| GitHub Actions artifact | `msix-bundle` | 仅当三项产品标识都配置时生成 |
| GitHub Release 资产（如发布） | `Melotag-1.4.1.msixbundle` | 发布工作流把 `msix-bundle` 复制到 `out/` 后上传 |

注意：文件名中的 `1.4.1` 或 `1.4.1.0` 不代表包内版本；包内版本以 `AppxManifest.xml` 的 `Identity/@Version` 为准，应为四段数字 `1.4.1.0`。Partner Center 上传的是 `.msixbundle`；本地安装测试需要自签名证书，Store 包由 Microsoft 在认证/发布流程中签名。

其他包清单事实：

- `Identity/@ProcessorArchitecture` 由 `-Architecture` 写入：x64 包为 `x64`，arm64 包为 `arm64`；两个包版本一致，合并为 `.msixbundle` 后同时覆盖两种设备。
- `TargetDeviceFamily` 为 `Windows.Desktop`，`MinVersion` 为 `10.0.19041.0`，`MaxVersionTested` 为 `10.0.26100.0`。
- `Application/@EntryPoint` 为 `Windows.FullTrustApplication`，`Executable` 为 `Melotag.exe`。
- `Capabilities` 只包含 `runFullTrust` 和 `internetClient`。
- 最终 `AppxManifest.xml` 中不得残留 `__IDENTITY_NAME__`、`__PUBLISHER__`、`__PUBLISHER_DISPLAY_NAME__`、`__VERSION__`、`__ARCH__`。

## 3. 版本规则

- 应用版本：`Directory.Build.props` 的 `Version` 为 `1.4.1`；桌面端、Android 显示版本与 User-Agent 都由程序集版本派生。
- Android：`ApplicationVersion`（versionCode）每次发布递增；它不用于 MSIX。
- MSIX：`Identity/@Version` 必须是四段纯数字，例如 `1.4.1.0`。`release.yml` 会把三段版本补齐为四段；`build-msix.ps1` 直接使用 `-Version` 参数，因此本地构建也要显式传 `1.4.1.0`。
- Store 版本必须高于已发布版本，且不能重复使用已发布的版本号；后续提交按实际发布递增。
- 如果认证要求重新提交，按 Partner Center 的要求处理版本号（可能需要递增）并重新打包；不要只改输出文件名。
- 不要用三段版本（如 `1.4.1`）生成最终 MSIX；四段数字（如 `1.4.1.0`）是 Store 包的必要格式。

## 4. 包内图标与 listing 300×300 图标的区别

| 项目 | 路径/尺寸 | 由谁生成 | 用途 |
| --- | --- | --- | --- |
| MSIX `StoreLogo.png` | `Assets\StoreLogo.png`，50×50 | `build-msix.ps1` 调用 `dotnet run --project tools/Melotag.Branding -- msix <stage>/Assets` | 包清单 `Properties/Logo`，包内商店图标 |
| MSIX 方形图标 | `Square44x44Logo.png`、`Square71x71Logo.png`、`Square150x150Logo.png`、`Square310x310Logo.png` | 同上 | 开始菜单、磁贴和包内视觉元素 |
| MSIX 宽图 | `Wide310x150Logo.png`，310×150 | 同上 | `uap:DefaultTile` |
| Listing 商店图标 | `packaging/store/images/logo/app-tile-300x300.png`，300×300 | `uv run --offline --with pillow python packaging/store/tools/Generate-StoreArt.py --only tile` | Partner Center listing 的 Store logo |

检查点：

- [ ] 包内 `Properties/Logo` 指向 `Assets\StoreLogo.png`，且该文件存在。
- [ ] `uap:VisualElements` 的 `Square150x150Logo`、`Square44x44Logo` 和 `uap:DefaultTile` 引用的文件都存在。
- [ ] 不要把 300×300 的 listing 图标直接改名成 `StoreLogo.png`；包清单需要的是 50×50 的 StoreLogo，其他尺寸也有独立文件名。
- [ ] 不要因为 listing 图标已上传就删除包内图标；两者是独立上传物。
- [ ] 在浅色/深色背景和 44×44 小尺寸下检查包内图标可辨识度。
- [ ] 不要把 `tools/` 或商店 listing 图片打进 MSIX 包。

## 5. `.mp4` 文件关联复核

`Package.appxmanifest` 的 `uap:FileTypeAssociation Name="audio"` 当前包含：

```text
.mp3 .flac .ape .wv .tta .mpc .ogg .opus .m4a .mp4
.wav .aif .aiff .wma .dsf .dff
```

应用是标签编辑器，不是媒体播放器；`src/Melotag.Tags/TagLibTagStore.cs` 的支持扩展名包含 `.mp4`，但不会播放音视频。复核点：

- [ ] 确认 `.mp4` 是否确实要作为“元数据编辑”关联。若产品不打算支持视频容器，发布前从清单移除 `.mp4`；这是代码/包变更，需重新构建和测试。
- [ ] 若保留 `.mp4`，确认 listing 和认证备注明确说明应用不是播放器，双击 `.mp4` 打开的是标签编辑器。
- [ ] 用单轨 `.mp4`、多轨 `.mp4` 和不支持的视频文件各测一次：应能打开/给出可理解状态，不应崩溃或静默破坏文件。
- [ ] 检查 `.m4a`（音频）与 `.mp4`（可能是视频）在文件关联 UI 中的显示是否合理；两者共用同一个 `FileTypeAssociation`。
- [ ] 检查双击关联文件时应用是否加载文件而不是只启动空窗口。
- [ ] 如果移除 `.mp4`，同步检查 listing 的“支持格式”描述，避免宣传未关联的格式。

## 6. 包清单硬编码中文

`Package.appxmanifest` 当前包含以下硬编码中文字符串：

- `Properties/DisplayName`：`音签 Melotag`
- `uap:VisualElements/@DisplayName`：`音签 Melotag`
- `uap:VisualElements/@Description`：`跨平台音乐标签编辑器`
- `uap:FileTypeAssociation/uap:DisplayName`：`音频文件`

清单声明了 `zh-CN`、`en-US`、`ja-JP` 三个 `Resource`，但这些显示名没有使用 `ms-resource:` 资源引用，因此英文/日文系统上的开始菜单、文件关联和部分 Shell 界面仍可能显示中文。发布前检查：

- [ ] 确认“包内显示名固定为中文”是否可以接受；如果要求三语本地化，需要把字符串迁到 `.resw`/PRI 资源并使用 `ms-resource:` 引用，这是代码/包变更。
- [ ] 确认 `Description` 中的“跨平台”是否适合 Windows Store 包；Store listing 只面向 Windows，若担心误导，改为中性的“音乐标签编辑器”需要重新打包。
- [ ] 检查 `Melotag.Desktop.csproj` 的 `Product`/`AssemblyTitle`/`Description` 也有中文（`音签 Melotag`、`跨平台音乐标签编辑器`）；它们进入 EXE 版本信息，若做本地化需一并评估。
- [ ] 确认 `PublisherDisplayName` 已被注入，不是 `__PUBLISHER_DISPLAY_NAME__`。
- [ ] 在英文/日文 Windows 上安装一次，观察开始菜单、文件关联和“应用和功能”中的显示名，再决定是否接受当前硬编码。
- [ ] 不要在认证备注中声称包内 Shell 显示名已经完全本地化；当前清单并非如此。

## 7. GitHub 更新检查与酷我 HTTP 等上线风险

### 7.1 GitHub Releases 更新检查

代码事实：

- `src/Melotag.Core/ProductInfo.cs`：`ReleasesApi = "https://api.github.com/repos/zdxiaoda/Melotag/releases/latest"`。
- `src/Melotag.Core/ProductInfo.cs`：默认 `UserAgent` 指向 GitHub 仓库。
- `src/Melotag.Core/Services/UpdateService.cs`：请求 GitHub Releases，比较版本；网络或限流失败时静默返回 `null`。
- `src/Melotag.UI/Views/UpdateUi.cs`：启动时静默检查，发现新版本只在状态栏提示；手动检查显示对话框，可打开 Release 页面；不下载、不安装、不替换文件。
- 设置中可关闭“启动时检查更新”；默认开启。

风险/检查点：

- [ ] 隐私政策和认证备注已披露启动时的 GitHub 请求；请求包含 IP、User-Agent 等常规信息。
- [ ] 向审核说明这不是自更新机制：不下载、不安装，只显示提示和外部链接；避免被误判为绕过 Store 更新。
- [ ] 确认 Store 版本 `1.4.1` 与 GitHub Release 标签的比较结果符合预期；`UpdateService` 会去掉 `v` 前缀和预发布后缀。
- [ ] 离线/被墙/GitHub 限流时不应阻塞启动或崩溃；当前实现是静默失败，但发布前应实测。
- [ ] 如果 Store 政策或认证要求 Store 包不进行启动外部检查，需要单独改代码（例如默认关闭或按包类型禁用）；本文不修改源码。
- [ ] 不要在任何商店文案中宣称“完全离线”或“无网络请求”。

### 7.2 酷我搜索使用明文 HTTP

代码事实：

- `src/Melotag.Core/Sources/ChineseSources.cs` 的 `KuwoSource` 使用 `http://search.kuwo.cn/r.s` 进行搜索。
- 封面地址使用 `https://img1.kuwo.cn/...`。
- 请求只包含搜索文本、常规请求头和 User-Agent；不发送账号、密码或音频文件。
- 当前酷我歌词接口返回 301，`KuwoSource` 的能力为标签和封面，不提供歌词。

风险/检查点：

- [ ] 明文 HTTP 的搜索词和响应可被网络中间人观察或篡改；Store 安全/隐私审核可能标记。
- [ ] 发布前测试酷我搜索接口是否有可用的 HTTPS 地址；若有，改为 HTTPS 属于代码变更，需要重新构建和测试。
- [ ] 若没有 HTTPS，评估是否在 Store 版本禁用酷我源，或在隐私政策/认证备注中明确说明该源使用明文 HTTP。
- [ ] 确认该请求不携带 Last.fm key、账号信息或本地路径。
- [ ] 如果保留明文 HTTP，至少在发布说明/隐私政策中不要让用户误以为所有在线请求都加密。

### 7.3 其他相关风险

- 其他在线源当前使用 HTTPS：网易云音乐、QQ 音乐、酷狗、MusicBrainz、Cover Art Archive、iTunes Search API、Last.fm、LRCLIB；仍需在发布前确认域名和证书有效。
- 如果用户填写 Last.fm API key，它会以明文保存在本机 `settings.json`，并在使用 Last.fm 源时发送给 Last.fm；隐私政策已披露。不要把这一点写成“无任何凭据”。
- 在线服务可能变更、限流或停止返回；应用应继续允许离线编辑，不要把在线查询作为保存/重命名前置条件。
- 启动更新检查和在线查询都属于网络行为；不要在任何商店文案中宣称“完全离线”或“无网络请求”。

## 8. 发布前检查清单

- [ ] `Directory.Build.props` 的版本为当前版本；MSIX 为对应的四段数字。
- [ ] `dotnet test Melotag.slnx` 通过。
- [ ] `dotnet build Melotag.slnx -c Release` 通过。
- [ ] 用 `PublishSingleFile=false` 为 x64 与 arm64 各生成 `dist/win-<arch>-msix`。
- [ ] 用三项产品标识生成两个架构的 MSIX，并合并为 `.msixbundle`；检查 `AppxManifest.xml` 无占位符。
- [ ] 在干净 Windows 10 2004+ x64 与 arm64 环境各安装并启动一次；不需要管理员权限。
- [ ] 检查包内图标、`Properties/Logo` 和各尺寸资产。
- [ ] 复核 `.mp4` 关联并做双击测试。
- [ ] 复核硬编码中文显示名和“跨平台”描述。
- [ ] 复核 GitHub 更新检查的披露与行为。
- [ ] 复核酷我 HTTP 风险并决定保留、改 HTTPS 还是禁用。
- [ ] 确认隐私政策 URL 在推送后可以公开访问；三语 listing 与截图已上传。
- [ ] 上传 `.msixbundle` 到 Partner Center，等待包验证；不要跳过失败项。
- [ ] 认证通过前不宣称已上架或已获评级。

## 9. 结论

以上内容均为发布前检查项。本文没有修改任何源码、清单、工作流或打包脚本；如果检查发现需要变更，应按正常开发流程提交变更、重新构建 MSIX、重新测试并重新上传。本文档不代表应用已经提交、通过认证、发布或获得任何年龄评级。
